using System.IO.Compression;
using System.Drawing;
using System.Diagnostics;
using ImageMagick;

namespace LivpViewer;

internal sealed class LivpArchive : IDisposable
{
    private const long MaxPhotoBytes = 150L * 1024 * 1024;
    private const long MaxVideoBytes = 2L * 1024 * 1024 * 1024;
    private const uint PreviewMaxSide = 2048;

    private readonly string _sourcePath;
    private readonly string _photoEntryName;
    private readonly string _videoEntryName;
    private readonly object _videoGate = new();
    private readonly CancellationTokenSource _videoCancellation = new();
    private string? _tempDirectory;
    private volatile string? _videoPath;
    private Task<string>? _videoPreparationTask;
    private bool _disposed;
    private int _disposeRequested;
    public Bitmap Photo { get; }
    public bool HasLargerPhoto { get; }
    public long VideoBytes { get; }
    public long SourceLength { get; }
    public DateTime SourceLastWriteUtc { get; }
    public bool IsVideoPrepared => _videoPath is not null;
    public Task<string> PrepareVideoAsync()
    {
        lock (_videoGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _videoPreparationTask ??= Task.Run(PrepareVideo);
        }
    }
    public TimeSpan LoadDuration { get; }
    public TimeSpan DecodeDuration { get; }

    private LivpArchive(Bitmap photo, string sourcePath, string photoEntryName, string videoEntryName,
        bool hasLargerPhoto, long videoBytes, long sourceLength, DateTime sourceLastWriteUtc,
        TimeSpan loadDuration, TimeSpan decodeDuration)
    {
        Photo = photo;
        _sourcePath = sourcePath;
        _photoEntryName = photoEntryName;
        _videoEntryName = videoEntryName;
        HasLargerPhoto = hasLargerPhoto;
        VideoBytes = videoBytes;
        SourceLength = sourceLength;
        SourceLastWriteUtc = sourceLastWriteUtc;
        LoadDuration = loadDuration;
        DecodeDuration = decodeDuration;
    }

    public static LivpArchive Load(string path)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!File.Exists(path) || !path.EndsWith(".livp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择存在的 .livp 文件。");

        var sourceInfo = new FileInfo(path);
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > 4096)
            throw new InvalidDataException("LIVP 容器包含过多文件。");

        var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
        var photo = files.Where(e => HasExtension(e.Name, ".heic", ".heif", ".jpg", ".jpeg", ".png"))
            .OrderBy(e => PhotoPriority(e.Name)).ThenByDescending(e => e.Length).FirstOrDefault();
        if (photo is null)
            throw new InvalidDataException("LIVP 中未找到 HEIC/JPEG 静态照片。");

        var photoStem = Path.GetFileNameWithoutExtension(photo.Name);
        var video = files.Where(e => HasExtension(e.Name, ".mov", ".mp4"))
            .OrderBy(e => string.Equals(Path.GetFileNameWithoutExtension(e.Name), photoStem,
                StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(e => e.Length).FirstOrDefault();
        if (video is null)
            throw new InvalidDataException("LIVP 中未找到 MOV/MP4 实况视频。");
        if (photo.Length <= 0 || photo.Length > MaxPhotoBytes || video.Length <= 0 || video.Length > MaxVideoBytes)
            throw new InvalidDataException("LIVP 媒体文件为空或超出安全大小限制。");

        Bitmap? bitmap = null;
        TimeSpan decodeDuration = TimeSpan.Zero;
        var hasLargerPhoto = false;
        try
        {
            using (var photoInput = photo.Open())
            using (var bounded = new MemoryStream())
            {
                CopyBounded(photoInput, bounded, MaxPhotoBytes);
                bounded.Position = 0;
                var decodeStart = stopwatch.Elapsed;
                using var image = new MagickImage(bounded);
                image.AutoOrient();
                hasLargerPhoto = Math.Max(image.Width, image.Height) > PreviewMaxSide;
                if (hasLargerPhoto)
                    image.Resize(new MagickGeometry(PreviewMaxSide, PreviewMaxSide));
                bitmap = image.ToBitmap();
                decodeDuration = stopwatch.Elapsed - decodeStart;
            }
            return new LivpArchive(bitmap!, path, photo.FullName, video.FullName,
                hasLargerPhoto, video.Length, sourceInfo.Length, sourceInfo.LastWriteTimeUtc,
                stopwatch.Elapsed, decodeDuration);
        }
        catch
        {
            bitmap?.Dispose();
            throw;
        }
    }

    public Bitmap LoadFullPhoto()
    {
        using var zip = ZipFile.OpenRead(_sourcePath);
        var photo = zip.GetEntry(_photoEntryName)
            ?? throw new InvalidDataException("LIVP 中的静态照片已不存在。");
        if (photo.Length <= 0 || photo.Length > MaxPhotoBytes)
            throw new InvalidDataException("静态照片为空或超出安全大小限制。");
        using var input = photo.Open();
        using var bounded = new MemoryStream();
        CopyBounded(input, bounded, MaxPhotoBytes);
        bounded.Position = 0;
        using var image = new MagickImage(bounded);
        image.AutoOrient();
        return image.ToBitmap();
    }

    private string PrepareVideo()
    {
        lock (_videoGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _videoCancellation.Token.ThrowIfCancellationRequested();
            if (_videoPath is not null) return _videoPath;

            using var zip = ZipFile.OpenRead(_sourcePath);
            var video = zip.GetEntry(_videoEntryName)
                ?? throw new InvalidDataException("LIVP 中的实况视频已不存在。");
            if (video.Length <= 0 || video.Length > MaxVideoBytes)
                throw new InvalidDataException("实况视频为空或超出安全大小限制。");

            var tempDirectory = Path.Combine(Path.GetTempPath(), "LivpViewer", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                var videoPath = Path.Combine(tempDirectory, "live" + Path.GetExtension(video.Name).ToLowerInvariant());
                using (var input = video.Open())
                using (var output = new FileStream(videoPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    CopyBounded(input, output, MaxVideoBytes, _videoCancellation.Token);
                _tempDirectory = tempDirectory;
                _videoPath = videoPath;
                return videoPath;
            }
            catch
            {
                try { Directory.Delete(tempDirectory, true); } catch { }
                throw;
            }
        }
    }

    private static void CopyBounded(Stream input, Stream output, long limit,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[1024 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > limit) throw new InvalidDataException("LIVP 媒体文件超出安全大小限制。");
            output.Write(buffer, 0, read);
        }
    }

    private static bool HasExtension(string name, params string[] extensions) =>
        extensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private static int PhotoPriority(string name) =>
        HasExtension(name, ".heic", ".heif") ? 0 : 1;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0)
            _videoCancellation.Cancel();
        lock (_videoGate)
        {
            if (_disposed) return;
            _disposed = true;
            Photo.Dispose();
            if (_tempDirectory is not null)
                try { Directory.Delete(_tempDirectory, true); } catch { /* Cleaned up on a later run. */ }
            _videoCancellation.Dispose();
        }
    }
}
