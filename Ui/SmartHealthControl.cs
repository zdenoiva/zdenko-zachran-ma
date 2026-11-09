using System.Drawing.Drawing2D;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

/// <summary>Grafické hodnotenie stavu disku z S.M.A.R.T. (podobne ako CrystalDiskInfo).</summary>
public sealed class SmartHealthControl : UserControl
{
    private readonly HealthGauge _gauge = new();
    private readonly Label _temp = new();
    private readonly Label _life = new();
    private readonly Label _hours = new();
    private readonly Label _sectors = new();
    private readonly Label _data = new();

    public SmartHealthControl()
    {
        Dock = DockStyle.Top;
        AutoSize = false;
        BackColor = Theme.Surface;
        Padding = new Padding(14, 12, 14, 12);
        MinimumSize = new Size(310, 0);
        Size = new Size(314, 214);
        BuildUi();
        SetData(null);
        Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = "STAV DISKU · S.M.A.R.T.",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8f),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Surface
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _gauge.Size = new Size(106, 106);
        _gauge.Margin = new Padding(0, 4, 10, 0);
        body.Controls.Add(_gauge, 0, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 8, 0, 0)
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 0; i < 3; i++)
            metrics.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddMetric(metrics, 0, "Teplota", _temp);
        AddMetric(metrics, 1, "Životnosť", _life);
        AddMetric(metrics, 2, "Hodiny", _hours);
        body.Controls.Add(metrics, 1, 0);
        root.Controls.Add(body, 0, 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _sectors.AutoSize = true;
        _sectors.Font = Theme.Small;
        _sectors.ForeColor = Theme.Muted;
        _sectors.Margin = new Padding(0, 0, 0, 2);
        footer.Controls.Add(_sectors, 0, 0);

        _data.AutoSize = true;
        _data.MaximumSize = new Size(286, 0);
        _data.Font = Theme.Small;
        _data.ForeColor = Theme.Muted;
        _data.Margin = new Padding(0);
        footer.Controls.Add(_data, 0, 1);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);
    }

    private static void AddMetric(TableLayoutPanel panel, int row, string caption, Label value)
    {
        panel.Controls.Add(new Label
        {
            Text = caption,
            AutoSize = true,
            Font = new Font("Segoe UI", 8f),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 2, 8, 2)
        }, 0, row);

        value.Text = "—";
        value.AutoSize = true;
        value.Font = new Font("Segoe UI Semibold", 10.5f);
        value.ForeColor = Theme.Text;
        value.Margin = new Padding(0, 0, 0, 2);
        panel.Controls.Add(value, 1, row);
    }

    public void SetData(SmartInfo? info)
    {
        if (info is null || !info.Available)
        {
            _gauge.Percent = -1;
            _gauge.Caption = "nedostupné";
            _gauge.Accent = Theme.Muted;
            _gauge.Invalidate();
            _temp.Text = "—";
            _life.Text = "—";
            _hours.Text = "—";
            _temp.ForeColor = Theme.Muted;
            _life.ForeColor = Theme.Muted;
            _hours.ForeColor = Theme.Muted;
            _sectors.Text = "S.M.A.R.T. údaje nie sú dostupné.";
            _sectors.ForeColor = Theme.Muted;
            _data.Text = "";
            return;
        }

        _gauge.Percent = info.HealthScore;
        _gauge.Caption = info.HealthLabel;
        _gauge.Accent = RatingColor(info.Rating);
        _gauge.Invalidate();

        _temp.Text = info.Temperature > 0 ? $"{info.Temperature} °C" : "—";
        _temp.ForeColor = info.Temperature switch
        {
            >= 70 => Theme.Danger,
            >= 56 => Theme.Warning,
            _ => Theme.Text
        };

        _life.Text = info.RemainingLife >= 0 ? $"{info.RemainingLife} %" : "—";
        _life.ForeColor = info.RemainingLife switch
        {
            >= 0 and < 30 => Theme.Danger,
            >= 0 and < 60 => Theme.Warning,
            _ => Theme.Text
        };

        _hours.Text = info.PowerOnHours > 0
            ? $"{info.PowerOnHours:N0} h"
            : "—";
        _hours.ForeColor = Theme.Text;

        var problems = new List<string>();
        if (info.ReallocatedSectors > 0) problems.Add($"presunuté {info.ReallocatedSectors:N0}");
        if (info.PendingSectors > 0) problems.Add($"nestabilné {info.PendingSectors:N0}");
        if (info.UncorrectableSectors > 0) problems.Add($"neopraviteľné {info.UncorrectableSectors:N0}");
        if (info.MediaErrors > 0) problems.Add($"chyby média {info.MediaErrors:N0}");
        if (info.CrcErrors > 0) problems.Add($"CRC {info.CrcErrors:N0}");
        if (info.CriticalWarning != 0)
            problems.Add("NVMe varovanie 0x" + info.CriticalWarning.ToString("X2"));

        if (problems.Count == 0)
        {
            _sectors.Text = "Sektory: bez chýb";
            _sectors.ForeColor = Theme.Success;
        }
        else
        {
            _sectors.Text = "Sektory: " + string.Join(" · ", problems);
            _sectors.ForeColor = Theme.Danger;
        }

        var details = new List<string>();
        if (info.PowerOnDays > 0) details.Add($"{info.PowerOnDays:N0} dní v prevádzke");
        if (info.Nvme && info.HostWriteBytes > 0)
            details.Add($"zápis {Fmt.Bytes((long)Math.Min(long.MaxValue, info.HostWriteBytes))}");
        if (info.Nvme && info.HostReadBytes > 0)
            details.Add($"čítanie {Fmt.Bytes((long)Math.Min(long.MaxValue, info.HostReadBytes))}");
        if (!info.Nvme && info.AvailableSpare >= 0)
            details.Add($"náhradné bloky {info.AvailableSpare} %");
        _data.Text = string.Join(" · ", details);
    }

    public static Color RatingColor(SmartRating rating) => rating switch
    {
        SmartRating.Good => Theme.Success,
        SmartRating.Caution => Theme.Warning,
        SmartRating.Bad => Theme.Danger,
        _ => Theme.Muted
    };

    private sealed class HealthGauge : Control
    {
        public int Percent = -1;
        public string Caption = "—";
        public Color Accent = Theme.Muted;

        public HealthGauge()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int size = Math.Min(Width, Height) - 14;
            if (size < 20) return;
            var rect = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);

            using (var background = new Pen(Theme.Border, 9))
                g.DrawArc(background, rect, -90, 360);

            if (Percent >= 0)
            {
                using var pen = new Pen(Accent, 9)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                g.DrawArc(pen, rect, -90, (float)(360.0 * Math.Clamp(Percent, 0, 100) / 100));
            }

            var area = new Rectangle(0, 0, Width, Height);
            using var big = new Font("Segoe UI Semibold", 16f);
            using var small = new Font("Segoe UI Semibold", 8f);
            var bigText = Percent >= 0 ? Percent + " %" : "—";
            var bigSize = TextRenderer.MeasureText(bigText, big);
            int centerY = area.Height / 2;
            TextRenderer.DrawText(g, bigText, big,
                new Rectangle(0, centerY - bigSize.Height + 4, area.Width, bigSize.Height),
                Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Caption, small,
                new Rectangle(0, centerY + 4, area.Width, 18),
                Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }
}
