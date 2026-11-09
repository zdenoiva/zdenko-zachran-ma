using System.Diagnostics;
using NtfsRescue.Core;
using NtfsRescue.Core.Fat32;
using NtfsRescue.Repair;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class MainForm : Form
{
    private readonly ComboBox _volumeCombo = new();
    private readonly Button _refreshButton = new();
    private readonly Button _btnImage = new();
    private readonly Button _btnOpenImage = new();
    private readonly Label _volumeInfo = new();
    private readonly Label _adminLabel = new();
    private readonly TabControl _tabs = new();

    private readonly DataGridView _diagGrid = new();
    private readonly RichTextBox _diagLog = new();
    private readonly RichTextBox _repairLog = new();
    private readonly RichTextBox _sessionLog = new();
    private readonly RecoveryPanel _recovery = new();
    private readonly WizardPanel _wizard = new();
    private readonly AboutPanel _about = new();
    private readonly BootPanel _boot = new();
    private SplitContainer? _diagSplit;

    private readonly Button _btnDiagnose = new();
    private readonly Button _btnSaveDiag = new();
    private readonly Button _btnSystemCheck = new();
    private readonly Button _btnSystemRepair = new();
    private readonly Label _systemStatus = new();
    private readonly Button _btnSaveSession = new();
    private readonly Button _btnOpenLogs = new();
    private readonly Button _btnBackup = new();
    private readonly Button _btnRepair = new();
    private readonly Button _btnVerify = new();
    private readonly Button _btnRestoreBoot = new();
    private readonly Button _btnReboot = new();
    private readonly Button _btnChangeBackup = new();
    private readonly CheckBox _chkChkdsk = new();
    private readonly CheckBox _chkFullScan = new();
    private readonly CheckBox _chkInventory = new();
    private readonly Label _backupFolderLabel = new();

    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _cancelButton = new();
    private readonly Panel _updateBanner = new();
    private readonly Label _updateLabel = new();
    private readonly LinkLabel _updateLink = new();
    private readonly ProgressBar _updateProgress = new();
    private readonly Label _updatePercent = new();
    private UpdateInfo? _updateInfo;
    private bool _updating;

    private readonly List<Control> _busyControls = new();

    private VolumeEntry? _selectedEntry;
    private DiskPartition? _selectedPartition;
    private readonly List<DiskPartition> _imagePartitions = new();
    private VolumeLocation _location = new(-1, 0, 0, 512, 0, "", "", 0);
    private DiagnosticsResult? _lastDiagnostics;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _suppressSelection;
    private bool _loadingVolumes;
    private int _selectionVersion;
    private string _backupFolder = AppPaths.Backups;
    private readonly bool _smoke;

    public MainForm(bool smoke = false)
    {
        _smoke = smoke;
        Text = $"{AppInfo.Name} – záchrana a oprava diskov";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 720);
        ClientSize = new Size(1280, 840);
        Font = Theme.Body;
        BackColor = Theme.Background;
        Icon = Branding.AppIcon;

        Log.Appended += OnLogAppended;
        _recovery.BusyChanged += busy =>
        {
            if (_busy) return;
            _volumeCombo.Enabled = !busy;
            _refreshButton.Enabled = !busy;
        };
        _wizard.ActionSelected += async action => await OnWizardActionAsync(action);

        BuildUi();
        if (!_smoke)
        {
            Shown += async (_, _) => await LoadPartitionsAsync();
            Shown += (_, _) => { _ = CheckForUpdatesAsync(); };
        }
        Shown += (_, _) => AdjustSplit();
        FormClosing += (_, _) =>
        {
            try { _cts?.Cancel(); } catch { }
            _recovery.ClearSource();
        };
    }

    public void SelectTabForTest(int index)
    {
        if (index >= 0 && index < _tabs.TabPages.Count)
            _tabs.SelectedIndex = index;
    }

    public void ShowUpdateBannerForTest(string version)
    {
        _updateLabel.Text = $"Dostupná novšia verzia {version}.";
        _updateLink.Tag = UpdateService.PageUrl;
        _updateBanner.Visible = true;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildUpdateBanner(), 0, 1);
        root.Controls.Add(BuildTabs(), 0, 2);
        root.Controls.Add(BuildStatusBar(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new GradientPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(22, 14, 22, 12)
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var titleRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        var logo = new PictureBox
        {
            Image = Branding.Logo,
            Size = new Size(46, 46),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 14, 0)
        };
        var title = new Label
        {
            Text = AppInfo.Name,
            AutoSize = true,
            Font = Theme.H1,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 14, 0)
        };
        var subtitle = new Label
        {
            Text = "záchrana súborov · NTFS a FAT32 · bezpečne a prehľadne",
            AutoSize = true,
            ForeColor = Color.FromArgb(214, 236, 248),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 16, 0, 0)
        };
        var version = new Label
        {
            Text = $"v{AppInfo.VersionDisplay}",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9f),
            ForeColor = Color.FromArgb(180, 240, 250),
            BackColor = Color.Transparent,
            Margin = new Padding(12, 16, 0, 0)
        };
        titleRow.Controls.Add(logo);
        titleRow.Controls.Add(title);
        titleRow.Controls.Add(subtitle);
        titleRow.Controls.Add(version);

        var controls = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 12, 0, 0)
        };
        controls.Controls.Add(new Label
        {
            Text = "Disk / partícia:",
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 238, 248),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 9, 6, 0)
        });

        _volumeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _volumeCombo.Width = 540;
        _volumeCombo.Height = 30;
        _volumeCombo.Margin = new Padding(0, 4, 8, 0);
        _volumeCombo.SelectedIndexChanged += async (_, _) => await OnPartitionSelectedAsync();
        controls.Controls.Add(_volumeCombo);

        StyleHeaderButton(_refreshButton, "Obnoviť zoznam", new Padding(0, 3, 12, 0));
        _refreshButton.Click += async (_, _) => await LoadPartitionsAsync();
        controls.Controls.Add(_refreshButton);

        StyleHeaderButton(_btnImage, "Vytvoriť obraz", new Padding(0, 3, 8, 0));
        _btnImage.Click += async (_, _) => await CreateImageAsync();
        controls.Controls.Add(_btnImage);

        StyleHeaderButton(_btnOpenImage, "Otvoriť obraz", new Padding(0, 3, 12, 0));
        _btnOpenImage.Click += async (_, _) => await OpenImageAsync();
        controls.Controls.Add(_btnOpenImage);

        _volumeInfo.AutoSize = true;
        _volumeInfo.MaximumSize = new Size(650, 0);
        _volumeInfo.ForeColor = Color.FromArgb(220, 238, 248);
        _volumeInfo.BackColor = Color.Transparent;
        _volumeInfo.Margin = new Padding(0, 9, 0, 0);
        controls.Controls.Add(_volumeInfo);

        _adminLabel.Text = "● Administrátor";
        _adminLabel.AutoSize = true;
        _adminLabel.ForeColor = Color.FromArgb(150, 245, 190);
        _adminLabel.BackColor = Color.Transparent;
        _adminLabel.Margin = new Padding(12, 9, 0, 0);
        controls.Controls.Add(_adminLabel);

        _busyControls.Add(_volumeCombo);
        _busyControls.Add(_refreshButton);
        _busyControls.Add(_btnImage);
        _busyControls.Add(_btnOpenImage);

        stack.Controls.Add(titleRow, 0, 0);
        stack.Controls.Add(controls, 0, 1);
        panel.Controls.Add(stack);
        return panel;
    }

    private static void StyleHeaderButton(Button button, string text, Padding margin)
    {
        button.Text = text;
        button.AutoSize = true;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(126, 208, 226);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 121, 156);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(28, 95, 128);
        button.BackColor = Color.FromArgb(32, 100, 132);
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 9f);
        button.Margin = margin;
    }

    private Control BuildUpdateBanner()
    {
        _updateBanner.Dock = DockStyle.Fill;
        _updateBanner.AutoSize = false;
        _updateBanner.Height = 52;
        _updateBanner.MinimumSize = new Size(0, 52);
        _updateBanner.BackColor = Theme.WarningSoft;
        _updateBanner.Padding = new Padding(18, 6, 12, 6);
        _updateBanner.Visible = false;
        _updateBanner.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(238, 204, 140));
            e.Graphics.DrawLine(pen, 0, _updateBanner.Height - 1,
                _updateBanner.Width, _updateBanner.Height - 1);
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));

        grid.Controls.Add(new Label
        {
            Text = "↑",
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = Theme.Warning,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _updateLabel.AutoSize = false;
        _updateLabel.Dock = DockStyle.Fill;
        _updateLabel.TextAlign = ContentAlignment.MiddleLeft;
        _updateLabel.AutoEllipsis = true;
        _updateLabel.Font = new Font("Segoe UI Semibold", 9.5f);
        _updateLabel.ForeColor = Color.FromArgb(125, 80, 0);
        _updateLabel.Margin = new Padding(0, 0, 8, 0);
        grid.Controls.Add(_updateLabel, 1, 0);

        _updateProgress.Dock = DockStyle.Fill;
        _updateProgress.Height = 10;
        _updateProgress.Minimum = 0;
        _updateProgress.Maximum = 100;
        _updateProgress.Style = ProgressBarStyle.Continuous;
        _updateProgress.Margin = new Padding(6, 15, 4, 14);
        _updateProgress.Visible = false;
        grid.Controls.Add(_updateProgress, 2, 0);

        _updatePercent.AutoSize = false;
        _updatePercent.Dock = DockStyle.Fill;
        _updatePercent.TextAlign = ContentAlignment.MiddleRight;
        _updatePercent.Font = new Font("Segoe UI Semibold", 8.5f);
        _updatePercent.ForeColor = Color.FromArgb(125, 80, 0);
        _updatePercent.Visible = false;
        grid.Controls.Add(_updatePercent, 3, 0);

        _updateLink.Text = "Aktualizovať";
        _updateLink.AutoSize = false;
        _updateLink.Dock = DockStyle.Fill;
        _updateLink.TextAlign = ContentAlignment.MiddleLeft;
        _updateLink.Font = new Font("Segoe UI Semibold", 9.5f);
        _updateLink.LinkColor = Theme.AccentDark;
        _updateLink.ActiveLinkColor = Theme.Accent;
        _updateLink.LinkBehavior = LinkBehavior.AlwaysUnderline;
        _updateLink.Margin = new Padding(0);
        _updateLink.LinkClicked += async (_, _) => await StartUpdateAsync();
        grid.Controls.Add(_updateLink, 4, 0);

        var close = new Button
        {
            Text = "×",
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 13f),
            Cursor = Cursors.Hand,
            TabStop = false,
            Margin = new Padding(2, 2, 0, 2)
        };
        close.FlatAppearance.BorderSize = 0;
        close.FlatAppearance.MouseOverBackColor = Color.FromArgb(250, 232, 190);
        close.Click += (_, _) => _updateBanner.Visible = false;
        grid.Controls.Add(close, 5, 0);

        _updateBanner.Controls.Add(grid);
        return _updateBanner;
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info is null || !info.Available || IsDisposed) return;

            _updateInfo = info;
            _updateLabel.Text = $"Dostupná novšia verzia {info.Version}.";
            _updateLabel.ForeColor = Color.FromArgb(125, 80, 0);
            _updateLink.Tag = string.IsNullOrWhiteSpace(info.PageUrl)
                ? UpdateService.PageUrl
                : info.PageUrl;
            _updateLink.Enabled = true;
            _updateLink.Text = "Aktualizovať";
            _updateProgress.Visible = false;
            _updatePercent.Visible = false;
            _updateBanner.Visible = true;
            Log.Info($"Dostupná novšia verzia {info.Version}; aktuálna je {AppInfo.VersionDisplay}.");
        }
        catch (Exception ex)
        {
            Log.Warn("Kontrola aktualizácií zlyhala: " + ex.Message);
        }
    }

    private async Task StartUpdateAsync()
    {
        if (_updating || _updateInfo is null) return;

        if (UpdateInstaller.DetectMode() == UpdateMethod.Unsupported)
        {
            OpenUpdatePage();
            return;
        }

        _updating = true;
        _updateLink.Enabled = false;
        _updateLink.Text = "Sťahujem…";
        _updateProgress.Visible = true;
        _updatePercent.Visible = true;
        _updateProgress.Value = 0;
        _updatePercent.Text = "0 %";
        _updateLabel.ForeColor = Color.FromArgb(125, 80, 0);
        _updateLabel.Text = $"Sťahujem verziu {_updateInfo.Version}…";

        var progress = new Progress<double>(value =>
        {
            var percent = (int)Math.Round(Math.Clamp(value, 0, 1) * 100);
            _updateProgress.Value = Math.Clamp(percent, 0, 100);
            _updatePercent.Text = percent + " %";
        });

        try
        {
            var result = await UpdateInstaller.ApplyAsync(_updateInfo, progress, CancellationToken.None);
            _updateProgress.Value = 100;
            _updatePercent.Text = "100 %";
            _updateLabel.Text = result.Message;
            _updateLink.Text = "Spúšťam…";
            Log.Info("Aktualizácia: " + result.Message);
            await Task.Delay(600);

            if (result.ExitNow)
                Application.Exit();
        }
        catch (Exception ex)
        {
            _updating = false;
            _updateLink.Enabled = true;
            _updateLink.Text = "Skúsiť znova";
            _updateProgress.Visible = false;
            _updatePercent.Visible = false;
            _updateLabel.ForeColor = Theme.Danger;
            _updateLabel.Text = "Aktualizácia zlyhala: " + ex.Message;
            Log.Error("Aktualizácia zlyhala: " + ex);
        }
    }

    private void OpenUpdatePage()
    {
        var url = _updateLink.Tag as string;
        if (string.IsNullOrWhiteSpace(url)) url = UpdateService.PageUrl;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Nepodarilo sa otvoriť stránku aktualizácie: " + ex.Message);
        }
    }

    private Control BuildTabs()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(14, 6);
        _tabs.Appearance = TabAppearance.FlatButtons;
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.ItemSize = new Size(150, 34);
        _tabs.Font = new Font("Segoe UI Semibold", 9.5f);
        _tabs.BackColor = Theme.Background;

        _tabs.TabPages.Add(BuildWizardTab());
        _tabs.TabPages.Add(BuildDiagnosticsTab());
        _tabs.TabPages.Add(BuildBootTab());
        _tabs.TabPages.Add(BuildRepairTab());

        var recoveryTab = new TabPage("Záchrana súborov")
        {
            BackColor = Theme.Background,
            Padding = new Padding(0)
        };
        recoveryTab.Controls.Add(_recovery);
        _tabs.TabPages.Add(recoveryTab);

        _tabs.TabPages.Add(BuildAboutTab());
        _tabs.TabPages.Add(BuildLogTab());
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab?.Controls.Contains(_boot) == true)
                _ = _boot.EnsureLoadedAsync();
        };
        return _tabs;
    }

    private TabPage BuildWizardTab()
    {
        var page = new TabPage("Sprievodca")
        {
            BackColor = Theme.Background,
            Padding = new Padding(0)
        };
        page.Controls.Add(_wizard);
        return page;
    }

    private TabPage BuildBootTab()
    {
        var page = new TabPage("Boot")
        {
            BackColor = Theme.Background,
            Padding = new Padding(0)
        };
        page.Controls.Add(_boot);
        return page;
    }

    private TabPage BuildAboutTab()
    {
        var page = new TabPage("O programe")
        {
            BackColor = Theme.Background,
            Padding = new Padding(0)
        };
        page.Controls.Add(_about);
        return page;
    }

    private TabPage BuildDiagnosticsTab()
    {
        var page = new TabPage("Diagnóza") { BackColor = Theme.Background, Padding = new Padding(0) };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Padding = new Padding(8) };
        _btnDiagnose.Text = "Spustiť diagnostiku (len čítanie)";
        _btnDiagnose.AutoSize = true;
        _btnDiagnose.Font = new Font(Font, FontStyle.Bold);
        _btnDiagnose.Click += async (_, _) => await RunDiagnosticsAsync();
        toolbar.Controls.Add(_btnDiagnose);
        _busyControls.Add(_btnDiagnose);

        _chkChkdsk.Text = "spustiť aj chkdsk (len čítanie)";
        _chkChkdsk.Checked = true;
        _chkChkdsk.AutoSize = true;
        _chkChkdsk.Margin = new Padding(10, 8, 8, 0);
        toolbar.Controls.Add(_chkChkdsk);
        _busyControls.Add(_chkChkdsk);

        _btnSaveDiag.Text = "Uložiť protokol";
        _btnSaveDiag.AutoSize = true;
        _btnSaveDiag.Click += (_, _) => SaveText(_diagLog.Text, "diagnostika");
        toolbar.Controls.Add(_btnSaveDiag);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        _diagSplit = split;

        ConfigureDiagGrid();
        split.Panel1.Controls.Add(_diagGrid);
        split.Panel1.Padding = new Padding(8, 0, 4, 8);
        split.Panel2.Controls.Add(_diagLog);
        split.Panel2.Padding = new Padding(4, 0, 8, 8);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(BuildSystemFilesCard(), 0, 1);
        root.Controls.Add(split, 0, 2);
        page.Controls.Add(root);
        return page;
    }

    private Control BuildSystemFilesCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.Surface,
            Padding = new Padding(14),
            Margin = new Padding(8, 0, 8, 8)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface
        };
        layout.Controls.Add(new Label
        {
            Text = "Systémové súbory Windows",
            Font = Theme.H3,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        });
        layout.Controls.Add(new Label
        {
            Text = "DISM /CheckHealth overí komponentové úložisko Windows. " +
                   "Ak nájde poškodenie, môžete ho hneď opraviť cez DISM /RestoreHealth a SFC /scannow. " +
                   "Oprava môže trvať dlho a môže vyžadovať internet.",
            Font = Theme.Body,
            ForeColor = Theme.Muted,
            AutoSize = true,
            MaximumSize = new Size(1150, 0),
            Margin = new Padding(0, 0, 0, 8)
        });

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };

        _btnSystemCheck.Text = "Skontrolovať systémové súbory (DISM)";
        _btnSystemCheck.AutoSize = true;
        _btnSystemCheck.Click += async (_, _) => await CheckSystemFilesAsync();
        buttons.Controls.Add(_btnSystemCheck);
        _busyControls.Add(_btnSystemCheck);

        _btnSystemRepair.Text = "Opraviť systémové súbory (DISM + SFC)";
        _btnSystemRepair.AutoSize = true;
        _btnSystemRepair.Click += async (_, _) => await RepairSystemFilesAsync();
        buttons.Controls.Add(_btnSystemRepair);
        _busyControls.Add(_btnSystemRepair);

        _systemStatus.Text = "Nekontrolované";
        _systemStatus.AutoSize = true;
        _systemStatus.ForeColor = Theme.Muted;
        _systemStatus.Margin = new Padding(10, 10, 0, 0);
        buttons.Controls.Add(_systemStatus);

        layout.Controls.Add(buttons);
        card.Controls.Add(layout);
        return card;
    }

    private void ConfigureDiagGrid()
    {
        _diagGrid.Dock = DockStyle.Fill;
        _diagGrid.ReadOnly = true;
        _diagGrid.AllowUserToAddRows = false;
        _diagGrid.AllowUserToDeleteRows = false;
        _diagGrid.AllowUserToResizeRows = false;
        _diagGrid.RowHeadersVisible = false;
        _diagGrid.BackgroundColor = Theme.Surface;
        _diagGrid.BorderStyle = BorderStyle.FixedSingle;
        _diagGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _diagGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _diagGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _diagGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        _diagGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Parameter",
            Name = "key",
            Width = 210,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        var value = new DataGridViewTextBoxColumn
        {
            HeaderText = "Hodnota",
            Name = "value",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _diagGrid.Columns.Add(value);
    }

    private TabPage BuildRepairTab()
    {
        var page = new TabPage("Oprava") { BackColor = Theme.Background, Padding = new Padding(0) };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var warning = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1200, 0),
            ForeColor = Color.FromArgb(150, 90, 0),
            BackColor = Color.FromArgb(255, 248, 230),
            Padding = new Padding(10),
            Margin = new Padding(8, 8, 8, 0),
            Text = "Oprava zapisuje do metadát (NTFS/FAT32). Pred spustením dokončite kopírovanie dôležitých dát. " +
                   "Aplikácia najprv uloží záložnú kópiu kritických metadát, ale plnú zálohu dát nenahradí. " +
                   "Pre FAT32 použite Sprievodcu."
        };
        root.Controls.Add(warning, 0, 0);

        var toolbar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Padding = new Padding(8, 8, 8, 4) };

        _btnBackup.Text = "1. Zálohovať metadáta";
        _btnBackup.AutoSize = true;
        _btnBackup.Click += async (_, _) => await BackupMetadataAsync();
        toolbar.Controls.Add(_btnBackup);
        _busyControls.Add(_btnBackup);

        _btnRepair.Text = "2. OPRAVIŤ NTFS";
        _btnRepair.AutoSize = true;
        _btnRepair.Font = new Font(Font, FontStyle.Bold);
        _btnRepair.ForeColor = Color.White;
        _btnRepair.BackColor = Color.FromArgb(180, 70, 40);
        _btnRepair.FlatStyle = FlatStyle.Flat;
        _btnRepair.Click += async (_, _) => await RepairAsync();
        toolbar.Controls.Add(_btnRepair);
        _busyControls.Add(_btnRepair);

        _btnVerify.Text = "3. Overiť";
        _btnVerify.AutoSize = true;
        _btnVerify.Click += async (_, _) => await VerifyAsync();
        toolbar.Controls.Add(_btnVerify);
        _busyControls.Add(_btnVerify);

        _btnReboot.Text = "Reštartovať počítač";
        _btnReboot.AutoSize = true;
        _btnReboot.Click += (_, _) => RebootAsync();
        toolbar.Controls.Add(_btnReboot);
        _busyControls.Add(_btnReboot);

        _btnRestoreBoot.Text = "Obnoviť boot sector...";
        _btnRestoreBoot.AutoSize = true;
        _btnRestoreBoot.Enabled = false;
        _btnRestoreBoot.Click += async (_, _) => await RestoreBootSectorAsync();
        toolbar.Controls.Add(_btnRestoreBoot);
        _busyControls.Add(_btnRestoreBoot);

        _chkFullScan.Text = "použiť /r (kontrola povrchu, dlhé)";
        _chkFullScan.AutoSize = true;
        _chkFullScan.Margin = new Padding(10, 8, 8, 0);
        toolbar.Controls.Add(_chkFullScan);
        _busyControls.Add(_chkFullScan);

        _chkInventory.Text = "pred opravou uložiť inventár CSV";
        _chkInventory.Checked = true;
        _chkInventory.AutoSize = true;
        _chkInventory.Margin = new Padding(8, 8, 8, 0);
        toolbar.Controls.Add(_chkInventory);
        _busyControls.Add(_chkInventory);

        _btnChangeBackup.Text = "Priečinok záloh...";
        _btnChangeBackup.AutoSize = true;
        _btnChangeBackup.Margin = new Padding(8, 3, 4, 0);
        _btnChangeBackup.Click += (_, _) => ChangeBackupFolder();
        toolbar.Controls.Add(_btnChangeBackup);
        _busyControls.Add(_btnChangeBackup);

        _backupFolderLabel.AutoSize = true;
        _backupFolderLabel.ForeColor = Color.DimGray;
        _backupFolderLabel.MaximumSize = new Size(520, 0);
        _backupFolderLabel.Margin = new Padding(0, 8, 0, 0);
        toolbar.Controls.Add(_backupFolderLabel);
        UpdateBackupLabel();

        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(_repairLog, 0, 2);
        _repairLog.Margin = new Padding(8, 0, 8, 8);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildLogTab()
    {
        var page = new TabPage("Protokol") { BackColor = Theme.Background, Padding = new Padding(0) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Padding = new Padding(8) };
        _btnSaveSession.Text = "Uložiť protokol";
        _btnSaveSession.AutoSize = true;
        _btnSaveSession.Click += (_, _) => SaveText(_sessionLog.Text, "protokol");
        toolbar.Controls.Add(_btnSaveSession);

        _btnOpenLogs.Text = "Otvoriť priečinok s logmi";
        _btnOpenLogs.AutoSize = true;
        _btnOpenLogs.Click += (_, _) => OpenFolder(AppPaths.Logs);
        toolbar.Controls.Add(_btnOpenLogs);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_sessionLog, 0, 1);
        _sessionLog.Margin = new Padding(8, 0, 8, 8);
        page.Controls.Add(root);
        return page;
    }

    private Control BuildStatusBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(12, 4, 12, 6),
            BackColor = Theme.Background
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        _statusLabel.Text = "Pripravené.";
        _statusLabel.AutoSize = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_statusLabel, 0, 0);

        _progress.Dock = DockStyle.Fill;
        _progress.Height = 16;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Margin = new Padding(6, 2, 6, 0);
        panel.Controls.Add(_progress, 1, 0);

        _cancelButton.Text = "Zrušiť";
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Enabled = false;
        _cancelButton.Visible = false;
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        panel.Controls.Add(_cancelButton, 2, 0);

        return panel;
    }

    private void ConfigureLogBox(RichTextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.ReadOnly = true;
        box.BackColor = Theme.SurfaceAlt;
        box.Font = Theme.Mono;
        box.WordWrap = false;
        box.DetectUrls = false;
        box.ScrollBars = RichTextBoxScrollBars.Both;
    }

    protected override void OnLoad(EventArgs e)
    {
        ConfigureLogBox(_diagLog);
        ConfigureLogBox(_repairLog);
        ConfigureLogBox(_sessionLog);
        base.OnLoad(e);
    }

    private void AdjustSplit()
    {
        if (_diagSplit is null) return;
        try
        {
            int width = _diagSplit.Width;
            if (width <= 640) return;
            _diagSplit.Panel1MinSize = 280;
            _diagSplit.Panel2MinSize = 300;
            _diagSplit.SplitterDistance = Math.Min(460, width - 310);
        }
        catch
        {
            // ak sa rozmer este nezmeral, SplitContainer zostane v predvolenom stave
        }
    }

    private async Task LoadPartitionsAsync()
    {
        if (_loadingVolumes) return;
        _loadingVolumes = true;
        _refreshButton.Enabled = false;
        _statusLabel.Text = "Načítavam disky a partície...";

        try
        {
            var previousDisk = _selectedPartition?.DiskNumber;
            var previousPart = _selectedPartition?.PartitionNumber;
            var previousImage = _selectedPartition?.ImagePath;
            var partitions = await DiskCatalog.LoadAsync(CancellationToken.None);
            partitions.AddRange(_imagePartitions);

            _suppressSelection = true;
            _volumeCombo.Items.Clear();
            foreach (var p in partitions) _volumeCombo.Items.Add(p);

            int index = 0;
            if (!string.IsNullOrWhiteSpace(previousImage))
            {
                int found = partitions.FindIndex(p =>
                    p.IsImage && p.ImagePath == previousImage && p.PartitionNumber == previousPart);
                if (found >= 0) index = found;
            }
            else if (previousDisk.HasValue)
            {
                int found = partitions.FindIndex(p =>
                    p.DiskNumber == previousDisk.Value && p.PartitionNumber == previousPart);
                if (found >= 0) index = found;
            }
            else
            {
                int preferred = partitions.FindIndex(p =>
                    !p.IsImage && !p.IsSystem && p.FileSystem is FileSystemKind.Raw or FileSystemKind.Unknown);
                if (preferred < 0)
                    preferred = partitions.FindIndex(p => !p.IsImage && !p.IsSystem);
                if (preferred < 0)
                    preferred = partitions.FindIndex(p => !p.IsImage &&
                        p.FileSystem is FileSystemKind.Ntfs or FileSystemKind.Fat32);
                if (preferred >= 0) index = preferred;
            }

            if (partitions.Count > 0) _volumeCombo.SelectedIndex = index;
            _suppressSelection = false;

            if (partitions.Count == 0)
            {
                _statusLabel.Text = "Nenašli sa žiadne partície.";
                _volumeInfo.Text = "";
                _wizard.ShowEmpty();
            }
            else
            {
                await OnPartitionSelectedAsync();
            }
        }
        catch (Exception ex)
        {
            _suppressSelection = false;
            Log.Error("Zoznam partícií sa nepodarilo načítať: " + ex);
            _statusLabel.Text = "Chyba pri načítaní zoznamu diskov.";
        }
        finally
        {
            _loadingVolumes = false;
            _refreshButton.Enabled = true;
        }
    }

    private async Task OnPartitionSelectedAsync()
    {
        if (_suppressSelection) return;
        if (_volumeCombo.SelectedItem is not DiskPartition partition)
        {
            _selectedPartition = null;
            _selectedEntry = null;
            _volumeInfo.Text = "";
            _recovery.ClearSource();
            _wizard.ShowEmpty();
            return;
        }

        _selectedPartition = partition;
        int version = ++_selectionVersion;
        _location = new VolumeLocation(partition.DiskNumber, partition.Offset, partition.Length,
            partition.SectorSize, partition.DiskSize, partition.Model, partition.Serial, 0);
        _lastDiagnostics = null;
        _btnRestoreBoot.Enabled = false;
        _recovery.ClearSource();

        _selectedEntry = partition.FileSystem == FileSystemKind.Ntfs && partition.HasLetter
            ? new VolumeEntry
            {
                Letter = partition.Letter,
                Label = partition.Label,
                FileSystem = "NTFS",
                Size = partition.Length,
                Health = "",
                Status = ""
            }
            : null;

        _wizard.ShowEmpty();
        _wizard.SetBusy(true, "Zisťujem stav partície...");

        bool ntfsAdvanced = _selectedEntry is not null;
        _btnDiagnose.Enabled = ntfsAdvanced;
        _btnBackup.Enabled = ntfsAdvanced;
        _btnRepair.Enabled = ntfsAdvanced;
        _btnVerify.Enabled = ntfsAdvanced;
        _btnChangeBackup.Enabled = true;
        UpdateActionAvailability();

        _volumeInfo.Text = $"{partition.Display}  ·  {partition.Model}";
        _volumeInfo.ForeColor = Theme.SidebarText;
        _statusLabel.Text = partition.IsSystem
            ? "Toto je systémová partícia. Oprava je zablokovaná."
            : "Zisťujem stav partície...";

        try
        {
            _wizard.SetBusy(true, "Zisťujem stav partície...");
            var report = await QuickDiagnosisService.RunAsync(partition, CancellationToken.None);
            if (version != _selectionVersion) return;
            _wizard.SetReport(report);
            _wizard.SetBusy(_busy);
            _statusLabel.Text = report.Title;

            if (!ntfsAdvanced)
            {
                _diagLog.Clear();
                AppendDiag("Pre FAT32/RAW partíciu použite Sprievodcu a záložku Záchrana súborov.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Rýchla diagnostika zlyhala: " + ex.Message);
            _wizard.SetBusy(false);
            _statusLabel.Text = "Diagnostika zlyhala: " + ex.Message;
        }
    }

    private async Task OnWizardActionAsync(RecommendedAction action)
    {
        if (_selectedPartition is not { } partition) return;

        switch (action)
        {
            case RecommendedAction.CopyFiles:
                await StartRecoveryAsync(partition, deep: false);
                break;

            case RecommendedAction.DeepScan:
            {
                var confirm = MessageBox.Show(
                    "Hĺbková analýza prejde celú partíciu a môže trvať hodiny. " +
                    "Na zdrojový disk sa pritom nezapisuje.\n\nPokračovať?",
                    "Hĺbková záchrana", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm == DialogResult.Yes)
                    await StartRecoveryAsync(partition, deep: true);
                break;
            }

            case RecommendedAction.Repair:
                if (partition.IsSystem)
                {
                    MessageBox.Show("Systémovú partíciu neopravujem.", AppInfo.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                }
                if (partition.FileSystem == FileSystemKind.Fat32)
                    await RepairFat32Async(partition);
                else
                    await RepairNtfsFromWizardAsync(partition);
                break;

            case RecommendedAction.RestoreBoot:
                if (partition.FileSystem == FileSystemKind.Fat32)
                    await RestoreFatBootAsync(partition);
                else
                    await RestoreNtfsBootAsync(partition);
                break;

            case RecommendedAction.RestoreFat:
                await RestoreFatAsync(partition);
                break;

            case RecommendedAction.AssignLetter:
                await AssignLetterAsync(partition);
                break;

            case RecommendedAction.TryNtfs:
                await StartRecoveryAsync(partition, deep: false, FileSystemKind.Ntfs);
                break;

            case RecommendedAction.TryFat32:
                await StartRecoveryAsync(partition, deep: false, FileSystemKind.Fat32);
                break;
        }
    }

    private async Task StartRecoveryAsync(DiskPartition partition, bool deep,
        FileSystemKind? forcedFileSystem = null)
    {
        if (!CloseRecoveryForOperation()) return;

        IRecoverySource? source = null;
        RawDisk? disk = null;

        try
        {
            if (deep)
            {
                await RunBusyAsync("Hĺbkový sken NTFS...", async ct =>
                {
                    disk = partition.OpenDisk();
                    var progress = new Progress<DeepScanProgress>(p =>
                    {
                        _statusLabel.Text = $"{p.Phase}: {Fmt.Bytes(p.BytesDone)} / {Fmt.Bytes(p.BytesTotal)}, " +
                                            $"{p.Records:N0} záznamov";
                        if (p.BytesTotal > 0)
                        {
                            _progress.Style = ProgressBarStyle.Continuous;
                            _progress.Maximum = 1000;
                            _progress.Value = (int)Math.Min(1000, p.BytesDone * 1000 / p.BytesTotal);
                        }
                    });

                    var result = await Task.Run(() => DeepMftScanner.Scan(disk, partition.Offset,
                        partition.Length, new DeepScanOptions { IncludeDeleted = true, Resume = true },
                        progress, ct), ct);

                    var scanned = new ScannedRecordSource(disk, partition.Offset, result.SectorSize,
                        result.ClusterSize, result.RecordSize, result.Records, result.Entries);
                    source = new NtfsRecoverySource(disk, scanned, ownsDisk: true);
                    disk = null; // vlastníctvo prechádza na source

                    Log.Info($"Hĺbkový sken: {result.Records.Count:N0} záznamov, " +
                             $"{result.Entries.Count:N0} súborov, {result.Fragments.Count} fragmentov.");
                });
            }
            else
            {
                var fs = forcedFileSystem ?? partition.FileSystem;
                if (fs is FileSystemKind.Raw or FileSystemKind.Unknown)
                {
                    // Skúsime NTFS, potom FAT32 – len na čítanie.
                    try
                    {
                        disk = partition.OpenDisk();
                        source = new NtfsRecoverySource(disk, partition.Offset, partition.Length);
                        disk = null;
                        fs = FileSystemKind.Ntfs;
                    }
                    catch
                    {
                        disk?.Dispose();
                        disk = null;
                        fs = FileSystemKind.Fat32;
                    }
                }

                if (source is null && fs == FileSystemKind.Ntfs)
                {
                    disk = partition.OpenDisk();
                    source = new NtfsRecoverySource(disk, partition.Offset, partition.Length);
                    disk = null;
                }
                else if (source is null && fs == FileSystemKind.Fat32)
                {
                    disk = partition.OpenDisk();
                    var raw = disk.ReadBytes(partition.Offset, 512);
                    var boot = Fat32BootSector.Parse(raw);
                    if (!boot.IsValid)
                    {
                        // záložný boot sector je zvyčajne v sektore 6
                        try
                        {
                            var backup = disk.ReadBytes(partition.Offset + 6L * partition.SectorSize, 512);
                            var parsed = Fat32BootSector.Parse(backup);
                            if (parsed.IsValid) boot = parsed;
                        }
                        catch { }
                    }
                    if (!boot.IsValid)
                        throw new InvalidDataException("FAT32 boot sector sa nedá prečítať ani zo zálohy.");
                    source = new Fat32RecoverySource(disk, partition.Offset, boot);
                    disk = null;
                }
            }

            if (source is null)
            {
                MessageBox.Show("Nepodarilo sa otvoriť súborový systém. Skúste vybrať NTFS alebo FAT32.",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _recovery.SetSource(source, partition.Display);
            _tabs.SelectedIndex = 3;
            await _recovery.LoadAsync();
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Operácia bola zrušená.";
        }
        catch (Exception ex)
        {
            Log.Error("Otvorenie záchrany zlyhalo: " + ex);
            MessageBox.Show("Záchranu sa nepodarilo spustiť:\n\n" + ex.Message,
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { disk?.Dispose(); } catch { }
        }
    }

    private async Task RepairNtfsFromWizardAsync(DiskPartition partition)
    {
        if (RefuseSystem(partition)) return;
        bool assigned = false;
        if (_selectedEntry is null)
        {
            var letter = await PartitionAccess.AssignDriveLetterAsync(partition, CancellationToken.None);
            if (letter is null)
            {
                MessageBox.Show("Nepodarilo sa priradiť písmeno partícii.", AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            partition.DriveLetter = letter.Value.ToString();
            _selectedEntry = new VolumeEntry
            {
                Letter = letter.Value,
                Label = partition.Label,
                FileSystem = "NTFS",
                Size = partition.Length
            };
            _location = new VolumeLocation(partition.DiskNumber, partition.Offset, partition.Length,
                partition.SectorSize, partition.DiskSize, partition.Model, partition.Serial, 0);
            assigned = true;
        }

        try
        {
            await RepairAsync();
        }
        finally
        {
            if (assigned)
            {
                try { await PartitionAccess.RemoveDriveLetterAsync(partition, CancellationToken.None); }
                catch { }
            }
        }
    }

    private async Task RepairFat32Async(DiskPartition partition)
    {
        if (RefuseSystem(partition)) return;
        var confirm = MessageBox.Show(
            $"Opraviť FAT32 na Disk {partition.DiskNumber} / partícia {partition.PartitionNumber}?\n\n" +
            "Program najprv uloží zálohu boot sectoru/FAT, potom spustí chkdsk /f. " +
            "V ojedinelých prípadoch môže dôjsť k premenovaniu poškodených súborov.\n\n" +
            "Odporúčame najprv skopírovať dôležité súbory.",
            "Potvrdenie opravy FAT32", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK) return;

        await RunBusyAsync("Opravujem FAT32...", async ct =>
        {
            var progress = new Progress<string>(p => _statusLabel.Text = p);
            var outcome = await Task.Run(() => Fat32RepairService.RepairAsync(partition,
                AppendRepair, progress, ct), ct);

            AppendRepair("");
            foreach (var (key, value) in outcome.Rows)
                AppendRepair($"{key}: {value}");

            _statusLabel.Text = outcome.Summary;
            Log.Info("FAT32 oprava: " + outcome.Summary);
            MessageBox.Show(outcome.Summary, "FAT32 oprava", MessageBoxButtons.OK,
                outcome.Repaired ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            await LoadPartitionsAsync();
            await RefreshWizardAsync();
        });
    }

    private async Task RestoreFatBootAsync(DiskPartition partition)
    {
        if (RefuseSystem(partition)) return;
        var confirm = MessageBox.Show(
            "Obnoviť FAT32 boot sector zo zálohy (sektor 6)?\n\n" +
            "Pred zápisom sa uloží kópia pôvodného boot sectoru.",
            "Obnova boot sectoru", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK) return;

        await RunBusyAsync("Obnovujem FAT32 boot sector...", async ct =>
        {
            var progress = new Progress<string>(p => _statusLabel.Text = p);
            var outcome = await Task.Run(() => Fat32RepairService.RestoreBootOnlyAsync(partition,
                AppendRepair, progress, ct), ct);
            MessageBox.Show(outcome.Summary, "FAT32 boot sector", MessageBoxButtons.OK,
                outcome.Repaired ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            await LoadPartitionsAsync();
            await RefreshWizardAsync();
        });
    }

    private async Task RestoreFatAsync(DiskPartition partition)
    {
        if (RefuseSystem(partition)) return;
        var confirm = MessageBox.Show(
            "Obnoviť FAT1 z FAT2?\n\nFAT2 musí byť použiteľná. Pred zápisom sa uloží kópia FAT1.",
            "Obnova FAT1", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK) return;

        await RunBusyAsync("Obnovujem FAT1 z FAT2...", async ct =>
        {
            var progress = new Progress<string>(p => _statusLabel.Text = p);
            var outcome = await Task.Run(() => Fat32RepairService.RestoreFatOnlyAsync(partition,
                AppendRepair, progress, ct), ct);
            MessageBox.Show(outcome.Summary, "FAT1", MessageBoxButtons.OK,
                outcome.Repaired ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            await LoadPartitionsAsync();
            await RefreshWizardAsync();
        });
    }

    private async Task RestoreNtfsBootAsync(DiskPartition partition)
    {
        if (RefuseSystem(partition)) return;
        bool assigned = false;
        char letter;
        if (partition.HasLetter)
        {
            letter = partition.Letter;
        }
        else
        {
            var l = await PartitionAccess.AssignDriveLetterAsync(partition, CancellationToken.None);
            if (l is null)
            {
                MessageBox.Show("Nepodarilo sa priradiť písmeno partícii.", AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            letter = l.Value;
            partition.DriveLetter = letter.ToString();
            assigned = true;
        }

        try
        {
            byte[] backup;
            using (var disk = partition.OpenDisk())
            {
                var volume = NtfsVolume.Open(disk, partition.Offset, partition.Length);
                if (volume.BackupBootRaw is null || volume.BackupBoot?.IsValid != true)
                {
                    MessageBox.Show("Záložný NTFS boot sector nie je platný.", AppInfo.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                backup = volume.BackupBootRaw;
            }

            var confirm = MessageBox.Show(
                "Obnoviť NTFS boot sector zo zálohy?\n\n" +
                "Pred zápisom sa uloží kópia pôvodného boot sectoru.",
                "Obnova boot sectoru", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (confirm != DialogResult.OK) return;

            await RunBusyAsync("Obnovujem NTFS boot sector...", async ct =>
            {
                await Task.Run(() => BootSectorRestorer.Restore(letter, backup,
                    AppendRepair, ct), ct);
                MessageBox.Show("Boot sector bol obnovený. Reštartujte počítač a spustite diagnostiku.",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                await LoadPartitionsAsync();
                await RefreshWizardAsync();
            });
        }
        finally
        {
            if (assigned)
            {
                try { await PartitionAccess.RemoveDriveLetterAsync(partition, CancellationToken.None); }
                catch { }
            }
        }
    }

    private async Task CreateImageAsync()
    {
        if (_selectedPartition is not { } partition) { NoVolume(); return; }
        if (!CloseRecoveryForOperation()) return;

        var mode = MessageBox.Show(
            "Vytvoriť obraz celého disku?\n\n" +
            "Áno = celý disk (vrátane partition table), Nie = iba vybraná partícia.",
            "Vytvoriť obraz", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (mode == DialogResult.Cancel) return;
        bool wholeDisk = mode == DialogResult.Yes;

        using var dialog = new SaveFileDialog
        {
            Title = "Uložiť obraz disku",
            Filter = "Obraz disku (*.img)|*.img|Všetky súbory (*.*)|*.*",
            FileName = $"{SafeName(partition.Model)}-{(wholeDisk ? "disk" : "part" + partition.PartitionNumber)}-{DateTime.Now:yyyyMMdd-HHmmss}.img",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        long estimated = wholeDisk ? Math.Max(partition.DiskSize, partition.Length) : partition.Length;
        var confirm = MessageBox.Show(
            $"Vytvoriť obraz s veľkosťou približne {Fmt.Bytes(estimated)}?\n\n" +
            $"Zdroj: {partition.Display}\nCieľ: {dialog.FileName}\n\n" +
            "Na zdrojový disk sa nezapisuje. Vykonajte to len ak máte na cieľovom disku dosť miesta.",
            "Potvrdenie obrazu", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK) return;

        await RunBusyAsync("Vytváram obraz...", async ct =>
        {
            var progress = new Progress<ImageProgress>(p =>
            {
                _statusLabel.Text = $"{p.Phase}";
                if (p.BytesTotal > 0)
                {
                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.Maximum = 1000;
                    _progress.Value = (int)Math.Min(1000, p.BytesDone * 1000 / p.BytesTotal);
                }
            });

            var result = await Task.Run(() => DiskImageService.CreateAsync(partition,
                dialog.FileName, wholeDisk, progress, AppendRepair, ct), ct);

            var msg = $"Obraz hotový:\n{result.Path}\n\nVeľkosť: {Fmt.Bytes(result.Bytes)}\n" +
                      $"Čas: {result.Duration:hh\\:mm\\:ss}";
            if (result.BadRanges > 0)
                msg += $"\n\nNeprečítateľné oblasti: {result.BadRanges} (log: {result.Path}.bad.txt)";

            MessageBox.Show(msg, "Obraz disku", MessageBoxButtons.OK,
                result.BadRanges > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            var open = MessageBox.Show("Chcete obraz hneď otvoriť na záchranu súborov?",
                "Obraz disku", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (open == DialogResult.Yes)
                await OpenImagePathAsync(result.Path);
        });
    }

    private async Task OpenImageAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Otvoriť obraz disku",
            Filter = "Obrazy (*.img;*.raw;*.dd;*.bin)|*.img;*.raw;*.dd;*.bin|Všetky súbory (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await OpenImagePathAsync(dialog.FileName);
    }

    private async Task OpenImagePathAsync(string path)
    {
        try
        {
            _statusLabel.Text = "Načítavam obraz...";
            var parts = await DiskCatalog.LoadImageAsync(path, CancellationToken.None);
            if (parts.Count == 0)
            {
                MessageBox.Show("V obraze sa nenašla žiadna partícia.", AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _imagePartitions.RemoveAll(p => p.ImagePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            _imagePartitions.AddRange(parts);
            await LoadPartitionsAsync();

            int index = -1;
            for (int i = 0; i < _volumeCombo.Items.Count; i++)
            {
                if (_volumeCombo.Items[i] is DiskPartition p &&
                    p.ImagePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0) _volumeCombo.SelectedIndex = index;

            if (parts.Count > 1)
                MessageBox.Show($"Obraz obsahuje {parts.Count} partícií. Vyberte si v zozname.",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Otvorenie obrazu zlyhalo: " + ex);
            MessageBox.Show("Obraz sa nepodarilo otvoriť:\n\n" + ex.Message, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "disk";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }

    private void RebootAsync()
    {
        var answer = MessageBox.Show(
            "Reštartovať počítač? Neuložené dáta v iných programoch sa stratia.",
            "Reštart počítača", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes) ScheduleReboot();
    }

    private void ScheduleReboot()
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                "/r /t 10 /c \"Zdenko Zachran ma: restart po oprave disku\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            _statusLabel.Text = "Reštart za 10 sekúnd... (zrušíte príkazom shutdown /a)";
            Log.Info("Naplánovaný reštart počítača o 10 sekúnd.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Reštart sa nepodarilo naplánovať:\n" + ex.Message, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task AssignLetterAsync(DiskPartition partition)
    {
        if (RefuseSystem(partition)) return;
        var letter = await PartitionAccess.AssignDriveLetterAsync(partition, CancellationToken.None);
        if (letter is null)
        {
            MessageBox.Show("Nepodarilo sa priradiť písmeno. Možno už žiadne voľné nie je.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show($"Partícii bolo priradené písmeno {letter}:.", AppInfo.Name,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        await LoadPartitionsAsync();
        await RefreshWizardAsync();
    }

    private async Task RefreshWizardAsync()
    {
        if (_selectedPartition is not { } partition) return;
        try
        {
            var report = await QuickDiagnosisService.RunAsync(partition, CancellationToken.None);
            _wizard.SetReport(report);
            _wizard.SetBusy(_busy);
            _statusLabel.Text = report.Title;
        }
        catch (Exception ex)
        {
            Log.Warn("Obnovenie diagnostiky zlyhalo: " + ex.Message);
        }
    }

    private async Task RunDiagnosticsAsync()
    {
        if (_selectedEntry is not { } entry) { NoVolume(); return; }
        if (!CloseRecoveryForOperation()) return;

        _diagGrid.Rows.Clear();
        _diagLog.Clear();
        _lastDiagnostics = null;

        await RunBusyAsync("Spúšťam diagnostiku...", async ct =>
        {
            var progress = new Progress<string>(p => _statusLabel.Text = p);
            var result = await DiagnosticsService.RunAsync(entry, _chkChkdsk.Checked,
                AppendDiag, progress, ct);

            _lastDiagnostics = result;
            FillDiagGrid(result);
            _btnRestoreBoot.Enabled = result.BootFromBackup || result.PrimaryBackupDiffer;

            Log.Info("Diagnostika dokončená: " + result.Summary);
            _statusLabel.Text = result.Summary;
        });
    }

    private async Task CheckSystemFilesAsync()
    {
        if (_busy) return;

        SetSystemStatus("Kontrolujem…", Theme.Muted);
        AppendDiag("");
        AppendDiag("=== Systémové súbory Windows (DISM CheckHealth) ===");

        SystemFileResult? result = null;
        await RunBusyAsync("Kontrolujem systémové súbory (DISM CheckHealth)…", async ct =>
        {
            result = await SystemFileService.CheckHealthAsync(AppendDiag, ct);
        });

        if (result is null)
        {
            SetSystemStatus("Kontrolu sa nepodarilo dokončiť.", Theme.Warning);
            return;
        }

        AppendDiag("");
        AppendDiag(result.Summary);
        Log.Info("DISM CheckHealth: " + result.Summary);

        if (result.IssueFound)
        {
            SetSystemStatus("Zistené poškodenie — odporúčaná oprava.", Theme.Danger);
            var answer = MessageBox.Show(
                "DISM zistil poškodenie systémových súborov alebo komponentového úložiska.\n\n" +
                "Chcete ich teraz automaticky opraviť? Spustí sa DISM /RestoreHealth a potom SFC /scannow. " +
                "Oprava môže trvať niekoľko minút a môže vyžadovať internet.",
                "Kontrola systémových súborov", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
                await RepairSystemFilesAsync(alreadyConfirmed: true);
        }
        else if (result.Success)
        {
            SetSystemStatus("Systémové súbory sú v poriadku.", Theme.Success);
        }
        else
        {
            SetSystemStatus(result.Summary, Theme.Warning);
        }
    }

    private async Task RepairSystemFilesAsync(bool alreadyConfirmed = false)
    {
        if (_busy) return;

        if (!alreadyConfirmed)
        {
            var confirm = MessageBox.Show(
                "Spustí sa oprava systémových súborov:\n\n" +
                "1. DISM /Online /Cleanup-Image /RestoreHealth\n" +
                "2. SFC /scannow\n\n" +
                "Oprava môže trvať dlho a môže vyžadovať internet. Pokračovať?",
                "Oprava systémových súborov", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;
        }

        SetSystemStatus("Opravujem…", Theme.Warning);
        AppendDiag("");
        AppendDiag("=== Oprava systémových súborov (DISM RestoreHealth + SFC) ===");

        SystemFileResult? result = null;
        await RunBusyAsync("Opravujem systémové súbory (DISM + SFC)…", async ct =>
        {
            var progress = new Progress<string>(p => _statusLabel.Text = p);
            result = await SystemFileService.RepairAsync(AppendDiag, progress, ct);
        });

        if (result is null)
        {
            SetSystemStatus("Opravu sa nepodarilo dokončiť.", Theme.Danger);
            return;
        }

        AppendDiag("");
        AppendDiag(result.Summary);
        Log.Info("Oprava systémových súborov: " + result.Summary);

        if (result.Success)
            SetSystemStatus(result.RebootRequired
                ? "Opravené — odporúčaný reštart počítača."
                : "Systémové súbory sú opravené.", Theme.Success);
        else
            SetSystemStatus("Oprava sa nedokončila úplne — pozrite protokol.", Theme.Danger);

        MessageBox.Show(result.Summary, "Oprava systémových súborov",
            MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void SetSystemStatus(string text, Color color)
    {
        _systemStatus.Text = text;
        _systemStatus.ForeColor = color;
    }

    private void FillDiagGrid(DiagnosticsResult result)
    {
        _diagGrid.SuspendLayout();
        try
        {
            _diagGrid.Rows.Clear();
            foreach (var (key, value) in result.Rows)
                _diagGrid.Rows.Add(key, value);
        }
        finally
        {
            _diagGrid.ResumeLayout();
        }
    }

    private async Task BackupMetadataAsync()
    {
        if (_selectedEntry is not { } entry) { NoVolume(); return; }
        if (!CloseRecoveryForOperation()) return;

        await RunBusyAsync("Zálohujem metadáta...", async ct =>
        {
            var progress = new Progress<string>(p => _statusLabel.Text = p);
            var includeInventory = _chkInventory.Checked;
            var backupFolder = _backupFolder;

            var backup = await Task.Run(() =>
            {
                var loc = VolumeMapper.Locate(entry.Letter);
                if (!loc.IsValid) throw new IOException("Nepodarilo sa zistiť fyzický disk.");

                using var disk = new RawDisk(loc.DiskNumber, loc.SectorSize, loc.DiskSize);
                var volume = NtfsVolume.Open(disk, loc.Offset, loc.Length);

                NtfsScanResult? inventory = null;
                if (includeInventory)
                {
                    AppendRepair("Skenujem súbory pre inventár (len čítanie)...");
                    inventory = NtfsScanner.Scan(volume, includeDeleted: true,
                        new Progress<ScanProgress>(p => _statusLabel.Text = $"Inventár: {p.Phase} {p.Current:N0}/{p.Total:N0}"),
                        ct);
                }

                return MetadataBackup.Create(volume, entry.Letter, backupFolder, inventory,
                    AppendRepair, progress, ct);
            }, ct);

            AppendRepair("");
            AppendRepair($"Záloha metadát hotová: {backup.Folder}");
            Log.Info($"Záloha metadát: {backup.Folder} ({Fmt.Bytes(backup.Bytes)})");
            _statusLabel.Text = $"Záloha metadát hotová: {Fmt.Bytes(backup.Bytes)}";
            _tabs.SelectedIndex = 2;
        });
    }

    private async Task RepairAsync()
    {
        if (_selectedEntry is not { } entry) { NoVolume(); return; }
        if (IsSystemVolume(entry.Letter))
        {
            MessageBox.Show("Toto je systémový disk s Windows. Program na ňom odmieta vykonávať opravu NTFS.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!CloseRecoveryForOperation()) return;

        var message =
            $"Opraviť NTFS na disku {entry.Letter}:?\n\n" +
            $"Disk: {_location.Model}\nSériové číslo: {_location.Serial}\n" +
            $"Partícia: {Fmt.Bytes(_location.Length)}\n\n" +
            "Program spustí chkdsk /f, ktorý zapisuje do metadát. V ojedinelých prípadoch môže " +
            "premenovať alebo izolovať poškodené súbory. Predtým sa uloží záloha metadát.\n\n" +
            "Uistite sa, že kopírovanie dôležitých dát už skončilo a disk nie je používaný iným programom.";

        using var confirm = new ConfirmForm(
            "Potvrdenie opravy NTFS",
            message,
            entry.Letter.ToString(),
            "Rozumiem, že oprava mení metadáta a môže mať následky.");
        if (confirm.ShowDialog(this) != DialogResult.OK) return;

        _lastDiagnostics = null;
        _btnRestoreBoot.Enabled = false;
        var includeInventory = _chkInventory.Checked;
        var backupFolder = _backupFolder;
        var fullScan = _chkFullScan.Checked;
        RepairOutcome? repairOutcome = null;

        await RunBusyAsync("Opravujem NTFS...", async ct =>
        {
            _repairLog.Clear();
            AppendRepair($"Cieľ: {entry.Letter}:  ({entry.Label})");
            AppendRepair("");

            // Najprv záloha metadát.
            var backup = await Task.Run(() =>
            {
                var loc = VolumeMapper.Locate(entry.Letter);
                if (!loc.IsValid) throw new IOException("Nepodarilo sa zistiť fyzický disk.");

                using var disk = new RawDisk(loc.DiskNumber, loc.SectorSize, loc.DiskSize);
                var volume = NtfsVolume.Open(disk, loc.Offset, loc.Length);

                NtfsScanResult? inventory = null;
                if (includeInventory)
                {
                    AppendRepair("Skenujem súbory pre inventár (len čítanie)...");
                    inventory = NtfsScanner.Scan(volume, includeDeleted: true,
                        new Progress<ScanProgress>(p => _statusLabel.Text = $"Inventár: {p.Phase} {p.Current:N0}/{p.Total:N0}"),
                        ct);
                }

                return MetadataBackup.Create(volume, entry.Letter, backupFolder, inventory,
                    AppendRepair, new Progress<string>(p => _statusLabel.Text = p), ct);
            }, ct);

            AppendRepair($"Záloha metadát: {backup.Folder}");
            AppendRepair("");

            var progress = new Progress<string>(p => _statusLabel.Text = p);
            var outcome = await RepairService.RepairAsync(entry.Letter, fullScan,
                AppendRepair, progress, ct);

            AppendRepair("");
            foreach (var (key, value) in outcome.Rows)
                AppendRepair($"{key}: {value}");

            _statusLabel.Text = outcome.Summary;
            Log.Info("Oprava: " + outcome.Summary);
            repairOutcome = outcome;

            MessageBox.Show(outcome.Summary + "\n\nZáloha metadát:\n" + backup.Folder,
                "Oprava dokončená", MessageBoxButtons.OK,
                outcome.Repaired ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            await LoadPartitionsAsync();
        });

        if (repairOutcome is { Repaired: false } && _selectedPartition is { } failedPartition)
        {
            var go = MessageBox.Show(
                "Oprava sa nedokončila úplne.\n\nChcete teraz načítať súbory v režime len na čítanie " +
                "a skopírovať ich na iný disk?",
                "Záchrana súborov", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (go == DialogResult.Yes)
                await StartRecoveryAsync(failedPartition, deep: false);
        }
    }

    private async Task VerifyAsync()
    {
        if (_selectedEntry is not { } entry) { NoVolume(); return; }
        if (!CloseRecoveryForOperation()) return;

        await RunBusyAsync("Overujem...", async ct =>
        {
            var progress = new Progress<string>(p => _statusLabel.Text = p);
            var outcome = await RepairService.VerifyAsync(entry.Letter, AppendRepair, progress, ct);

            AppendRepair("");
            foreach (var (key, value) in outcome.Rows)
                AppendRepair($"{key}: {value}");

            _statusLabel.Text = outcome.Summary;
            Log.Info("Overenie: " + outcome.Summary);

            MessageBox.Show(outcome.Summary, "Overenie",
                MessageBoxButtons.OK, outcome.Healthy ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            await LoadPartitionsAsync();
        });
    }

    private async Task RestoreBootSectorAsync()
    {
        if (_selectedEntry is not { } entry) { NoVolume(); return; }
        if (IsSystemVolume(entry.Letter))
        {
            MessageBox.Show("Toto je systémový disk s Windows. Program na ňom odmieta zapisovať do boot sectoru.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_lastDiagnostics is null)
        {
            MessageBox.Show("Najprv spustite diagnostiku.", AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CloseRecoveryForOperation()) return;

        using var confirm = new ConfirmForm(
            "Obnova boot sectoru",
            "Táto operácia prepíše prvý sektor partície záložným boot sectorom. " +
            "Použite ju len vtedy, keď diagnostika ukázala poškodený primárny boot sector.\n\n" +
            "Po obnove bude možno potrebný reštart.",
            entry.Letter.ToString(),
            "Rozumiem, že ide o zápis do boot sectoru.");
        if (confirm.ShowDialog(this) != DialogResult.OK) return;

        await RunBusyAsync("Obnovujem boot sector...", async ct =>
        {
            var loc = VolumeMapper.Locate(entry.Letter);
            if (!loc.IsValid) throw new IOException("Nepodarilo sa zistiť fyzický disk.");

            byte[] backup;
            using (var disk = new RawDisk(loc.DiskNumber, loc.SectorSize, loc.DiskSize))
            {
                backup = disk.ReadBytes(loc.Offset + loc.Length - 512, 512);
            }

            await Task.Run(() =>
            {
                BootSectorRestorer.Restore(entry.Letter, backup, AppendRepair, ct);
            }, ct);

            AppendRepair("Obnova boot sectoru dokončená. Reštartujte počítač a skontrolujte disk.");
            Log.Info("Boot sector obnovený zo zálohy.");
            MessageBox.Show("Boot sector bol obnovený.\n\nReštartujte počítač a potom spustite diagnostiku.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private async Task RunBusyAsync(string status, Func<CancellationToken, Task> action)
    {
        if (_busy) return;

        _cts = new CancellationTokenSource();
        SetBusy(true, status);
        _progress.Style = ProgressBarStyle.Marquee;

        try
        {
            await action(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Operácia bola zrušená.";
            Log.Warn("Operácia bola zrušená.");
        }
        catch (Exception ex)
        {
            Log.Error("Operácia zlyhala: " + ex);
            _statusLabel.Text = "Chyba: " + ex.Message;
            MessageBox.Show("Operácia zlyhala:\n\n" + ex.Message, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, null);
            UpdateActionAvailability();
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
        }
    }

    private void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        foreach (var c in _busyControls) c.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _cancelButton.Visible = busy;

        if (status is not null) _statusLabel.Text = status;

        if (!busy)
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }

        _wizard.SetBusy(busy);
        UpdateActionAvailability();
    }

    private void UpdateActionAvailability()
    {
        bool ntfs = !_busy && _selectedEntry is not null;
        _btnDiagnose.Enabled = ntfs;
        _btnBackup.Enabled = ntfs;
        _btnRepair.Enabled = ntfs;
        _btnVerify.Enabled = ntfs;
        _btnChangeBackup.Enabled = !_busy;
        _btnReboot.Enabled = !_busy;
        _btnImage.Enabled = !_busy && _selectedPartition is not null;
        _btnOpenImage.Enabled = !_busy;
        _btnRestoreBoot.Enabled = ntfs && _lastDiagnostics is { } d &&
                                  (d.BootFromBackup || d.PrimaryBackupDiffer);
    }

    private static bool RefuseSystem(DiskPartition partition)
    {
        if (!partition.IsSystem) return false;
        MessageBox.Show("Toto je systémová alebo recovery partícia. Program na nej odmieta vykonávať opravu.",
            AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return true;
    }

    private bool CloseRecoveryForOperation()
    {
        if (_recovery.IsBusy)
        {
            MessageBox.Show("V zálohe „Záchrana súborov“ práve prebieha operácia. Dokončite ju alebo zrušte a skúste znova.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _recovery.ClearSource();
        return true;
    }

    private static bool IsSystemVolume(char letter)
    {
        try
        {
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            return systemRoot.StartsWith($"{char.ToUpperInvariant(letter)}:", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void NoVolume()
    {
        MessageBox.Show("Najprv vyberte disk alebo partíciu.", AppInfo.Name,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void AppendDiag(string line) => Append(_diagLog, line);
    private void AppendRepair(string line) => Append(_repairLog, line);

    private static void Append(RichTextBox box, string line)
    {
        try
        {
            if (box.IsDisposed) return;
            if (box.InvokeRequired)
            {
                box.BeginInvoke(new Action(() => Append(box, line)));
                return;
            }

            if (box.TextLength > 2_000_000) box.Clear();
            box.AppendText(line + Environment.NewLine);
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
        }
        catch { }
    }

    private void OnLogAppended(string line) => Append(_sessionLog, line);

    private void SaveText(string text, string name)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        using var dialog = new SaveFileDialog
        {
            Title = "Uložiť " + name,
            Filter = "Text (*.txt)|*.txt|Všetky súbory (*.*)|*.*",
            FileName = $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            InitialDirectory = AppPaths.Logs
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                File.WriteAllText(dialog.FileName, text);
                _statusLabel.Text = "Uložené: " + dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nepodarilo sa uložiť:\n" + ex.Message, AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ChangeBackupFolder()
    {
        Directory.CreateDirectory(_backupFolder);
        using var dialog = new FolderBrowserDialog
        {
            Description = "Priečinok pre zálohy metadát (nie na zdrojovom disku).",
            SelectedPath = _backupFolder,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _backupFolder = dialog.SelectedPath;
            UpdateBackupLabel();
        }
    }

    private void UpdateBackupLabel()
    {
        _backupFolderLabel.Text = "Zálohy: " + _backupFolder;
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Log.Appended -= OnLogAppended;
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _recovery.ClearSource();
        }
        base.Dispose(disposing);
    }
}

