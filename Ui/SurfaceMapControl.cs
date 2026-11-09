using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using NtfsRescue.Repair;

namespace NtfsRescue.Ui;

public sealed class SurfaceMapControl : Control
{
    private Bitmap? _bitmap;
    private int _columns = 256;
    private int _totalBlocks;

    public SurfaceMapControl()
    {
        DoubleBuffered = true;
        BackColor = Theme.Surface;
        MinimumSize = new Size(200, 120);
    }

    public int TotalBlocks => _totalBlocks;

    public void Reset(int totalBlocks)
    {
        _totalBlocks = Math.Max(0, totalBlocks);
        _columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(_totalBlocks)));
        _bitmap?.Dispose();
        _bitmap = null;
        if (_totalBlocks > 0)
        {
            int rows = Math.Max(1, (_totalBlocks + _columns - 1) / _columns);
            _bitmap = new Bitmap(_columns, rows, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(_bitmap);
            g.Clear(Color.FromArgb(232, 237, 243));
        }
        Invalidate();
    }

    public void SetBlock(int index, SurfaceBlock block)
    {
        if (_bitmap is null || index < 0 || index >= _totalBlocks) return;
        int x = index % _columns;
        int y = index / _columns;
        _bitmap.SetPixel(x, y, ColorFor(block));
    }

    public void SetBlocks(IReadOnlyList<SurfaceBlock> blocks)
    {
        if (_bitmap is null) return;
        for (int i = 0; i < blocks.Count && i < _totalBlocks; i++)
            SetBlock(i, blocks[i]);
        Invalidate();
    }

    private static Color ColorFor(SurfaceBlock block)
    {
        if (block.Bad) return Color.FromArgb(205, 35, 35);
        if (block.Ms >= SurfaceScanner.VerySlowMs) return Color.FromArgb(242, 130, 28);
        if (block.Ms >= SurfaceScanner.SlowMs) return Color.FromArgb(226, 187, 42);
        if (block.Ms >= 50) return Color.FromArgb(122, 190, 72);
        return Color.FromArgb(26, 166, 108);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_bitmap is null)
        {
            using var brush = new SolidBrush(Theme.Muted);
            e.Graphics.DrawString("Zatiaľ bez kontroly.", Font, brush, new PointF(12, 12));
            return;
        }

        var area = ClientRectangle;
        area.Inflate(-1, -1);
        if (area.Width <= 0 || area.Height <= 0) return;

        float scaleX = (float)area.Width / _bitmap.Width;
        float scaleY = (float)area.Height / _bitmap.Height;
        float scale = Math.Min(scaleX, scaleY);
        int width = Math.Max(1, (int)(_bitmap.Width * scale));
        int height = Math.Max(1, (int)(_bitmap.Height * scale));
        int x = area.X + (area.Width - width) / 2;
        int y = area.Y + (area.Height - height) / 2;

        var old = e.Graphics.InterpolationMode;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_bitmap, new Rectangle(x, y, width, height));
        e.Graphics.InterpolationMode = old;

        using var border = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bitmap?.Dispose();
        base.Dispose(disposing);
    }
}
