using System.Globalization;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

/// <summary>Karta jedného testu povrchu — kompaktná, s rozbaliteľným detailom.</summary>
public sealed class SurfaceJobCard : Panel
{
    private readonly SurfaceJob _job;
    private readonly TableLayoutPanel _layout = new();
    private readonly Label _title = new();
    private readonly Label _meta = new();
    private readonly Label _badge = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _percent = new();
    private readonly Label _speed = new();
    private readonly Label _eta = new();
    private readonly Label _bad = new();
    private readonly Label _statusText = new();
    private readonly SurfaceMapControl _map = new();
    private readonly Panel _mapHost = new();
    private readonly TableLayoutPanel _metrics = new();
    private readonly FlowLayoutPanel _legend = new();
    private readonly Panel _logHost = new();
    private readonly RichTextBox _log = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnHeaderStop = new();
    private readonly Button _btnDetail = new();
    private readonly Button _btnReport = new();
    private readonly Button _btnRepair = new();
    private readonly Button _btnRemove = new();

    private readonly int _appDisk;
    private bool _expanded;
    private int _lastLogVersion;

    public SurfaceJob Job => _job;

    public event Action<SurfaceJobCard>? DetailRequested;
    public event Action<SurfaceJobCard>? StopRequested;
    public event Action<SurfaceJobCard>? RemoveRequested;
    public event Action<SurfaceJobCard>? RepairRequested;
    public event Action<SurfaceJobCard>? ReportRequested;

    public SurfaceJobCard(SurfaceJob job)
    {
        _job = job;
        _appDisk = VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "");

        BackColor = Theme.Surface;
        Padding = new Padding(12);
        Margin = new Padding(0, 0, 0, 10);
        MinimumSize = new Size(420, 0);

