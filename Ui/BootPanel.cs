using NtfsRescue.Core;
using NtfsRescue.Repair;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class BootPanel : UserControl
{
    private sealed class DiskOption
    {
        public int Number;
        public string Model = "";
        public long Size;
        public string Style = "";
        public bool IsSystem;
        public List<DiskPartition> Partitions = new();

        public override string ToString()
            => $"Disk {Number} · {Model} · {Fmt.Bytes(Size)} · {Style}" +
               (IsSystem ? " · aktuálny systémový" : "");
    }

    private readonly ComboBox _diskCombo = new();
    private readonly Button _btnReload = new();
    private readonly Button _btnCheck = new();
    private readonly Button _btnCancel = new();
    private readonly Button _btnRepair = new();
    private readonly Button _btnWinRe = new();
    private readonly Button _btnOem = new();
    private readonly Button _btnConvert = new();
    private readonly Button _btnBios = new();
    private readonly CheckBox _chkRenewSignature = new();
    private readonly Label _status = new();
    private readonly Label _summary = new();
    private readonly Label _firmware = new();
    private readonly Label _layout = new();
    private readonly Label _windows = new();
    private readonly Label _bcd = new();
    private readonly Label _recovery = new();
    private readonly Label _blocked = new();
    private readonly DataGridView _grid = new();
    private readonly RichTextBox _log = new();
    private readonly ProgressBar _progress = new();

    private List<DiskPartition> _allPartitions = new();
    private BootAnalysis? _analysis;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _loaded;

    public BootPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();
    }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded) await LoadDisksAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18, 14, 18, 14),
            BackColor = Theme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildDetails(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildToolbar()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 10)
        };

        panel.Controls.Add(new Label
        {
            Text = "Cieľový disk:",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Theme.Text,
            Margin = new Padding(0, 8, 8, 0)
        });

        _diskCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _diskCombo.Width = 620;
        _diskCombo.Margin = new Padding(0, 4, 8, 0);
        _diskCombo.SelectedIndexChanged += (_, _) =>
        {
            _analysis = null;
            ClearResults();
        };
        panel.Controls.Add(_diskCombo);

        _btnReload.Text = "Obnoviť disky";
        _btnReload.AutoSize = true;
        _btnReload.Margin = new Padding(0, 3, 8, 0);
        _btnReload.Click += async (_, _) => await LoadDisksAsync();
        panel.Controls.Add(_btnReload);

        _btnCheck.Text = "Skontrolovať bootovanie";
        _btnCheck.AutoSize = true;
        _btnCheck.Font = new Font(Font, FontStyle.Bold);
        _btnCheck.Margin = new Padding(0, 3, 8, 0);
        _btnCheck.Click += async (_, _) => await CheckAsync();
        panel.Controls.Add(_btnCheck);

        _btnCancel.Text = "Zrušiť";
        _btnCancel.AutoSize = true;
        _btnCancel.Visible = false;
        _btnCancel.Margin = new Padding(0, 3, 8, 0);
        _btnCancel.Click += (_, _) => _cts?.Cancel();
        panel.Controls.Add(_btnCancel);

        _progress.Visible = false;
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.Size = new Size(120, 18);
        _progress.Margin = new Padding(0, 8, 0, 0);
        panel.Controls.Add(_progress);

        _status.Text = "Vyberte disk";
        _status.AutoSize = true;
        _status.ForeColor = Theme.Muted;
        _status.Margin = new Padding(8, 8, 0, 0);
        panel.Controls.Add(_status);

        return panel;
    }

    private Control BuildSummary()
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.Surface,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 10)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 8,
            BackColor = Theme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddSummaryRow(layout, 0, "Firmware", _firmware);
        AddSummaryRow(layout, 1, "Disk", _layout);
        AddSummaryRow(layout, 2, "Windows", _windows);
        AddSummaryRow(layout, 3, "Boot / recovery", _bcd);

        _recovery.AutoSize = true;
        _recovery.MaximumSize = new Size(1100, 0);
        _recovery.Font = Theme.Small;
        _recovery.ForeColor = Theme.Muted;
        _recovery.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_recovery, 0, 4);
        layout.SetColumnSpan(_recovery, 2);

        _summary.Text = "Vyberte disk a spustite kontrolu.";
        _summary.AutoSize = true;
        _summary.MaximumSize = new Size(1100, 0);
        _summary.Font = new Font("Segoe UI Semibold", 9.5f);
        _summary.ForeColor = Theme.Muted;
        _summary.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(_summary, 0, 5);
        layout.SetColumnSpan(_summary, 2);

        _blocked.AutoSize = true;
        _blocked.MaximumSize = new Size(1100, 0);
        _blocked.ForeColor = Theme.Danger;
        _blocked.Font = new Font("Segoe UI Semibold", 9f);
        _blocked.Visible = false;
        _blocked.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_blocked, 0, 6);
        layout.SetColumnSpan(_blocked, 2);

        card.Controls.Add(layout);
        return card;
    }

    private static void AddSummaryRow(TableLayoutPanel layout, int row, string key, Label value)
    {
        layout.Controls.Add(new Label
        {
            Text = key + ":",
            AutoSize = true,
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 3, 8, 0)
        }, 0, row);

        value.AutoSize = true;
        value.MaximumSize = new Size(1000, 0);
        value.Font = Theme.Body;
        value.ForeColor = Theme.Text;
        value.Margin = new Padding(0, 2, 0, 2);
        layout.Controls.Add(value, 1, row);
    }

    private Control BuildDetails()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300
        };

        ConfigureGrid();
        split.Panel1.Controls.Add(_grid);
        split.Panel1.Padding = new Padding(0, 0, 0, 6);

        ConfigureLog();
        split.Panel2.Controls.Add(_log);
        split.Panel2.Padding = new Padding(0, 6, 0, 0);
        return split;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Theme.Surface;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Kontrola",
            Width = 190,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Stav",
            Width = 90,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Detail",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void ConfigureLog()
    {
        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.Surface;
        _log.Font = Theme.Mono;
        _log.WordWrap = true;
    }

    private Control BuildActions()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Theme.Background,
            Margin = new Padding(0, 8, 0, 0)
        };

        _btnRepair.Text = "Opraviť bootovanie";
        _btnRepair.AutoSize = true;
        _btnRepair.Font = new Font(Font, FontStyle.Bold);
        _btnRepair.Enabled = false;
        _btnRepair.Click += async (_, _) => await RepairAsync();
        panel.Controls.Add(_btnRepair);

        _btnWinRe.Text = "Pridať Windows RE";
        _btnWinRe.AutoSize = true;
        _btnWinRe.Enabled = false;
        _btnWinRe.Click += async (_, _) => await AddRecoveryAsync(true);
        panel.Controls.Add(_btnWinRe);

        _btnOem.Text = "Pridať OEM recovery";
        _btnOem.AutoSize = true;
        _btnOem.Enabled = false;
        _btnOem.Click += async (_, _) => await AddRecoveryAsync(false);
        panel.Controls.Add(_btnOem);

        _btnConvert.Text = "Previesť MBR → GPT…";
        _btnConvert.AutoSize = true;
        _btnConvert.Enabled = false;
        _btnConvert.Click += async (_, _) => await ConvertAsync();
        panel.Controls.Add(_btnConvert);

        _chkRenewSignature.Text = "Nová disk signature (pri klone)";
        _chkRenewSignature.AutoSize = true;
        _chkRenewSignature.Visible = false;
        _chkRenewSignature.Margin = new Padding(8, 8, 0, 0);
        panel.Controls.Add(_chkRenewSignature);

        _btnBios.Text = "Čo nastaviť v BIOS/UEFI…";
        _btnBios.AutoSize = true;
        _btnBios.Margin = new Padding(8, 3, 0, 0);
        _btnBios.Click += (_, _) => ShowBiosInfo();
        panel.Controls.Add(_btnBios);

        return panel;
    }

    private async Task LoadDisksAsync()
    {
        if (_busy) return;
        SetBusy(true, "Načítavam disky…");
        try
        {
            var previous = _diskCombo.SelectedItem as DiskOption;
            _allPartitions = await DiskCatalog.LoadAllAsync(CancellationToken.None);
            _loaded = true;
            var options = _allPartitions
                .GroupBy(p => p.DiskNumber)
                .Select(g =>
                {
                    var first = g.First();
                    return new DiskOption
                    {
                        Number = first.DiskNumber,
                        Model = first.Model,
                        Size = first.DiskSize,
                        Style = string.IsNullOrWhiteSpace(first.PartitionStyle)
                            ? (first.IsGpt ? "GPT" : "MBR")
                            : first.PartitionStyle,
                        IsSystem = first.DiskIsSystem,
                        Partitions = g.ToList()
                    };
                })
                .OrderBy(d => d.Number)
                .ToList();

            _diskCombo.Items.Clear();
            foreach (var option in options) _diskCombo.Items.Add(option);
            if (options.Count == 0)
            {
                SetStatus("Nenašli sa žiadne disky.", Theme.Warning);
                return;
            }

            var select = previous is not null
                ? options.FirstOrDefault(o => o.Number == previous.Number)
                : options.FirstOrDefault(o => !o.IsSystem) ?? options[0];
            _diskCombo.SelectedItem = select;
            _analysis = null;
            ClearResults();
            SetStatus("Pripravené", Theme.Muted);
        }
        catch (Exception ex)
        {
            AppendLog("Načítanie diskov zlyhalo: " + ex.Message);
            SetStatus("Disky sa nepodarilo načítať.", Theme.Danger);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private async Task CheckAsync()
    {
        if (_busy || _diskCombo.SelectedItem is not DiskOption disk) return;
        if (_allPartitions.Count == 0) await LoadDisksAsync();
        SetBusy(true, "Kontrolujem bootovanie…");
        _grid.Rows.Clear();
        _blocked.Visible = false;
        _blocked.Text = "";
        _log.Clear();
        try
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<string>(text => _status.Text = text);
            _analysis = await BootAnalysisService.AnalyzeDiskAsync(disk.Partitions[0],
                _allPartitions, AppendLog, progress, _cts.Token);
            RenderAnalysis();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Kontrola bola zrušená.", Theme.Warning);
            AppendLog("Kontrola bola zrušená.");
        }
        catch (Exception ex)
        {
            AppendLog("Kontrola zlyhala: " + ex.Message);
            SetStatus("Kontrola zlyhala.", Theme.Danger);
        }
        finally
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            SetBusy(false);
            UpdateButtons();
        }
    }

    private void RenderAnalysis()
    {
        if (_analysis is null) return;
        var a = _analysis;

        _firmware.Text = a.Firmware switch
        {
            FirmwareMode.Uefi => "UEFI",
            FirmwareMode.Bios => "BIOS / Legacy",
            _ => "nezistený"
        };
        _layout.Text = $"{a.Target.PartitionStyle} · {a.Target.Model}" +
                       (a.Target.DiskIsSystem ? " · AKTUÁLNY SYSTÉMOVÝ DISK" : "");
        _windows.Text = a.WindowsPartitions.Count == 0
            ? "nenašla sa"
            : $"{a.WindowsPartitions[0].DriveLetter}: · " +
              (string.IsNullOrWhiteSpace(a.WindowsVersion) ? "Windows" : a.WindowsVersion);

        var boot = a.BcdValid ? "BCD OK" : "BCD poškodené / chýba";
        if (a.IsBios && !a.MbrBootCodePresent) boot += " · MBR boot code chýba";
        _bcd.Text = boot;

        if (a.WinReImage is not null)
            _recovery.Text = "Windows RE: " + a.WinReImage.Path;
        else if (a.OemImage is not null)
            _recovery.Text = "OEM recovery: " + a.OemImage.Path;
        else if (a.OemRecoveryPartitions.Count > 0)
            _recovery.Text = "Recovery partícia nájdená, ale bez WIM obrazu.";
        else
            _recovery.Text = "Recovery sa nenašla.";

        _grid.Rows.Clear();
        foreach (var check in a.Checks)
        {
            var index = _grid.Rows.Add(check.Name, LevelText(check.Level), check.Detail);
            _grid.Rows[index].Cells[1].Style.ForeColor = LevelColor(check.Level);
            _grid.Rows[index].Cells[1].Style.Font = new Font("Segoe UI Semibold", 8.5f);
        }

        _summary.Text = a.Summary;
        _summary.ForeColor = a.CanRepair ? Theme.Warning : Theme.Muted;

        if (!string.IsNullOrWhiteSpace(a.RepairBlockedReason))
        {
            _blocked.Text = "Oprava nie je dostupná: " + a.RepairBlockedReason;
            _blocked.Visible = true;
        }

        _chkRenewSignature.Visible = !a.Target.IsGpt && a.SignatureCollision;
        SetStatus(a.Summary, a.CanRepair ? Theme.Warning : Theme.Muted);
    }

    private async Task RepairAsync(bool alreadyConfirmed = false)
    {
        if (_busy || _analysis is not { CanRepair: true } a) return;

        if (!alreadyConfirmed)
        {
            var mode = a.IsUefi ? "UEFI" : "BIOS / MBR";
            var steps = a.IsUefi
                ? "1. záloha BCD a boot súborov\n2. bcdboot /f UEFI\n3. doplnenie \\EFI\\Boot\\bootx64.efi"
                : "1. záloha MBR, VBR a BCD\n2. nastavenie active partície\n3. bcdboot /f BIOS\n4. bootsect /nt60 /mbr";
            if (_chkRenewSignature.Visible && _chkRenewSignature.Checked)
                steps = "0. nová disk signature\n" + steps;

            var text =
                $"Disk {a.Target.DiskNumber} · {a.Target.Model}\n" +
                $"Režim: {mode}\n" +
                $"Windows: {a.WindowsPartitions[0].DriveLetter}:\\Windows\n\n" +
                "Program vykoná:\n" + steps + "\n\n" +
                "Pred opravou sa vytvorí záloha boot súborov do Dokumentov.\n\n" +
                "POZOR: Zapisuje sa do boot sektora a BCD. Ak je disk určený pre iný počítač, " +
                "skontrolujte, či režim UEFI/BIOS zodpovedá cieľovému počítaču.\n\n" +
                "Pokračovať?";

            using var confirm = new ConfirmForm("Oprava bootovania", text, "OPRAVIŤ",
                "Rozumiem, že program zapíše do boot sektora a BCD.");
            if (confirm.ShowDialog(FindForm()) != DialogResult.OK) return;
        }

        SetBusy(true, "Opravujem bootovanie…");
        try
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<string>(t => _status.Text = t);
            var result = await BootRepairService.RepairAsync(a, _chkRenewSignature.Checked,
                AppendLog, progress, _cts.Token);
            MessageBox.Show(result.Summary + "\n\nZáloha:\n" + result.BackupFolder,
                "Oprava bootovania", MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Oprava bola zrušená.");
        }
        catch (Exception ex)
        {
            AppendLog("Oprava zlyhala: " + ex.Message);
            MessageBox.Show("Oprava zlyhala:\n\n" + ex.Message, "Oprava bootovania",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            SetBusy(false);
        }

        await CheckAsync();
    }

    private async Task AddRecoveryAsync(bool winRe)
    {
        if (_busy || _analysis is not { } a) return;
        var image = winRe ? a.WinReImage : a.OemImage;
        var allowed = winRe ? a.CanAddWinRe : a.CanAddOemRecovery;
        if (!allowed || image is null)
        {
            MessageBox.Show(winRe
                    ? "Windows RE obraz sa nenašiel."
                    : "OEM recovery WIM sa nenašiel. Recovery partícia existuje, ale bez boot.wim.",
                "Recovery", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var title = winRe ? "Windows RE" : $"OEM recovery ({a.Vendor})";
        var text =
            $"Pridá sa položka „{title}“ do boot menu.\n\n" +
            $"Obraz: {image.Path}\n\n" +
            "Program zálohuje BCD a pridá ramdisk záznam s boot.sdi a WIM. " +
            "Tento záznam môže byť pri niektorých OEM recovery potrebné overiť; " +
            "záloha BCD je uložená v Dokumentoch.\n\nPokračovať?";
        using var confirm = new ConfirmForm("Pridať recovery", text, "RECOVERY",
            "Rozumiem, že sa upraví BCD.");
        if (confirm.ShowDialog(FindForm()) != DialogResult.OK) return;

        SetBusy(true, "Pridávam recovery…");
        try
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<string>(t => _status.Text = t);
            var result = await BootRepairService.AddRecoveryAsync(a, image, AppendLog,
                progress, _cts.Token);
            MessageBox.Show(result.Summary, "Recovery", MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Pridanie recovery bolo zrušené.");
        }
        catch (Exception ex)
        {
            AppendLog("Pridanie recovery zlyhalo: " + ex.Message);
            MessageBox.Show("Recovery sa nepodarilo pridať:\n\n" + ex.Message, "Recovery",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            SetBusy(false);
        }

        await CheckAsync();
    }

    private async Task ConvertAsync()
    {
        if (_busy || _analysis is not { CanConvert: true } a) return;

        var text =
            $"Disk {a.Target.DiskNumber} · {a.Target.Model}\n" +
            $"{a.Target.PartitionStyle} → GPT\n\n" +
            "Pred konverziou:\n" +
            "• zálohujte dôležité dáta\n" +
            "• disk musí byť iný ako aktuálny systémový\n" +
            "• nesmie byť BitLocker bez recovery kľúča\n\n" +
            "Po konverzii:\n" +
            "• v BIOSe zapnite UEFI a vypnite CSM/Legacy\n" +
            "• nastavte boot z Windows Boot Manager\n" +
            "• NEMEŇTE súčasne SATA/RAID/AHCI režim — hrozí INACCESSIBLE_BOOT_DEVICE\n" +
            "• program sa pokúsi obnoviť UEFI boot súbory\n\n" +
            "Pokračovať?";

        using var confirm = new ConfirmForm("Konverzia MBR → GPT", text, "PREVIESŤ",
            "Rozumiem riziku a mám zálohu dát.");
        if (confirm.ShowDialog(FindForm()) != DialogResult.OK) return;

        SetBusy(true, "Konvertujem MBR → GPT…");
        try
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<string>(t => _status.Text = t);
            var result = await BootRepairService.ConvertMbrToGptAsync(a, AppendLog, progress,
                _cts.Token);
            MessageBox.Show(result.Summary + "\n\nZáloha:\n" + result.BackupFolder,
                "Konverzia MBR → GPT", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Konverzia bola zrušená.");
        }
        catch (Exception ex)
        {
            AppendLog("Konverzia zlyhala: " + ex.Message);
            MessageBox.Show("Konverzia zlyhala:\n\n" + ex.Message +
                "\n\nSkontrolujte log a stav disku. Pred ďalším krokom obnovte zálohu, ak je to potrebné.",
                "Konverzia MBR → GPT", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            SetBusy(false);
        }

        await LoadDisksAsync();
        await CheckAsync();

        if (_analysis is { CanRepair: true, IsUefi: true } fresh)
        {
            var answer = MessageBox.Show(
                "Disk je prevedený na GPT. Chcete teraz obnoviť UEFI boot súbory " +
                "(bcdboot /f UEFI)?\n\nPo dokončení vypnite v BIOSe CSM/Legacy a nastavte UEFI.",
                "Konverzia hotová", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
                await RepairAsync(alreadyConfirmed: true);
        }
    }

    private void ShowBiosInfo()
    {
        var text =
            "UEFI (disk GPT)\n" +
            "• zapnite UEFI boot\n" +
            "• vypnite CSM / Legacy / Compatibility Support Module\n" +
            "• boot order: Windows Boot Manager\n" +
            "• Secure Boot zapnite, ak ho Windows podporuje\n" +
            "• SATA/NVMe režim (AHCI/RAID/IDE) nechajte rovnaký ako pri inštalácii Windows\n\n" +
            "BIOS / Legacy (disk MBR)\n" +
            "• zapnite CSM / Legacy boot\n" +
            "• vypnite Secure Boot\n" +
            "• boot z active systémovej partície\n" +
            "• SATA režim nezmeňte súčasne s opravou bootu\n\n" +
            "Konverzia MBR → GPT\n" +
            "• po konverzii prepnite BIOS na UEFI a vypnite CSM\n" +
            "• ak Windows po prepnutí neštartuje, býva to zmena radiča SATA/RAID/AHCI " +
            "(INACCESSIBLE_BOOT_DEVICE); vráťte pôvodný režim alebo nainštalujte správny driver\n" +
            "• ideálne majte pripravený záchranný USB s WinRE\n\n" +
            "Nikdy nemeňte viac nastavení naraz. Po každej zmene v BIOSe skúste štart.";

        MessageBox.Show(text, "BIOS / UEFI nastavenia", MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ClearResults()
    {
        _grid.Rows.Clear();
        _summary.Text = "Vyberte disk a spustite kontrolu.";
        _summary.ForeColor = Theme.Muted;
        _firmware.Text = "";
        _layout.Text = "";
        _windows.Text = "";
        _bcd.Text = "";
        _recovery.Text = "";
        _blocked.Visible = false;
        _blocked.Text = "";
        _log.Clear();
        UpdateButtons();
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        _btnCheck.Enabled = !busy;
        _btnReload.Enabled = !busy;
        _btnCancel.Visible = busy;
        _progress.Visible = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (status is not null) SetStatus(status, Theme.Muted);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var can = !_busy && _analysis is not null;
        _btnRepair.Enabled = can && _analysis!.CanRepair;
        _btnWinRe.Enabled = can && _analysis!.CanAddWinRe;
        _btnOem.Enabled = can && _analysis!.CanAddOemRecovery;
        _btnConvert.Enabled = can && _analysis!.CanConvert;
        _diskCombo.Enabled = !_busy;
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private static string LevelText(BootCheckLevel level) => level switch
    {
        BootCheckLevel.Ok => "OK",
        BootCheckLevel.Info => "info",
        BootCheckLevel.Warning => "pozor",
        BootCheckLevel.Error => "chyba",
        _ => ""
    };

    private static Color LevelColor(BootCheckLevel level) => level switch
    {
        BootCheckLevel.Ok => Theme.Success,
        BootCheckLevel.Info => Theme.Muted,
        BootCheckLevel.Warning => Theme.Warning,
        BootCheckLevel.Error => Theme.Danger,
        _ => Theme.Muted
    };

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
        Log.Write("Boot: " + line);
    }
}
