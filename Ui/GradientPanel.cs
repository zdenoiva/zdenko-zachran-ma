using System.Drawing.Drawing2D;

namespace NtfsRescue.Ui;

/// <summary>Panel s viacfarebnym gradientom a jemnymi svetelnymi kruhmi.</summary>
public sealed class GradientPanel : Panel
{
    public Color From = Color.FromArgb(11, 92, 122);
    public Color Middle = Color.FromArgb(13, 96, 165);
    public Color To = Color.FromArgb(49, 46, 129);
    public Color Glow = Color.FromArgb(70, 6, 182, 212);
    public bool DrawBottomLine = true;

    public GradientPanel()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using (var brush = new LinearGradientBrush(rect, From, To, LinearGradientMode.Horizontal))
        {
            brush.InterpolationColors = new ColorBlend
            {
                Colors = new[] { From, Middle, To },
                Positions = new[] { 0f, 0.55f, 1f }
            };
            e.Graphics.FillRectangle(brush, rect);
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var glow = new SolidBrush(Glow))
        {
            e.Graphics.FillEllipse(glow, rect.Width - 250, -120, 360, 360);
            e.Graphics.FillEllipse(glow, rect.Width - 470, 55, 220, 220);
        }

        using (var soft = new SolidBrush(Color.FromArgb(34, 255, 255, 255)))
        {
            e.Graphics.FillEllipse(soft, -90, rect.Height - 110, 260, 260);
        }

        if (DrawBottomLine)
        {
            using var line = new Pen(Color.FromArgb(150, 34, 211, 238), 3f);
            e.Graphics.DrawLine(line, 0, rect.Height - 2, rect.Width, rect.Height - 2);
        }
    }
}