        Build();
        SetExpanded(false);
        RefreshFromJob();
    }

    private void Build()
    {
        Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        _layout.Dock = DockStyle.Top;
        _layout.AutoSize = true;
        _layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _layout.ColumnCount = 1;
        _layout.RowCount = 7;
        _layout.BackColor = Theme.Surface;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(_layout);

        _layout.Controls.Add(BuildHeader(), 0, 0);
        _layout.Controls.Add(BuildProgress(), 0, 1);
        _layout.Controls.Add(BuildMap(), 0, 2);
        _layout.Controls.Add(BuildMetrics(), 0, 3);
        _layout.Controls.Add(BuildLegend(), 0, 4);
        _layout.Controls.Add(BuildLog(), 0, 5);
        _layout.Controls.Add(BuildButtons(), 0, 6);

        _map.SetCells(_job.Cells);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, 4),
            Cursor = Cursors.Hand
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var names = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        _title.Text = $"Disk {_job.Disk.Number} · {_job.Disk.Model}";
        _title.AutoSize = true;
        _title.Font = Theme.H3;
        _title.ForeColor = Theme.Text;
        _title.Margin = new Padding(0);

        _meta.Text = $"{Fmt.Bytes(_job.Disk.Size)} · {_job.Disk.BusType} · " +
                     $"bloky {_job.FirstBlock:N0} – {_job.LastBlock:N0}";
        _meta.AutoSize = true;
        _meta.Font = Theme.Small;
        _meta.ForeColor = Theme.Muted;
        _meta.Margin = new Padding(0, 1, 0, 0);

        names.Controls.Add(_title, 0, 0);
        names.Controls.Add(_meta, 0, 1);
        header.Controls.Add(names, 0, 0);

        _badge.AutoSize = true;
        _badge.Font = new Font("Segoe UI Semibold", 8.5f);
        _badge.Padding = new Padding(8, 3, 8, 3);
        _badge.Margin = new Padding(8, 0, 0, 0);

        var right = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        _btnHeaderStop.Text = "Stop";
        StyleSmallDanger(_btnHeaderStop);
        _btnHeaderStop.Visible = false;
        _btnHeaderStop.Click += (_, _) => StopRequested?.Invoke(this);
        right.Controls.Add(_btnHeaderStop);
        right.Controls.Add(_badge);
        header.Controls.Add(right, 1, 0);

        header.Click += (_, _) =>
        {
            if ((ModifierKeys & Keys.Control) == 0) DetailRequested?.Invoke(this);
        };
        names.Click += (_, _) => DetailRequested?.Invoke(this);
        _title.Click += (_, _) => DetailRequested?.Invoke(this);
        _meta.Click += (_, _) => DetailRequested?.Invoke(this);

        return header;
    }

    private Control BuildProgress()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 2, 0, 2)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Size = new Size(300, 10);
        _progress.Margin = new Padding(0, 7, 10, 0);
        row.Controls.Add(_progress);

        _percent.AutoSize = true;
        _percent.Font = new Font("Segoe UI Semibold", 10.5f);
        _percent.ForeColor = Theme.Text;
        _percent.Margin = new Padding(0, 3, 14, 0);
        row.Controls.Add(_percent);

        _speed.AutoSize = true;
        _speed.Font = Theme.Small;
        _speed.ForeColor = Theme.Muted;
        _speed.Margin = new Padding(0, 7, 14, 0);
        row.Controls.Add(_speed);

        _eta.AutoSize = true;
        _eta.Font = Theme.Small;
        _eta.ForeColor = Theme.Muted;
        _eta.Margin = new Padding(0, 7, 14, 0);
        row.Controls.Add(_eta);

        _bad.AutoSize = true;
        _bad.Font = new Font("Segoe UI Semibold", 8.5f);
        _bad.Margin = new Padding(0, 7, 0, 0);
        row.Controls.Add(_bad);
        panel.Controls.Add(row, 0, 0);

        _statusText.AutoSize = false;
        _statusText.Dock = DockStyle.Top;
        _statusText.Height = 18;
        _statusText.AutoEllipsis = true;
        _statusText.Font = Theme.Small;
        _statusText.ForeColor = Theme.Muted;
        _statusText.Margin = new Padding(0, 2, 0, 0);
        panel.Controls.Add(_statusText, 0, 1);

        return panel;
    }

    private Control BuildMap()
    {
        _mapHost.Dock = DockStyle.Fill;
        _mapHost.BackColor = SurfaceMapControl.Unknown;
        _mapHost.Margin = new Padding(0, 4, 0, 4);
        _map.Dock = DockStyle.Fill;
        _map.MinimumSize = Size.Empty;
        _mapHost.Controls.Add(_map);
        return _mapHost;
    }

    private Control BuildMetrics()
    {
        _metrics.Dock = DockStyle.Top;
        _metrics.AutoSize = true;
        _metrics.ColumnCount = 4;
        _metrics.RowCount = 1;
        _metrics.BackColor = Theme.Surface;
        _metrics.Margin = new Padding(0, 6, 0, 0);
        for (int i = 0; i < 4; i++)
            _metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _metrics.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _metrics.Controls.Add(MetricBox("Poškodené bloky", _valDamaged, _capDamaged, Theme.Success), 0, 0);
        _metrics.Controls.Add(MetricBox("Rýchlosť", _valSpeed, null, Theme.Text), 1, 0);
        _metrics.Controls.Add(MetricBox("Pozícia", _valPosition, _capPosition, Theme.Text), 2, 0);
        _metrics.Controls.Add(MetricBox("Uplynulý čas", _valTime, _capTime, Theme.Text), 3, 0);
        return _metrics;
    }

    private Label _valDamaged = new();
    private Label _capDamaged = new();
    private Label _valSpeed = new();
    private Label _valPosition = new();
    private Label _capPosition = new();
    private Label _valTime = new();
    private Label _capTime = new();

    private static Control MetricBox(string caption, Label value, Label? sub, Color valueColor)
    {
        var box = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 8, 0)
        };
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.Controls.Add(new Label
        {
            Text = caption.ToUpperInvariant(),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 7.5f),
            ForeColor = Theme.Muted,
            Margin = new Padding(0)
        }, 0, 0);

        value.Text = "—";
        value.AutoSize = true;
        value.Font = new Font("Segoe UI Semibold", 14f);
        value.ForeColor = valueColor;
        value.Margin = new Padding(0, 1, 0, 0);
        box.Controls.Add(value, 0, 1);

        if (sub is not null)
        {
            sub.Text = "";
            sub.AutoSize = true;
            sub.Font = Theme.Small;
            sub.ForeColor = Theme.Muted;
            sub.Margin = new Padding(0, 0, 0, 0);
            box.Controls.Add(sub, 0, 2);
        }
        return box;
    }

    private Control BuildLegend()
    {
        _legend.Dock = DockStyle.Top;
        _legend.AutoSize = true;
        _legend.WrapContents = true;
        _legend.BackColor = Theme.Surface;
        _legend.Margin = new Padding(0, 4, 0, 0);
        _legend.Controls.Add(LegendRow("OK (do 50 ms)", SurfaceMapControl.Ok));
        _legend.Controls.Add(LegendRow("50 – 100 ms", SurfaceMapControl.Medium));
        _legend.Controls.Add(LegendRow("100 – 250 ms", SurfaceMapControl.Slow));
        _legend.Controls.Add(LegendRow("nad 250 ms", SurfaceMapControl.VerySlow));
        _legend.Controls.Add(LegendRow("chyba / vadný blok", SurfaceMapControl.Bad));
        _legend.Controls.Add(new Label
        {
            Text = "1 štvorec ≈ " + Fmt.Bytes(_job.CellBytes),
            AutoSize = true,
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            Margin = new Padding(4, 1, 0, 0)
        });
        return _legend;
    }

    private static Control LegendRow(string text, Color color)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 1, 14, 0)
        };
        row.Controls.Add(new Panel
        {
            Size = new Size(11, 11),
            BackColor = color,
            Margin = new Padding(0, 2, 6, 0)
        });
        row.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = Theme.Small,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 0, 0, 0)
        });
        return row;
    }

    private Control BuildLog()
    {
        _logHost.Dock = DockStyle.Fill;
        _logHost.BackColor = Theme.Surface;
        _logHost.Margin = new Padding(0, 6, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "PROTOKOL KONTROLY",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 7.5f),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.SurfaceAlt;
        _log.Font = Theme.Mono;
        _log.WordWrap = true;
        _log.ScrollBars = RichTextBoxScrollBars.Vertical;
        layout.Controls.Add(_log, 0, 1);
        _logHost.Controls.Add(layout);
        return _logHost;
    }

    private Control BuildButtons()
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 6, 0, 0)
        };

        StyleSecondary(_btnStop, "Zastaviť");
        _btnStop.Click += (_, _) => StopRequested?.Invoke(this);
        row.Controls.Add(_btnStop);

        StyleSecondary(_btnDetail, "Detail");
        _btnDetail.Click += (_, _) => DetailRequested?.Invoke(this);
        row.Controls.Add(_btnDetail);

        StyleSecondary(_btnReport, "Report");
        _btnReport.Click += (_, _) => ReportRequested?.Invoke(this);
        row.Controls.Add(_btnReport);

        StyleDanger(_btnRepair, "Oprava…");
        _btnRepair.Click += (_, _) => RepairRequested?.Invoke(this);
        row.Controls.Add(_btnRepair);

        StyleSecondary(_btnRemove, "Odstrániť");
        _btnRemove.Click += (_, _) => RemoveRequested?.Invoke(this);
        row.Controls.Add(_btnRemove);

        return row;
    }

    private static void StyleSmallDanger(Button button)
    {
        button.AutoSize = true;
        button.Height = 24;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Theme.Danger;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 8f);
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(8, 0, 8, 0);
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(160, 45, 45);
    }

    private static void StyleSecondary(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 30;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Theme.Surface;
        button.ForeColor = Theme.Text;
        button.Font = new Font("Segoe UI Semibold", 8.5f);
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(10, 0, 10, 0);
        button.Margin = new Padding(0, 0, 6, 0);
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Theme.SurfaceAlt;
    }

    private static void StyleDanger(Button button, string text)
    {
        StyleSecondary(button, text);
        button.BackColor = Theme.Danger;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(160, 45, 45);
    }

    public void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        _metrics.Visible = expanded;
        _legend.Visible = expanded;
        _logHost.Visible = expanded;
        _btnDetail.Text = expanded ? "Skryť" : "Detail";
        RecalculateHeight();
        _map.Invalidate();
    }

    private int Scale(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96.0);

    private bool _recalculating;

    private void RecalculateHeight()
    {
        if (_recalculating || _layout.RowStyles.Count < 7) return;
        _recalculating = true;
        try
        {
            _layout.RowStyles[2].Height = Scale(_expanded ? 190 : 38);
            _layout.RowStyles[3].Height = _expanded ? Scale(64) : 0;
            _layout.RowStyles[4].Height = _expanded ? Scale(28) : 0;
            _layout.RowStyles[5].Height = _expanded ? Scale(150) : 0;
            _layout.PerformLayout();
            int height = _layout.PreferredSize.Height + Padding.Vertical;
            if (height > 0 && Height != height) Height = height;
        }
        finally
        {
            _recalculating = false;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_recalculating && _layout.Width > 0) RecalculateHeight();
    }

    public void RefreshFromJob()
    {
        var s = Job.Snapshot();

        _badge.Text = s.State switch
        {
            SurfaceJobState.Running => "Prebieha",
            SurfaceJobState.Done => s.BadSectors > 0 ? $"Hotovo · {s.BadSectors:N0} vadných" : "Hotovo",
            SurfaceJobState.Stopped => "Zastavené",
            SurfaceJobState.Failed => "Chyba",
            _ => "Čaká"
        };
        (_badge.BackColor, _badge.ForeColor) = s.State switch
        {
            SurfaceJobState.Running => (Theme.AccentSoft, Theme.AccentDark),
            SurfaceJobState.Done => s.BadSectors > 0
                ? (Theme.WarningSoft, Theme.Warning)
                : (Theme.SuccessSoft, Theme.Success),
            SurfaceJobState.Failed => (Theme.DangerSoft, Theme.Danger),
            _ => (Theme.SurfaceAlt, Theme.Muted)
        };

        _progress.Value = Math.Clamp(s.Percent, 0, 100);
        _percent.Text = s.Percent + " %";
        _speed.Text = s.BytesPerSecond > 0
            ? Fmt.Bytes((long)s.BytesPerSecond) + "/s"
            : "—";
        _eta.Text = s.State == SurfaceJobState.Running
            ? "ETA " + FormatDuration(s.Eta)
            : s.State == SurfaceJobState.Done
                ? FormatDuration(s.Elapsed)
                : "—";
        _bad.Text = s.BadSectors > 0 ? $"{s.BadSectors:N0} vadných" : "0 vadných";
        _bad.ForeColor = s.BadSectors > 0 ? Theme.Danger : Theme.Success;
        _statusText.Text = string.IsNullOrWhiteSpace(s.Error) ? s.Status : s.Error!;
        _statusText.ForeColor = string.IsNullOrWhiteSpace(s.Error) ? Theme.Muted : Theme.Danger;

        double damage = s.KnownCells > 0 ? (double)s.BadCells * 100 / s.KnownCells : 0;
        _valDamaged.Text = damage.ToString("0.0", CultureInfo.CurrentCulture) + " %";
        _valDamaged.ForeColor = s.BadCells > 0 ? Theme.Danger : Theme.Success;
        _capDamaged.Text = $"{s.BadCells:N0} blokov · {s.BadSectors:N0} sektorov";
        _valSpeed.Text = s.BytesPerSecond > 0
            ? Fmt.Bytes((long)s.BytesPerSecond) + "/s"
            : "—";
        _valPosition.Text = Fmt.Bytes(s.Position);
        _capPosition.Text = Math.Min(100,
            s.Position * 100 / Math.Max(1, Job.Disk.Size)) + " % disku";
        _valTime.Text = FormatDuration(s.Elapsed);
        _capTime.Text = s.State == SurfaceJobState.Running
            ? "zostáva " + FormatDuration(s.Eta)
            : s.State == SurfaceJobState.Done ? "dokončené" : "";

        _btnStop.Enabled = s.State == SurfaceJobState.Running;
        _btnHeaderStop.Visible = s.State == SurfaceJobState.Running;
        _btnReport.Enabled = Job.HasData;
        _btnRepair.Enabled = !Job.IsActive && s.BadSectors > 0 &&
                             !Job.Disk.IsCurrentSystem && _appDisk != Job.Disk.Number;

        if (s.LogVersion != _lastLogVersion)
        {
            var log = Job.GetLog();
            _log.Text = string.Join(Environment.NewLine, log);
            _lastLogVersion = s.LogVersion;
            if (_expanded)
            {
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }
        }

        if (s.State == SurfaceJobState.Running) _map.Invalidate();
    }

    private static string FormatDuration(TimeSpan value)
        => value.TotalHours >= 1
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
}
