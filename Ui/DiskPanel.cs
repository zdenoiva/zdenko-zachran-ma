using NtfsRescue.Core;
using NtfsRescue.Repair;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class DiskPanel : UserControl
{
    private readonly ComboBox _diskCombo = new();
    private readonly Button _btnReload = new();
    private readonly Button _btnErase = new();
    private readonly Button _btnCancel = new();
    private readonly RadioButton _rdoQuick = new();
    private readonly RadioButton _rdoFull = new();
    private readonly CheckBox _chkCreate = new();
    private readonly ComboBox _fsCombo = new();
    private readonly ComboBox _styleCombo = new();
    private readonly ComboBox _letterCombo = new();
    private readonly TextBox _labelBox = new();
    private readonly Label _diskInfo = new();
    private readonly Label _warning = new();
    private readonly Label _blocked = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly RichTextBox _log = new();

    private List<DiskInfo> _disks = new();
    private DiskInfo? _selected;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _loaded;

    public event Action? DiskChanged;

    public DiskPanel()
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildInfo(), 0, 1);
        root.Controls.Add(BuildOptions(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        root.Controls.Add(BuildLog(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildToolbar()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
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
        _diskCombo.Width = 680;
        _diskCombo.Margin = new Padding(0, 4, 8, 0);
        _diskCombo.SelectedIndexChanged += (_, _) => SelectDisk();
        panel.Controls.Add(_diskCombo);

        _btnReload.Text = "Obnoviť disky";
        _btnReload.AutoSize = true;
        _btnReload.Margin = new Padding(0, 3, 0, 0);
        _btnReload.Click += async (_, _) => await LoadDisksAsync();
        panel.Controls.Add(_btnReload);
        return panel;
    }

    private Control BuildInfo()
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.Surface,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 10)
        };
        card.Paint += PaintBorder;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _diskInfo.Text = "Vyberte disk.";
        _diskInfo.AutoSize = true;
        _diskInfo.MaximumSize = new Size(960, 0);
        _diskInfo.Font = Theme.Body;
        _diskInfo.ForeColor = Theme.Text;
        layout.Controls.Add(_diskInfo);

        _blocked.AutoSize = true;
        _blocked.MaximumSize = new Size(960, 0);
        _blocked.ForeColor = Theme.Danger;
        _blocked.Font = new Font("Segoe UI Semibold", 9f);
        _blocked.Visible = false;
        _blocked.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_blocked);

        _warning.AutoSize = true;
        _warning.MaximumSize = new Size(960, 0);
        _warning.ForeColor = Theme.Warning;
        _warning.Font = Theme.Small;
        _warning.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_warning);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildOptions()
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.Surface,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 10)
        };
        card.Paint += PaintBorder;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 4,
            BackColor = Theme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _rdoQuick.Text = "Odstrániť partície (rýchlo)";
        _rdoQuick.AutoSize = true;
        _rdoQuick.Checked = true;
        _rdoQuick.Font = new Font("Segoe UI Semibold", 9.5f);
        _rdoQuick.CheckedChanged += (_, _) => UpdateMode();
        layout.Controls.Add(_rdoQuick, 0, 0);
        layout.SetColumnSpan(_rdoQuick, 2);

        _rdoFull.Text = "Úplne prepísať disk nulami (nevratné)";
        _rdoFull.AutoSize = true;
        _rdoFull.Font = new Font("Segoe UI Semibold", 9.5f);
        _rdoFull.ForeColor = Theme.Danger;
        _rdoFull.CheckedChanged += (_, _) => UpdateMode();
        layout.Controls.Add(_rdoFull, 2, 0);
        layout.SetColumnSpan(_rdoFull, 2);

        _chkCreate.Text = "Po vyčistení vytvoriť jednu partíciu";
        _chkCreate.AutoSize = true;
        _chkCreate.Checked = true;
        _chkCreate.Margin = new Padding(0, 8, 0, 0);
        _chkCreate.CheckedChanged += (_, _) => UpdateMode();
        layout.Controls.Add(_chkCreate, 0, 1);
        layout.SetColumnSpan(_chkCreate, 2);

        layout.Controls.Add(KeyLabel("Štýl disku:"), 0, 2);
        _styleCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _styleCombo.Items.AddRange(new object[] { "GPT", "MBR" });
        _styleCombo.SelectedIndex = 0;
        _styleCombo.Width = 130;
        _styleCombo.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_styleCombo, 1, 2);

        layout.Controls.Add(KeyLabel("Systém súborov:"), 2, 2);
        _fsCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _fsCombo.Items.AddRange(new object[] { "NTFS", "FAT32" });
        _fsCombo.SelectedIndex = 0;
        _fsCombo.Width = 130;
        _fsCombo.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_fsCombo, 3, 2);

        layout.Controls.Add(KeyLabel("Názov partície:"), 0, 3);
        _labelBox.Text = "DATA";
        _labelBox.Width = 220;
        _labelBox.MaxLength = 32;
        _labelBox.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_labelBox, 1, 3);

        layout.Controls.Add(KeyLabel("Písmeno:"), 2, 3);
        _letterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _letterCombo.Width = 130;
        _letterCombo.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_letterCombo, 3, 3);

        card.Controls.Add(layout);
        return card;
    }

    private static Label KeyLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Small,
        ForeColor = Theme.Muted,
        Margin = new Padding(0, 9, 8, 0)
    };

    private Control BuildActions()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 8)
        };

        _btnErase.Text = "Vymazať disk…";
        _btnErase.AutoSize = true;
        _btnErase.Font = new Font("Segoe UI Semibold", 9.5f);
        _btnErase.ForeColor = Color.White;
        _btnErase.BackColor = Theme.Danger;
        _btnErase.FlatStyle = FlatStyle.Flat;
        _btnErase.FlatAppearance.BorderSize = 0;
        _btnErase.Padding = new Padding(14, 0, 14, 0);
        _btnErase.MinimumSize = new Size(160, 38);
        _btnErase.Margin = new Padding(0, 4, 10, 4);
        _btnErase.Click += async (_, _) => await EraseAsync();
        panel.Controls.Add(_btnErase);

        _btnCancel.Text = "Zrušiť";
        _btnCancel.AutoSize = true;
        _btnCancel.Visible = false;
        _btnCancel.Margin = new Padding(0, 3, 10, 0);
        _btnCancel.Click += (_, _) => _cts?.Cancel();
        panel.Controls.Add(_btnCancel);

        _progress.Visible = false;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Size = new Size(260, 18);
        _progress.Margin = new Padding(0, 12, 10, 0);
        panel.Controls.Add(_progress);

        _status.Text = "Vyberte disk.";
        _status.AutoSize = true;
        _status.ForeColor = Theme.Muted;
        _status.Margin = new Padding(0, 14, 0, 0);
        panel.Controls.Add(_status);
        return panel;
    }

    private Control BuildLog()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(12)
        };
        card.Paint += PaintBorder;
        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.Surface;
        _log.Font = Theme.Mono;
        _log.WordWrap = true;
        card.Controls.Add(_log);
        return card;
    }

    private void PaintBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel) return;
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
    }

    private async Task LoadDisksAsync()
    {
        if (_busy) return;
        SetBusy(true, "Načítavam disky…");
        try
        {
            var previous = _selected?.Number;
            _disks = await DiskInfoCatalog.LoadAsync(CancellationToken.None);
            _diskCombo.Items.Clear();
            foreach (var disk in _disks) _diskCombo.Items.Add(disk);
            if (_disks.Count == 0)
            {
                _selected = null;
                _diskInfo.Text = "Nenašli sa žiadne fyzické disky.";
                UpdateFreeLetters();
                return;
            }

            _selected = previous.HasValue
                ? _disks.FirstOrDefault(d => d.Number == previous.Value) ?? _disks[0]
                : _disks.FirstOrDefault(d => !d.IsCurrentSystem) ?? _disks[0];
            _diskCombo.SelectedItem = _selected;
            SelectDisk();
            _loaded = true;
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
        }
    }

    private void SelectDisk()
    {
        _selected = _diskCombo.SelectedItem as DiskInfo;
        if (_selected is null)
        {
            _diskInfo.Text = "Vyberte disk.";
            _blocked.Visible = false;
            _btnErase.Enabled = false;
            return;
        }

        var d = _selected;
        _diskInfo.Text =
            $"Disk {d.Number} · {d.Model}\n" +
            $"{Fmt.Bytes(d.Size)} · {(string.IsNullOrWhiteSpace(d.PartitionStyle) ? "?" : d.PartitionStyle)} · {d.BusType}" +
            (string.IsNullOrWhiteSpace(d.Serial) ? "" : $" · S/N {d.Serial}") +
            (d.IsOffline ? " · offline" : "") +
            (d.IsReadOnly ? " · read-only" : "");

        var appDisk = VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "");
        var blocked = "";
        if (d.IsCurrentSystem)
            blocked = "Toto je aktuálny systémový disk. Vymazanie je zablokované.";
        else if (appDisk >= 0 && appDisk == d.Number)
            blocked = "Program beží z tohto disku. Spustite ho z iného disku.";
        _blocked.Text = blocked;
        _blocked.Visible = blocked.Length > 0;
        _btnErase.Enabled = !_busy && blocked.Length == 0;

        if (d.PartitionStyle.Equals("MBR", StringComparison.OrdinalIgnoreCase))
            _styleCombo.SelectedItem = "MBR";
        else
            _styleCombo.SelectedItem = "GPT";
        UpdateFreeLetters();
        UpdateMode();
    }

    private void UpdateFreeLetters()
    {
        var selected = _letterCombo.SelectedItem as string;
        _letterCombo.Items.Clear();
        _letterCombo.Items.Add("Automaticky");
        var used = new HashSet<char>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (string.IsNullOrEmpty(drive.Name)) continue;
                var letter = char.ToUpperInvariant(drive.Name[0]);
                if (_selected is { } target)
                {
                    var location = VolumeMapper.Locate(letter);
                    if (location.IsValid && location.DiskNumber == target.Number)
                        continue;
                }
                used.Add(letter);
            }
        }
        catch { }

        for (char c = 'D'; c <= 'Z'; c++)
            if (!used.Contains(c)) _letterCombo.Items.Add(c.ToString());
        _letterCombo.SelectedIndex = selected is not null && _letterCombo.Items.Contains(selected)
            ? _letterCombo.Items.IndexOf(selected)
            : 0;
    }

    private void UpdateMode()
    {
        var full = _rdoFull.Checked;
        _btnErase.Text = full ? "Vymazať a prepísať…" : "Odstrániť partície…";
        _btnErase.BackColor = full ? Color.FromArgb(165, 40, 40) : Theme.Danger;

        var create = _chkCreate.Checked;
        _fsCombo.Enabled = !_busy && create;
        _styleCombo.Enabled = !_busy && create;
        _letterCombo.Enabled = !_busy && create;
        _labelBox.Enabled = !_busy && create;

        var warnings = new List<string>();
        if (full)
            warnings.Add("Celý disk sa prepíše nulami. Dáta sa nebudú dať obnoviť. " +
                         "Na SSD nemusí samotné prepísanie nulami zaručiť fyzické vymazanie; " +
                         "použite výrobcom doporučený secure erase.");
        else
            warnings.Add("Odstránia sa partície a dátové štruktúry. Dáta môžu byť stále obnoviteľné.");

        if (create && _fsCombo.SelectedItem as string == "FAT32" &&
            _selected is { Size: > 32L * 1024 * 1024 * 1024 })
            warnings.Add("FAT32 nad 32 GB nemusí Windows naformátovať. Odporúčame NTFS.");
        if (create && _styleCombo.SelectedItem as string == "MBR" &&
            _selected is { Size: > 2_199_023_255_552 })
            warnings.Add("MBR nepodporuje disk nad 2 TB. Zvoľte GPT.");
        if (full && _selected is { } d)
            warnings.Add($"Odhad: {Fmt.Bytes(d.Size)} nulami; môže trvať desiatky minút až hodiny.");

        _warning.Text = string.Join("\n", warnings);
    }

    private async Task EraseAsync()
    {
        if (_busy || _selected is null) return;
        var d = _selected;
        if (d.IsCurrentSystem)
        {
            MessageBox.Show("Aktuálny systémový disk nie je možné vymazať.", "Vymazanie disku",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var options = new EraseOptions
        {
            FullWipe = _rdoFull.Checked,
            CreatePartition = _chkCreate.Checked,
            FileSystem = _fsCombo.SelectedItem as string ?? "NTFS",
            PartitionStyle = _styleCombo.SelectedItem as string ?? "GPT",
            Letter = _letterCombo.SelectedItem is string s && s.Length == 1 ? s[0] : null,
            Label = string.IsNullOrWhiteSpace(_labelBox.Text) ? "DATA" : _labelBox.Text.Trim()
        };

        var text =
            $"Disk {d.Number} · {d.Model}\n" +
            $"{Fmt.Bytes(d.Size)} · {(string.IsNullOrWhiteSpace(d.PartitionStyle) ? "?" : d.PartitionStyle)} · {d.BusType}\n" +
            (string.IsNullOrWhiteSpace(d.Serial) ? "" : $"S/N: {d.Serial}\n") +
            $"\nRežim: {(options.FullWipe ? "ÚPLNÉ PREPÍSANIE NULAMI" : "ODSTRÁNENIE PARTÍCIÍ")}\n" +
            (options.CreatePartition
                ? $"Vytvoriť partíciu: {options.FileSystem} · {options.PartitionStyle} · " +
                  $"{(options.Letter is { } l ? l + ":" : "automaticky")} · {options.Label}\n"
                : "Po vyčistení sa nevytvorí partícia.\n") +
            "\nTáto operácia je nevratná. Všetky dáta na disku budú zničené.\n\nPokračovať?";

        using var confirm = new ConfirmForm("Vymazanie disku", text, "VYMAZAŤ",
            "Rozumiem, že všetky dáta na tomto disku budú nevratne zničené.");
        if (confirm.ShowDialog(FindForm()) != DialogResult.OK) return;

        SetBusy(true, "Pripravujem vymazanie…");
        _log.Clear();
        try
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<EraseProgress>(p =>
            {
                if (p.BytesTotal > 0)
                {
                    var percent = (int)Math.Clamp(p.BytesDone * 100 / p.BytesTotal, 0, 100);
                    _progress.Value = percent;
                    _status.Text = $"{p.Phase} · {percent} % · {Fmt.Bytes((long)p.BytesPerSecond)}/s";
                }
                else
                {
                    _status.Text = p.Phase;
                }
            });

            var result = await DiskEraser.EraseAsync(d, options, progress, AppendLog, _cts.Token);
            _status.Text = result.Summary;
            AppendLog(result.Summary);
            Log.Info("Vymazanie disku: " + result.Summary);
            MessageBox.Show(result.Summary + (string.IsNullOrWhiteSpace(result.DriveLetter)
                    ? "" : $"\n\nNová partícia: {result.DriveLetter}:")
                , "Vymazanie disku", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DiskChanged?.Invoke();
        }
        catch (OperationCanceledException)
        {
            AppendLog("Operácia bola zrušená. Disk môže byť čiastočne prepísaný.");
            MessageBox.Show("Operácia bola zrušená. Disk môže byť čiastočne prepísaný.",
                "Vymazanie disku", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            AppendLog("Vymazanie zlyhalo: " + ex.Message);
            MessageBox.Show("Vymazanie zlyhalo:\n\n" + ex.Message, "Vymazanie disku",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            SetBusy(false);
        }

        await LoadDisksAsync();
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        _btnReload.Enabled = !busy;
        _diskCombo.Enabled = !busy;
        _rdoQuick.Enabled = !busy;
        _rdoFull.Enabled = !busy;
        _chkCreate.Enabled = !busy;
        _btnCancel.Visible = busy;
        _progress.Visible = busy;
        _progress.Style = busy ? ProgressBarStyle.Continuous : ProgressBarStyle.Continuous;
        if (!busy) _progress.Value = 0;
        var appDisk = VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "");
        _btnErase.Enabled = !busy && _selected is not null &&
                            !_selected.IsCurrentSystem &&
                            (appDisk < 0 || appDisk != _selected.Number);
        if (status is not null) SetStatus(status, Theme.Muted);
        if (!busy) UpdateMode();
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
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
        Log.Write("Disk: " + line);
    }
}

