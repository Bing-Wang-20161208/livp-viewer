using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;

namespace LivpViewer;

internal sealed class PhotoCanvas : Control
{
    private Bitmap? _photo;
    private Bitmap? _fullPhoto;
    private float _zoom = 1f;
    public event EventHandler? FullResolutionRequested;

    public PhotoCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 20, 24);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public void SetPhoto(Bitmap? photo)
    {
        _photo = photo;
        _fullPhoto = null;
        ResetZoom();
    }

    public void SetFullPhoto(Bitmap fullPhoto)
    {
        _fullPhoto = fullPhoto;
        Invalidate();
    }

    public void ResetZoom()
    {
        _zoom = 1f;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_photo is null) return;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f), 0.1f, 20f);
        if (_zoom > 1.3f && _fullPhoto is null)
            FullResolutionRequested?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_photo is null) return;
        var displayed = _zoom > 1.3f && _fullPhoto is not null ? _fullPhoto : _photo;
        e.Graphics.InterpolationMode = _zoom > 1.3f
            ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
        var fit = Math.Min((float)ClientSize.Width / displayed.Width,
            (float)ClientSize.Height / displayed.Height);
        var width = displayed.Width * fit * _zoom;
        var height = displayed.Height * fit * _zoom;
        var left = (ClientSize.Width - width) / 2f;
        var top = (ClientSize.Height - height) / 2f;
        var visible = RectangleF.Intersect(
            new RectangleF(left, top, width, height),
            new RectangleF(0, 0, ClientSize.Width, ClientSize.Height));
        if (visible.Width <= 0 || visible.Height <= 0) return;

        // Draw only the visible source region when zoomed in. GDI+ otherwise
        // scales a potentially huge off-screen image on every wheel tick.
        var source = new RectangleF(
            (visible.Left - left) * displayed.Width / width,
            (visible.Top - top) * displayed.Height / height,
            visible.Width * displayed.Width / width,
            visible.Height * displayed.Height / height);
        e.Graphics.DrawImage(displayed, visible, source, GraphicsUnit.Pixel);
    }
}
