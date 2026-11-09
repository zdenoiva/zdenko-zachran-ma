using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class PrerequisitesForm : Form
{
    private readonly bool _firstRun;
    private readonly bool _autoInstall;
    private IReadOnlyList<PrerequisiteItem>? _preloaded;
    private bool _autoFixAttempted;
    private bool _autoCloseScheduled;
    private readonly TableLayoutPanel _itemsLayout = new();
    private readonly RichTextBox _log = new();
    private readonly Label _status = new();
    private readonly Button _btnInstall = new();
    private readonly Button _btnRecheck = new();
    private readonly Button _btnContinue = new();
    private readonly Label _summary = new();
    private readonly CancellationTokenSource _cts = new();

    private List<PrerequisiteItem> _items = new();
    private bool _running;

    public bool AllRequiredOk =>
        _items.Count > 0 && _items.All(i => i.Ok || !i.Required);

    public PrerequisitesForm(bool firstRun, bool autoInstall = false,
        IReadOnlyList<PrerequisiteItem>? preloaded = null)
    {
        _firstRun = firstRun;
        _autoInstall = autoInstall;
        _preloaded = preloaded;
        Text = "Kontrola prostredia – " + AppInfo.Name;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 640);
        ClientSize = new Size(880, 720);
        Font = Theme.Body;
        BackColor = Theme.Background;
        Icon = Branding.AppIcon;

        BuildUi();
        if (_autoInstall)
        {
            _btnInstall.Visible = false;
            _btnRecheck.Visible = false;
        }
        Shown += async (_, _) => await RunCheckAsync();
        FormClosing += (_, _) =>
        {
            try { _cts.Cancel(); } catch { }
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildChecklist(), 0, 1);
        root.Controls.Add(BuildLog(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new GradientPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(24, 18, 24, 16)
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent
        };

        var titleRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        var logo = new PictureBox
        {
            Image = Branding.Logo,
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 12, 0)
        };
        var title = new Label
        {
            Text = _autoInstall ? "Príprava prostredia" : "Kontrola prostredia",
            AutoSize = true,
            Font = Theme.H1,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 0)
        };
        titleRow.Controls.Add(logo);
        titleRow.Controls.Add(title);

        var subtitle = new Label
        {
            Text = _autoInstall
                ? "Chýbajúce súčasti program automaticky doinštaluje. Okno sa po úspechu samo zavrie."
                : "Overím, či má počítač všetko potrebné. Ak niečo chýba, program to sám stiahne a nainštaluje.",
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 238, 248),
            BackColor = Color.Transparent,
            MaximumSize = new Size(800, 0),
            Margin = new Padding(0, 6, 0, 0)
        };
        var note = new Label
        {
            Text = _autoInstall
                ? "Toto okno je len informatívne — nemusíte nič klikať."
                : "Nič sa nemení, kým nekliknete na „Doinštalovať chýbajúce“.",
            AutoSize = true,
            ForeColor = Color.FromArgb(165, 212, 232),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 0)
        };

        stack.Controls.Add(titleRow, 0, 0);
        stack.Controls.Add(subtitle, 0, 1);
        stack.Controls.Add(note, 0, 2);
        panel.Controls.Add(stack);
        return panel;
    }

    private Control BuildChecklist()
    {
        var wrapper = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 14, 20, 8),
            BackColor = Theme.Background
        };

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Background
        };

        _itemsLayout.Dock = DockStyle.Top;
        _itemsLayout.AutoSize = true;
        _itemsLayout.ColumnCount = 1;
        _itemsLayout.BackColor = Theme.Background;

        scroll.Controls.Add(_itemsLayout);

        _summary.Dock = DockStyle.Top;
        _summary.AutoSize = true;
        _summary.Font = new Font("Segoe UI Semibold", 10f);
        _summary.Margin = new Padding(0, 0, 0, 6);

        wrapper.Controls.Add(scroll);
        wrapper.Controls.Add(_summary);
        return wrapper;
    }

    private Control BuildLog()
    {
        var wrapper = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 0, 20, 8),
            BackColor = Theme.Background
        };

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.Surface;
        _log.ForeColor = Theme.Muted;
        _log.Font = Theme.Mono;
        _log.WordWrap = true;

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(12)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        card.Controls.Add(_log);
        wrapper.Controls.Add(card);
        return wrapper;
    }

    private Control BuildFooter()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(20, 4, 20, 16),
            BackColor = Theme.Background
        };

        _btnInstall.Text = "Doinštalovať chýbajúce";
        CopyButtonStyle(Theme.PrimaryButton(_btnInstall.Text), _btnInstall);
        _btnInstall.Click += async (_, _) => await InstallMissingAsync();

        _btnRecheck.Text = "Znova skontrolovať";
        CopyButtonStyle(Theme.SecondaryButton(_btnRecheck.Text), _btnRecheck);
        _btnRecheck.Click += async (_, _) => await RunCheckAsync();

        _btnContinue.Text = _firstRun ? "Pokračovať do programu" : "Zavrieť";
        CopyButtonStyle(Theme.SecondaryButton(_btnContinue.Text), _btnContinue);
        _btnContinue.Click += (_, _) => Close();

        _status.Text = "Pripravené";
        _status.AutoSize = true;
        _status.ForeColor = Theme.Muted;
        _status.Margin = new Padding(4, 14, 0, 0);

        panel.Controls.Add(_btnInstall);
        panel.Controls.Add(_btnRecheck);
        panel.Controls.Add(_btnContinue);
        panel.Controls.Add(_status);
        return panel;
    }

    private static void CopyButtonStyle(Button source, Button target)
    {
        target.Text = source.Text;
        target.AutoSize = source.AutoSize;
        target.AutoSizeMode = source.AutoSizeMode;
        target.Height = source.Height;
        target.MinimumSize = source.MinimumSize;
        target.FlatStyle = source.FlatStyle;
        target.BackColor = source.BackColor;
        target.ForeColor = source.ForeColor;
        target.Font = source.Font;
        target.Cursor = source.Cursor;
        target.Padding = source.Padding;
        target.Margin = source.Margin;
        target.FlatAppearance.BorderColor = source.FlatAppearance.BorderColor;
        target.FlatAppearance.BorderSize = source.FlatAppearance.BorderSize;
        target.FlatAppearance.MouseOverBackColor = source.FlatAppearance.MouseOverBackColor;
        target.FlatAppearance.MouseDownBackColor = source.FlatAppearance.MouseDownBackColor;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _btnRecheck.Enabled = !running;
        _btnContinue.Enabled = !running;
        if (running) _btnInstall.Enabled = false;
    }

    private async Task RunCheckAsync()
    {
        if (_running) return;
        SetRunning(true);
        _summary.Text = "Kontrolujem prostredie…";
        _summary.ForeColor = Theme.Muted;
        _itemsLayout.Controls.Clear();
        AppendLog("— kontrola prostredia —");

        try
        {
            if (_preloaded is not null)
            {
                _items = _preloaded.ToList();
                _preloaded = null;
            }
            else
            {
                var items = await PrerequisiteChecker.CheckAsync(AppendLog, _cts.Token);
                _items = items;
            }
            RenderItems();
        }
        catch (OperationCanceledException)
        {
            AppendLog("Kontrola bola zrušená.");
        }
        catch (Exception ex)
        {
            AppendLog("Kontrola zlyhala: " + ex.Message);
            _summary.Text = "Kontrolu sa nepodarilo dokončiť.";
            _summary.ForeColor = Theme.Danger;
        }
        finally
        {
            SetRunning(false);
            _status.Text = "Hotovo";
            var problems = _items.Count(i => i.Required && !i.Ok);
            _btnInstall.Enabled = _items.Any(i => i.Required && !i.Ok && i.CanFix);
            _btnInstall.BackColor = _btnInstall.Enabled ? Theme.Danger : Theme.Muted;
            if (problems == 0 && _items.Count > 0)
            {
                _summary.Text = "✓ Prostredie je pripravené — program môže začať.";
                _summary.ForeColor = Theme.Success;
            }
            else if (problems > 0)
            {
                _summary.Text = _autoInstall
                    ? $"! Automatickú opravu sa nepodarilo dokončiť pre {problems} povinných položiek."
                    : $"! Chýba {problems} povinných položiek. Kliknite na „Doinštalovať chýbajúce“.";
                _summary.ForeColor = Theme.Danger;
                if (_autoInstall && _autoFixAttempted)
                    _btnRecheck.Visible = true;
            }
        }

        var allRequiredOk = AllRequiredOk;
        if (_autoInstall && !_autoFixAttempted &&
            _items.Any(i => i.Required && !i.Ok && i.CanFix))
        {
            _autoFixAttempted = true;
            await InstallMissingAsync();
            return;
        }

        if (_autoInstall && allRequiredOk)
            await ScheduleAutoCloseAsync();
    }

    private async Task ScheduleAutoCloseAsync()
    {
        if (_autoCloseScheduled) return;
        _autoCloseScheduled = true;

        _btnInstall.Enabled = false;
        _btnInstall.BackColor = Theme.Muted;
        _summary.Text = "✓ Hotovo — prostredie je pripravené. Okno sa o chvíľu zavrie.";
        _summary.ForeColor = Theme.Success;
        _status.Text = "Hotovo";

        await Task.Delay(1800);
        if (!IsDisposed) Close();
    }

    private void RenderItems()
    {
        _itemsLayout.Controls.Clear();
        foreach (var item in _items)
            _itemsLayout.Controls.Add(BuildRow(item));
        _itemsLayout.Refresh();
    }

    private Control BuildRow(PrerequisiteItem item)
    {
        var row = new Panel
        {
            Dock = DockStyle.Top,
            Height = 62,
            BackColor = Theme.Surface,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 0, 8)
        };
        row.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, row.Width - 1, row.Height - 1);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Theme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var icon = new Label
        {
            Text = item.Ok ? "✓" : item.Required ? "!" : "i",
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = item.Ok ? Theme.Success : item.Required ? Theme.Danger : Theme.Warning,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };

        var text = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        var title = new Label
        {
            Text = item.Title,
            Font = new Font("Segoe UI Semibold", 9.8f),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(0, 0)
        };
        var detail = new Label
        {
            Text = item.Detail,
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(0, 24),
            Size = new Size(520, 18)
        };
        text.Controls.Add(title);
        text.Controls.Add(detail);
        text.Resize += (_, _) => detail.Width = Math.Max(50, text.Width);

        var status = new Label
        {
            Text = item.Ok ? "OK" : item.Required ? "CHÝBA" : "VOLITEĽNÉ",
            Font = new Font("Segoe UI Semibold", 8.5f),
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 12, 0, 0),
            ForeColor = item.Ok ? Theme.Success : item.Required ? Theme.Danger : Theme.Muted
        };

        layout.Controls.Add(icon, 0, 0);
        layout.Controls.Add(text, 1, 0);
        layout.Controls.Add(status, 2, 0);
        row.Controls.Add(layout);
        return row;
    }

    private async Task InstallMissingAsync()
    {
        if (_running) return;
        var missing = _items.Where(i => i.Required && !i.Ok && i.CanFix).ToList();
        if (missing.Count == 0)
        {
            AppendLog("Nie je čo doinštalovať.");
            return;
        }

        SetRunning(true);
        _btnInstall.Enabled = false;
        _summary.Text = "Doinštalovávam chýbajúce súčasti… môže to trvať niekoľko minút.";
        _summary.ForeColor = Theme.Warning;
        try
        {
            foreach (var item in missing)
            {
                AppendLog("");
                AppendLog("— " + item.Title + " —");
                _status.Text = item.Title;
                await PrerequisiteChecker.ApplyFixAsync(item, AppendLog, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Inštalácia bola zrušená.");
        }
        finally
        {
            SetRunning(false);
            if (!_cts.IsCancellationRequested && !IsDisposed)
                await RunCheckAsync();
        }
    }

    private void AppendLog(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action<string>(AppendLog), line); } catch { }
            return;
        }
        _log.AppendText(line + Environment.NewLine);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
        Log.Write("Prostredie: " + line);
    }
}
