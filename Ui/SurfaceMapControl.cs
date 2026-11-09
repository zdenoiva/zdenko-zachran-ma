using NtfsRescue.Repair;

namespace NtfsRescue.Ui;

/// <summary>Mapa povrchu disku: jeden štvorec = jeden väčší blok disku (podobne ako HD Tune).</summary>
public sealed class SurfaceMapControl : Control
{
    public static readonly Color Ok = Color.FromArgb(36, 173, 113);
    public static readonly Color Medium = Color.FromArgb(128, 198, 92);
    public static readonly Color Slow = Color.FromArgb(235, 184, 46);
    public static readonly Color VerySlow = Color.FromArgb(240, 136, 36);
    public static readonly Color Bad = Color.FromArgb(214, 45, 45);
    public static readonly Color Unknown = Theme.SurfaceAlt;

    private static readonly Brush[] Brushes =
    {
        new SolidBrush(Unknown),
        new SolidBrush(Ok),
        new SolidBrush(Medium),
        new SolidBrush(Slow),
        new SolidBrush(VerySlow),
        new SolidBrush(Bad)
    };

    private int[]? _cells;

    public SurfaceMapControl()
    {
        DoubleBuffered = true;
        BackColor = Unknown;
        MinimumSize = new Size(200, 140);
        ResizeRedraw = true;
    }

    public static Color ColorFor(int state) => state switch
    {
        1 => Ok,
        2 => Medium,
        3 => Slow,
        4 => VerySlow,
        5 => Bad,
        _ => Unknown
    };

    /// <summary>Nastaví bunky mapy (0 = ešte neskontrolované).</summary>
    public void SetCells(int[]? cells)
    {
        _cells = cells;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        var cells = _cells;
        if (cells is null || cells.Length == 0)
        {
            using var brush = new SolidBrush(Theme.Muted);
            g.DrawString("Zatiaľ bez kontroly.", Font, brush, new PointF(14, 12));
            return;
        }

        var area = ClientRectangle;
        if (area.Width < 8 || area.Height < 8) return;

        double ratio = (double)area.Width / area.Height;
        int columns = Math.Max(1, (int)Math.Round(Math.Sqrt(cells.Length * ratio)));
        columns = Math.Min(columns, cells.Length);
        int rows = (int)Math.Ceiling((double)cells.Length / columns);

        for (int i = 0; i < cells.Length; i++)
        {
            int state = cells[i];
            if (state <= 0) continue;
            int column = i % columns;
            int row = i / columns;
            int x0 = area.X + (int)((long)column * area.Width / columns);
            int x1 = area.X + (int)((long)(column + 1) * area.Width / columns);
            int y0 = area.Y + (int)((long)row * area.Height / rows);
            int y1 = area.Y + (int)((long)(row + 1) * area.Height / rows);
            g.FillRectangle(Brushes[Math.Clamp(state, 0, 5)], x0, y0,
                Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
        }
    }
}
