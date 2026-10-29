using System.IO.Compression;
using System.Drawing;

namespace LivpViewer;

internal static class SelfTest
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LivpViewer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "sample.livp");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using var bitmap = new Bitmap(2500, 1900);
                using var jpeg = new MemoryStream();
                bitmap.Save(jpeg, System.Drawing.Imaging.ImageFormat.Jpeg);
                jpeg.Position = 0;
                using (var output = archive.CreateEntry("folder/photo.jpg").Open()) jpeg.CopyTo(output);
                using (var output = archive.CreateEntry("folder/photo.mov").Open()) output.Write(new byte[] { 1, 2, 3, 4 });
                using (var output = archive.CreateEntry("../ignored.txt").Open()) output.WriteByte(9);
            }
            using var loaded = LivpArchive.Load(path);
            if (loaded.IsVideoPrepared)
                throw new Exception("Video must not be extracted before playback.");
            if (!loaded.HasLargerPhoto || Math.Max(loaded.Photo.Width, loaded.Photo.Height) > 2048)
                throw new Exception("Bounded preview self-test failed.");
            using var full = loaded.LoadFullPhoto();
            if (full.Width != 2500 || full.Height != 1900)
                throw new Exception("Full-resolution zoom self-test failed.");
            var firstPreparation = loaded.PrepareVideoAsync();
            var secondPreparation = loaded.PrepareVideoAsync();
            var videoPath = firstPreparation.GetAwaiter().GetResult();
            if (secondPreparation.GetAwaiter().GetResult() != videoPath)
                throw new Exception("Concurrent video preparation must share one result.");
            if (!loaded.IsVideoPrepared || !File.Exists(videoPath) ||
                !videoPath.StartsWith(Path.Combine(Path.GetTempPath(), "LivpViewer"), StringComparison.OrdinalIgnoreCase))
                throw new Exception("LIVP extraction self-test failed.");

            var invalid = Path.Combine(directory, "invalid.livp");
            using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Create))
            using (var output = archive.CreateEntry("only.mov").Open()) output.WriteByte(1);
            try
            {
                using var unexpected = LivpArchive.Load(invalid);
                throw new Exception("Missing-photo validation failed.");
            }
            catch (InvalidDataException) { }
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }
}
