using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace LivpViewer;

internal sealed class MainForm : Form, IMessageFilter
{
    private readonly LibVLC _vlc = new();
    private readonly MediaPlayer _player;
    private readonly PhotoCanvas _photo = new() { Dock = DockStyle.Fill };
    private readonly VideoView _video = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.FromArgb(165, 171, 181),
        Font = new Font("Segoe UI", 15), Text = "拖入 .livp 文件，或点击“打开”"
    };
    private readonly Label _name = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.White, Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoEllipsis = true };
    private readonly Label _status = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(165, 171, 181), Font = new Font("Segoe UI", 9) };
    private readonly Button _playButton;
    private readonly System.Windows.Forms.Timer _holdTimer = new() { Interval = 35 };
    private readonly Dictionary<string, LivpArchive> _recentPhotos = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recentOrder = new();

    private LivpArchive? _archive;
    private LivpArchive? _mediaArchive;
    private Media? _activeMedia;
    private Bitmap? _fullPhoto;
    private string? _currentPath;
    private string? _requestedPath;
    private string[] _siblings = [];
    private int _loadVersion;
    private int _playbackVersion;
    private int _fullRequestVersion = -1;
    private bool _playing;
    private bool _playerReady;
    private long _videoPrepareMs;
    private long _playStartTicks;
    private bool _mouseHolding;
    private long _mouseDownTicks;

    public MainForm(string? initialPath)
    {
        Text = "LIVP Viewer";
        MinimumSize = new Size(640, 480);
        Size = new Size(1080, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(25, 27, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        KeyPreview = true;

        _player = new MediaPlayer(_vlc);
        _player.EnableHardwareDecoding = true;
        _player.EnableMouseInput = false;
        _video.MediaPlayer = _player;
        _player.Playing += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed || _activeMedia is null ||
                        !ReferenceEquals(_mediaArchive, _archive) ||
                        _player.State != VLCState.Playing) return;
                    _playerReady = true;
                    if (!_playing) _player.SetPause(true);
                    else _status.Text = $"实况播放中（准备 {_videoPrepareMs} ms，启动 {Stopwatch.GetElapsedTime(_playStartTicks).TotalMilliseconds:0} ms）· 松开长按或再次点击返回照片";
                }));
        };
        _player.EndReached += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && _playing && _player.State == VLCState.Ended)
                        ReturnToStill(resetPlayer: true);
                }));
        };
        _player.EncounteredError += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed || !_playing || _player.State != VLCState.Error) return;
                    ReturnToStill(resetPlayer: true);
                    _status.Text = "视频播放失败；此设备可能不支持该 MOV 的硬件解码。";
                }));
        };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(root);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5,
            Padding = new Padding(12, 10, 12, 8), BackColor = Color.FromArgb(35, 38, 45) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        root.Controls.Add(toolbar, 0, 0);

        var openButton = MakeButton("打开");
        openButton.Click += async (_, _) => await PickAndOpenAsync();
        toolbar.Controls.Add(openButton, 0, 0);
        var previous = MakeButton("←");
        previous.Click += async (_, _) => await NavigateAsync(-1);
        toolbar.Controls.Add(previous, 1, 0);
        var next = MakeButton("→");
        next.Click += async (_, _) => await NavigateAsync(1);
        toolbar.Controls.Add(next, 2, 0);
        _name.Dock = DockStyle.Fill;
        _name.Padding = new Padding(12, 0, 0, 0);
        toolbar.Controls.Add(_name, 3, 0);
        _playButton = MakeButton("播放实况");
        _playButton.Click += (_, _) => TogglePlayback();
        toolbar.Controls.Add(_playButton, 4, 0);

        var viewport = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 20, 24) };
        root.Controls.Add(viewport, 0, 1);
        viewport.Controls.Add(_photo);
        viewport.Controls.Add(_video);
        viewport.Controls.Add(_empty);
        _empty.BringToFront();

        _status.Dock = DockStyle.Fill;
        _status.Padding = new Padding(16, 0, 0, 0);
        _status.Text = "支持 HEIC + MOV · 左右键浏览 · 滚轮缩放";
        root.Controls.Add(_status, 0, 2);

        _photo.MouseDown += PhotoMouseDown;
        _photo.MouseEnter += (_, _) => _photo.Focus();
        _photo.FullResolutionRequested += RequestFullResolution;
        _video.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && _playing && !_mouseHolding) ReturnToStill(); };
        _holdTimer.Tick += (_, _) => CheckMouseRelease();
        KeyDown += OnShortcut;
        Application.AddMessageFilter(this);
        EnableDrop(this);
        EnableDrop(viewport);
        EnableDrop(_photo);
        EnableDrop(_video);
        EnableDrop(_empty);

        if (!string.IsNullOrWhiteSpace(initialPath))
            Shown += async (_, _) => await OpenAsync(initialPath);
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(54, 58, 67), ForeColor = Color.White,
        Margin = new Padding(3, 2, 3, 2), Cursor = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 }
    };

    private void EnableDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true &&
                e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
                paths.Any(p => p.EndsWith(".livp", StringComparison.OrdinalIgnoreCase))
                ? DragDropEffects.Copy : DragDropEffects.None;
        };
        control.DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                var path = paths.FirstOrDefault(p => p.EndsWith(".livp", StringComparison.OrdinalIgnoreCase));
                if (path is not null) await OpenAsync(path);
            }
        };
    }

    private async Task PickAndOpenAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Live Photo (*.livp)|*.livp|所有文件 (*.*)|*.*",
            Title = "打开 LIVP", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await OpenAsync(dialog.FileName);
    }

    private async Task OpenAsync(string path)
    {
        var version = ++_loadVersion;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) { ShowError(ex.Message); return; }

        _requestedPath = fullPath;
        ReturnToStill(resetPlayer: true);
        _status.Text = "正在读取 " + Path.GetFileName(fullPath) + "…";
        LivpArchive loaded;
        var fromCache = TryGetCached(fullPath);
        try { loaded = fromCache ?? await Task.Run(() => LivpArchive.Load(fullPath)); }
        catch (Exception ex)
        {
            if (version == _loadVersion && !IsDisposed)
            {
                _requestedPath = _currentPath;
                ShowError(ex.Message);
            }
            return;
        }
        if (version != _loadVersion || IsDisposed)
        {
            if (fromCache is null) loaded.Dispose();
            return;
        }

        ReturnToStill(resetPlayer: true);
        _photo.SetPhoto(null);
        _fullPhoto?.Dispose();
        _fullPhoto = null;
        _fullRequestVersion = -1;
        _archive = loaded;
        Remember(fullPath, loaded);
        _photo.SetPhoto(loaded.Photo);
        _currentPath = fullPath;
        _requestedPath = fullPath;
        _name.Text = Path.GetFileName(fullPath);
        _empty.Visible = false;
        _photo.Visible = true;
        _photo.BringToFront();
        try
        {
            _siblings = Directory.EnumerateFiles(Path.GetDirectoryName(fullPath)!, "*.livp")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (IOException) { _siblings = [fullPath]; }
        catch (UnauthorizedAccessException) { _siblings = [fullPath]; }
        _status.Text = fromCache is null
            ? $"照片已显示（{loaded.LoadDuration.TotalMilliseconds:0} ms，解码 {loaded.DecodeDuration.TotalMilliseconds:0} ms）· 按住或轻点播放 · ← / → 浏览"
            : "照片已显示（内存缓存）· 按住或轻点播放 · ← / → 浏览";
        if (loaded.VideoBytes <= 32L * 1024 * 1024 && !loaded.IsVideoPrepared)
            _ = WarmVideoAsync(loaded);
    }

    private static async Task WarmVideoAsync(LivpArchive archive)
    {
        try { await archive.PrepareVideoAsync(); }
        catch { /* Playback reports the error when requested. */ }
    }

    private LivpArchive? TryGetCached(string path)
    {
        if (!_recentPhotos.TryGetValue(path, out var cached)) return null;
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == cached.SourceLength &&
                info.LastWriteTimeUtc == cached.SourceLastWriteUtc ? cached : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void Remember(string path, LivpArchive archive)
    {
        if (_recentPhotos.TryGetValue(path, out var replaced) && !ReferenceEquals(replaced, archive))
            _ = Task.Run(replaced.Dispose);
        _recentPhotos[path] = archive;
        _recentOrder.Remove(path);
        _recentOrder.AddLast(path);
        while (_recentOrder.Count > 3)
        {
            var oldest = _recentOrder.First!.Value;
            _recentOrder.RemoveFirst();
            _recentPhotos.Remove(oldest, out var evicted);
            if (evicted is not null) _ = Task.Run(evicted.Dispose);
        }
    }

    private async void RequestFullResolution(object? sender, EventArgs e)
    {
        var archive = _archive;
        var version = _loadVersion;
        if (archive is null || !archive.HasLargerPhoto || _fullPhoto is not null ||
            _fullRequestVersion == version) return;
        _fullRequestVersion = version;
        try
        {
            var full = await Task.Run(archive.LoadFullPhoto);
            if (IsDisposed || version != _loadVersion || !ReferenceEquals(archive, _archive))
                full.Dispose();
            else
            {
                _fullPhoto = full;
                _photo.SetFullPhoto(full);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == _loadVersion)
                _status.Text = "高清照片加载失败，仍可使用预览：" + ex.Message;
        }
    }

    private async Task NavigateAsync(int direction)
    {
        var startingPath = _requestedPath ?? _currentPath;
        if (startingPath is null || _siblings.Length == 0) return;
        var index = Array.FindIndex(_siblings, p => string.Equals(p, startingPath,
            StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        await OpenAsync(_siblings[(index + direction + _siblings.Length) % _siblings.Length]);
    }

    private void PhotoMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _archive is null) return;
        _mouseDownTicks = Stopwatch.GetTimestamp();
        _mouseHolding = true;
        StartPlayback();
        if (_playing) _holdTimer.Start();
        else _mouseHolding = false;
    }

    private void CheckMouseRelease()
    {
        if (!_mouseHolding || Control.MouseButtons.HasFlag(MouseButtons.Left)) return;
        _holdTimer.Stop();
        _mouseHolding = false;
        var duration = Stopwatch.GetElapsedTime(_mouseDownTicks);
        if (duration >= TimeSpan.FromMilliseconds(300)) ReturnToStill();
    }

    private void TogglePlayback()
    {
        _mouseHolding = false;
        _holdTimer.Stop();
        if (_playing) ReturnToStill();
        else StartPlayback();
    }

    private async void StartPlayback()
    {
        if (_archive is null || _playing) return;
        var archive = _archive;
        var version = ++_playbackVersion;
        _playing = true;
        _playButton.Text = "返回照片";
        _status.Text = "正在准备实况视频…";
        try
        {
            // ZIP extraction can be slow on a cold file. Never wait for it on the UI thread.
            var preparationStart = Stopwatch.GetTimestamp();
            var videoPath = await archive.PrepareVideoAsync();
            if (IsDisposed || !_playing || version != _playbackVersion ||
                !ReferenceEquals(archive, _archive)) return;
            _videoPrepareMs = (long)Stopwatch.GetElapsedTime(preparationStart).TotalMilliseconds;
            if (!ReferenceEquals(_mediaArchive, archive) || _activeMedia is null)
            {
                ReleasePlayer();
                _activeMedia = new Media(_vlc, new Uri(videoPath));
                _player.Media = _activeMedia;
                _mediaArchive = archive;
                _playerReady = false;
            }
            _video.Visible = true;
            _video.BringToFront();
            _playStartTicks = Stopwatch.GetTimestamp();
            if (_playerReady)
            {
                _player.Time = 0;
                _player.SetPause(false);
            }
            else if (!_player.Play())
                throw new InvalidOperationException("视频播放器无法启动。");
            _status.Text = $"视频准备 {_videoPrepareMs} ms，正在启动播放…";
        }
        catch (Exception ex)
        {
            if (IsDisposed || version != _playbackVersion) return;
            ReturnToStill(resetPlayer: true);
            ShowError("无法播放实况视频：" + ex.Message);
        }
    }

    private void ReturnToStill(bool resetPlayer = false)
    {
        ++_playbackVersion;
        _holdTimer.Stop();
        _mouseHolding = false;
        // If opening has not reached Playing yet, a pause request may have no
        // effect. Stop that attempt so audio cannot start behind the photo.
        if (_activeMedia is not null && !_playerReady) resetPlayer = true;
        if (_playing && !resetPlayer && _activeMedia is not null)
            _player.SetPause(true);
        _playing = false;
        _photo.Visible = true;
        _photo.BringToFront();
        _playButton.Text = "播放实况";
        if (resetPlayer) ReleasePlayer();
        if (_archive is not null)
            _status.Text = "按住照片播放，松开返回；轻点切换播放 · 滚轮缩放 · ← / → 浏览";
    }

    private void ReleasePlayer()
    {
        _player.Stop();
        _player.Media = null;
        _activeMedia?.Dispose();
        _activeMedia = null;
        _mediaArchive = null;
        _playerReady = false;
    }

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left: _ = NavigateAsync(-1); break;
            case Keys.Right: _ = NavigateAsync(1); break;
            case Keys.Space: TogglePlayback(); break;
            case Keys.Escape: ReturnToStill(); break;
            case Keys.O when e.Control: _ = PickAndOpenAsync(); break;
            case Keys.D0 when e.Control: _photo.ResetZoom(); break;
            default: return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    public bool PreFilterMessage(ref Message m)
    {
        const int wmLeftButtonDown = 0x0201;
        if (m.Msg == wmLeftButtonDown && _playing && !_mouseHolding && _video.Visible &&
            _video.RectangleToScreen(_video.ClientRectangle).Contains(Cursor.Position))
        {
            ReturnToStill();
            return true;
        }
        return false;
    }

    private void ShowError(string message)
    {
        _status.Text = "错误：" + message;
        MessageBox.Show(this, message, "LIVP Viewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Application.RemoveMessageFilter(this);
        ReturnToStill(resetPlayer: true);
        _holdTimer.Dispose();
        _photo.SetPhoto(null);
        _fullPhoto?.Dispose();
        foreach (var archive in _recentPhotos.Values) archive.Dispose();
        _video.MediaPlayer = null;
        _player.Dispose();
        _vlc.Dispose();
        base.OnFormClosed(e);
    }
}
