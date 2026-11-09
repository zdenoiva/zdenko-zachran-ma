using System.Drawing.Drawing2D;

namespace NtfsRescue.Ui;

public static class Theme
{
    public static readonly Color Background = Color.FromArgb(246, 248, 251);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(250, 251, 253);
    public static readonly Color Border = Color.FromArgb(224, 229, 236);
    public static readonly Color Text = Color.FromArgb(28, 38, 52);
    public static readonly Color Muted = Color.FromArgb(105, 117, 134);
    public static readonly Color Accent = Color.FromArgb(0, 145, 160);
    public static readonly Color AccentDark = Color.FromArgb(0, 112, 125);
    public static readonly Color AccentSoft = Color.FromArgb(226, 246, 248);
    public static readonly Color Success = Color.FromArgb(35, 145, 95);
    public static readonly Color SuccessSoft = Color.FromArgb(231, 247, 239);
    public static readonly Color Warning = Color.FromArgb(190, 120, 0);
    public static readonly Color WarningSoft = Color.FromArgb(255, 247, 230);
    public static readonly Color Danger = Color.FromArgb(190, 60, 60);
    public static readonly Color DangerSoft = Color.FromArgb(254, 236, 236);
    public static readonly Color Sidebar = Color.FromArgb(22, 34, 47);
    public static readonly Color SidebarText = Color.FromArgb(220, 230, 240);

    public static Font H1 { get; } = new("Segoe UI Semibold", 20f);
    public static Font H2 { get; } = new("Segoe UI Semibold", 14f);
    public static Font H3 { get; } = new("Segoe UI Semibold", 11f);
    public static Font Body { get; } = new("Segoe UI", 9.5f);
    public static Font Small { get; } = new("Segoe UI", 8.5f);
    public static Font Mono { get; } = new("Consolas", 9f);

    public static Button PrimaryButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 38,
            MinimumSize = new Size(150, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f),
            Cursor = Cursors.Hand,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 4, 10, 4)
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentDark;
        b.FlatAppearance.MouseDownBackColor = AccentDark;
        return b;
    }

    public static Button SecondaryButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 38,
            MinimumSize = new Size(130, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = Text,
            Font = new Font("Segoe UI Semibold", 9.5f),
            Cursor = Cursors.Hand,
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, 4, 10, 4)
        };
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = SurfaceAlt;
        return b;
    }

    public static Button DangerButton(string text)
    {
        var b = PrimaryButton(text);
        b.BackColor = Danger;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(160, 45, 45);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 45, 45);
        return b;
    }

    public static Label Heading(string text) => new()
    {
        Text = text,
        Font = H2,
        ForeColor = Text,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 6)
    };

    public static Label MutedLabel(string text) => new()
    {
        Text = text,
        Font = Body,
        ForeColor = Muted,
        AutoSize = true,
        MaximumSize = new Size(900, 0),
        Margin = new Padding(0, 0, 0, 4)
    };

    public static Panel Card()
    {
        var p = new Panel
        {
            BackColor = Surface,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 0, 12),
            Dock = DockStyle.Top
        };
        p.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        };
        return p;
    }

    public static void Round(Control control, int radius = 10)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(control.Width - d - 1, 0, d, d, 270, 90);
        path.AddArc(control.Width - d - 1, control.Height - d - 1, d, d, 0, 90);
        path.AddArc(0, control.Height - d - 1, d, d, 90, 90);
        path.CloseFigure();
        control.Region = new Region(path);
    }
}
