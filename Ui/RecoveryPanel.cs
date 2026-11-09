using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class RecoveryPanel : UserControl
{
    private readonly TreeView _tree = new();
    private readonly TextBox _filter = new();
    private readonly TextBox _dest = new();
    private readonly CheckBox _chkDeleted = new();
    private readonly CheckBox _chkOverwrite = new();
    private readonly Button _btnLoad = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCsv = new();
    private readonly Label _lblSource = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblCount = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _btnCancel = new();

    private IRecoverySource? _source;
    private List<RecoveryEntry> _allEntries = new();
    private List<RecoveryEntry> _filteredEntries = new();
    private CancellationTokenSource? _cts;
    private bool _updatingCheck;
    private bool _copying;

    public bool IsBusy => _cts is not null;
    public bool IsCopying => _copying;

    public void CancelOperation()
    {
        try { _cts?.Cancel(); } catch { }
    }

    public event Action<bool>? BusyChanged;

    public RecoveryPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(12, 10, 12, 6),
            BackColor = Theme.Background
        };

        _lblSource.Text = "Zdroj: nevybraný";
        _lblSource.AutoSize = true;
        _lblSource.Font = Theme.H3;
        _lblSource.ForeColor = Theme.Text;
        _lblSource.Margin = new Padding(0, 9, 18, 0);
        toolbar.Controls.Add(_lblSource);

        _btnLoad.Text = "Načítať súbory";
        _btnLoad.AutoSize = true;
        _btnLoad.Click += async (_, _) => await LoadAsync();
        StyleButton(_btnLoad, primary: true);
        toolbar.Controls.Add(_btnLoad);

        _chkDeleted.Text = "Zobraziť aj zmazané";
        _chkDeleted.AutoSize = true;
        _chkDeleted.ForeColor = Theme.Muted;
        _chkDeleted.Margin = new Padding(10, 9, 6, 0);
        toolbar.Controls.Add(_chkDeleted);

        toolbar.Controls.Add(new Label
        {
            Text = "Filter:",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(12, 10, 2, 0)
        });

        _filter.Width = 180;
        _filter.PlaceholderText = "časť názvu alebo cesty";
        _filter.Margin = new Padding(0, 6, 4, 0);
        _filter.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyFilter(); }
        };
        toolbar.Controls.Add(_filter);

        var btnFilter = new Button { Text = "Filtrovať" };
        btnFilter.AutoSize = true;
        btnFilter.Click += (_, _) => ApplyFilter();
        StyleButton(btnFilter);
        toolbar.Controls.Add(btnFilter);

        var btnExpand = new Button { Text = "Rozbaliť" };
        btnExpand.AutoSize = true;
        btnExpand.Click += (_, _) => _tree.ExpandAll();
        StyleButton(btnExpand);
        toolbar.Controls.Add(btnExpand);

        var btnCollapse = new Button { Text = "Zbaliť" };
        btnCollapse.AutoSize = true;
        btnCollapse.Click += (_, _) => _tree.CollapseAll();
        StyleButton(btnCollapse);
        toolbar.Controls.Add(btnCollapse);

        _btnCsv.Text = "Export CSV";
        _btnCsv.AutoSize = true;
        _btnCsv.Click += async (_, _) => await ExportCsvAsync();
        StyleButton(_btnCsv);
        toolbar.Controls.Add(_btnCsv);

        toolbar.Controls.Add(new Label
        {
            Text = "Cieľ:",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(12, 10, 2, 0)
        });

        _dest.Width = 240;
        _dest.PlaceholderText = "vyberte cieľový priečinok";
        _dest.Text = "";
        _dest.Margin = new Padding(0, 6, 4, 0);
        toolbar.Controls.Add(_dest);

        var btnBrowse = new Button { Text = "Vybrať priečinok..." };
        btnBrowse.AutoSize = true;
        btnBrowse.Click += (_, _) => BrowseDestination();
        StyleButton(btnBrowse);
        toolbar.Controls.Add(btnBrowse);

        _chkOverwrite.Text = "Prepísať existujúce";
        _chkOverwrite.AutoSize = true;
        _chkOverwrite.ForeColor = Theme.Muted;
        _chkOverwrite.Margin = new Padding(4, 9, 10, 0);
        toolbar.Controls.Add(_chkOverwrite);

        _btnSave.Text = "Zachrániť";
        _btnSave.AutoSize = true;
        _btnSave.Click += async (_, _) => await SaveAsync();
        StyleButton(_btnSave, primary: true);
        toolbar.Controls.Add(_btnSave);

        var treePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 0, 12, 0),
            BackColor = Theme.Background
        };
        ConfigureTree();
        treePanel.Controls.Add(_tree);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(12, 6, 12, 8),
            BackColor = Theme.Background
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        _lblStatus.Text = "Pripravené.";
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = Theme.Muted;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        bottom.Controls.Add(_lblStatus, 0, 0);

        _lblCount.Text = "";
        _lblCount.AutoSize = true;
        _lblCount.ForeColor = Theme.Muted;
        _lblCount.TextAlign = ContentAlignment.MiddleRight;
        bottom.Controls.Add(_lblCount, 1, 0);

        _progress.Dock = DockStyle.Fill;
        _progress.Height = 18;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Maximum = 1000;
        _progress.Margin = new Padding(4, 2, 4, 0);
        bottom.Controls.Add(_progress, 2, 0);

        _btnCancel.Text = "Zrušiť";
        _btnCancel.Dock = DockStyle.Fill;
        _btnCancel.Enabled = false;
        _btnCancel.Click += (_, _) => _cts?.Cancel();
        bottom.Controls.Add(_btnCancel, 3, 0);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(treePanel, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);
    }

    private static void StyleButton(Button b, bool primary = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        b.BackColor = primary ? Theme.Accent : Theme.Surface;
        b.ForeColor = primary ? Color.White : Theme.Text;
        b.Font = new Font("Segoe UI Semibold", 9f);
        b.Cursor = Cursors.Hand;
        b.Height = 30;
        b.Padding = new Padding(8, 0, 8, 0);
        b.Margin = new Padding(0, 3, 8, 0);
    }

    private void ConfigureTree()
    {
        _tree.Dock = DockStyle.Fill;
        _tree.CheckBoxes = true;
        _tree.HideSelection = false;
        _tree.FullRowSelect = true;
        _tree.ShowLines = true;
        _tree.ShowRootLines = true;
        _tree.ItemHeight = 24;
        _tree.Font = Theme.Body;
        _tree.BackColor = Theme.Surface;
        _tree.BorderStyle = BorderStyle.FixedSingle;
        _tree.AfterCheck += (_, e) =>
        {
            if (_updatingCheck || e.Node is null) return;
            _updatingCheck = true;
            try
            {
                SetChildren(e.Node, e.Node.Checked);
                UpdateParents(e.Node.Parent);
            }
            finally { _updatingCheck = false; }
        };
        _tree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node is null) return;
            e.Node.Toggle();
        };
    }

    private static void SetChildren(TreeNode node, bool value)
    {
        foreach (TreeNode child in node.Nodes)
        {
            child.Checked = value;
            SetChildren(child, value);
        }
    }

    private static void UpdateParents(TreeNode? node)
    {
        while (node is not null)
        {
            bool any = node.Nodes.Cast<TreeNode>().Any(n => n.Checked);
            node.Checked = any;
            node = node.Parent;
        }
    }

    public void SetSource(IRecoverySource source, string? subtitle = null)
    {
        if (IsBusy) throw new InvalidOperationException("Prebieha operácia.");

        CloseSource();
        _source = source;
        _allEntries = new List<RecoveryEntry>();
        _filteredEntries = new List<RecoveryEntry>();
        _tree.Nodes.Clear();
        _lblSource.Text = $"Zdroj: {source.SourceName}" +
                          (string.IsNullOrWhiteSpace(subtitle) ? "" : " — " + subtitle);
        _lblStatus.Text = "Pripravené. Kliknite na „Načítať súbory“.";
        _lblCount.Text = "";
        _progress.Value = 0;
    }

    public void ClearSource()
    {
        if (IsBusy) return;
        CloseSource();
        _allEntries.Clear();
        _filteredEntries.Clear();
        _tree.Nodes.Clear();
        _lblSource.Text = "Zdroj: nevybraný";
        _lblStatus.Text = "Pripravené.";
        _lblCount.Text = "";
        _progress.Value = 0;
    }

    private void CloseSource()
    {
        try { _source?.Dispose(); } catch { }
        _source = null;
    }

    public async Task LoadAsync()
    {
        if (_source is null)
        {
            MessageBox.Show("Najprv vyberte disk alebo partíciu.", AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (IsBusy) return;
        _cts = new CancellationTokenSource();
        SetBusy(true, "Načítavam súbory...");
        _progress.Style = ProgressBarStyle.Marquee;

        try
        {
            var includeDeleted = _chkDeleted.Checked;
            var progress = new Progress<ScanProgress>(p =>
            {
                _lblStatus.Text = p.Total > 0
                    ? $"{p.Phase}: {p.Current:N0} / {p.Total:N0}"
                    : $"{p.Phase}: {p.Current:N0}";
            });

            var source = _source;
            await Task.Run(() => source.Load(includeDeleted, progress, _cts.Token), _cts.Token);

            _allEntries = source.Entries
                .OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ApplyFilter();

            int fileCount = _allEntries.Count(e => !e.IsDirectory);
            _lblStatus.Text = $"Hotovo. Nájdených {fileCount:N0} súborov.";
            Log.Info($"Záchrana: {source.SourceName}, {fileCount:N0} súborov, {Fmt.Bytes(source.TotalBytes)}.");
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = "Načítanie zrušené.";
        }
        catch (Exception ex)
        {
            Log.Error("Načítanie súborov zlyhalo: " + ex);
            MessageBox.Show("Súbory sa nepodarilo načítať:\n\n" + ex.Message,
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            _lblStatus.Text = "Chyba: " + ex.Message;
        }
        finally
        {
            SetBusy(false, null);
            _progress.Style = ProgressBarStyle.Continuous;
        }
    }

    private void ApplyFilter()
    {
        var text = _filter.Text.Trim();
        _filteredEntries = text.Length == 0
            ? _allEntries
            : _allEntries.Where(e => e.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                     e.FullPath.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();

        PopulateTree(_filteredEntries);
    }

    private void PopulateTree(List<RecoveryEntry> entries)
    {
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            var root = new TreeNode("Súbory") { Tag = null };
            var dirs = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var path = (entry.FullPath ?? "").Replace('/', '\\').Trim('\\');
                if (entry.IsDirectory)
                {
                    var node = EnsureDir(path, dirs, root);
                    node.Tag = entry;
                    node.Text = "📁 " + entry.Name;
                    node.ForeColor = entry.Deleted ? Color.Gray : Theme.Text;
                }
                else
                {
                    var parentPath = Path.GetDirectoryName(path) ?? "";
                    var parent = EnsureDir(parentPath, dirs, root);
                    var node = new TreeNode($"📄 {entry.Name}   ({Fmt.Bytes(entry.Size)}, {entry.Status})")
                    {
                        Tag = entry,
                        ForeColor = entry.Deleted ? Color.Gray : Theme.Text
                    };
                    parent.Nodes.Add(node);
                }
            }

            _tree.Nodes.Add(root);
            root.Expand();
            foreach (TreeNode node in root.Nodes) node.Expand();
        }
        finally
        {
            _tree.EndUpdate();
        }

        var files = entries.Where(e => !e.IsDirectory).ToList();
        long total = files.Sum(e => Math.Max(0, e.Size));
        _lblCount.Text = $"{files.Count:N0} súborov, {Fmt.Bytes(total)}";
    }

    private static TreeNode EnsureDir(string path, Dictionary<string, TreeNode> dirs, TreeNode root)
    {
        path = path.Trim('\\');
        if (path.Length == 0) return root;
        if (dirs.TryGetValue(path, out var existing)) return existing;

        var parentPath = Path.GetDirectoryName(path) ?? "";
        var parent = EnsureDir(parentPath, dirs, root);
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name)) name = path;
        var node = new TreeNode("📁 " + name) { Tag = null, ForeColor = Theme.Text };
        parent.Nodes.Add(node);
        dirs[path] = node;
        return node;
    }

    private async Task SaveAsync()
    {
        if (IsBusy || _source is null) return;

        var files = CollectCheckedFiles(_tree.Nodes);
        if (files.Count == 0 && _tree.SelectedNode is { } selected)
            files = CollectFiles(selected);

        if (files.Count == 0)
        {
            var all = _filteredEntries.Where(e => !e.IsDirectory).ToList();
            if (all.Count == 0)
            {
                MessageBox.Show("Zoznam súborov je prázdny. Najprv načítajte súbory.",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var answer = MessageBox.Show(
                "Nie sú označené žiadne súbory.\n\nChcete zachrániť všetky načítané súbory?",
                "Zachrániť", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            files = all;
        }

        var dest = _dest.Text.Trim();
        if (dest.Length == 0)
        {
            BrowseDestination();
            dest = _dest.Text.Trim();
            if (dest.Length == 0) return;
        }

        try { Directory.CreateDirectory(dest); }
        catch (Exception ex)
        {
            MessageBox.Show("Cieľový priečinok sa nedá vytvoriť:\n" + ex.Message, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        long totalBytes = files.Sum(f => Math.Max(0, f.Size));
        var sizeText = totalBytes > 0 ? Fmt.Bytes(totalBytes) : "veľkosť neznáma";
        var confirm = MessageBox.Show(
            $"Zachrániť {files.Count:N0} súborov ({sizeText}) do:\n\n{dest}\n\n" +
            "Na zdrojový disk sa nezapisuje.",
            "Potvrdenie záchrany", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK) return;

        _copying = true;
        _cts = new CancellationTokenSource();
        SetBusy(true, "Zachraňujem...");
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = 0;

        try
        {
            var progress = new Progress<CopyProgress>(p =>
            {
                if (p.BytesTotal > 0)
                    _progress.Value = (int)Math.Min(1000, p.BytesDone * 1000 / p.BytesTotal);
                _lblStatus.Text = string.IsNullOrEmpty(p.CurrentFile)
                    ? $"Zachraňujem... {p.FilesDone:N0}/{p.FilesTotal:N0}"
                    : $"{p.FilesDone:N0}/{p.FilesTotal:N0}  {p.CurrentFile}";
            });

            var options = new CopyOptions
            {
                DestinationRoot = dest,
                Overwrite = _chkOverwrite.Checked,
                SkipExistingSameSize = !_chkOverwrite.Checked,
                VerifySize = true
            };

            int messageCount = 0;
            void OnMessage(string message)
            {
                messageCount++;
                bool important = message.StartsWith("CHYBA", StringComparison.Ordinal)
                                 || message.StartsWith("Preskočené", StringComparison.Ordinal)
                                 || message.Contains("zlyhalo", StringComparison.OrdinalIgnoreCase)
                                 || message.Contains("reťazec", StringComparison.OrdinalIgnoreCase)
                                 || message.Contains("súvislo", StringComparison.OrdinalIgnoreCase)
                                 || message.Contains("kontrola veľkosti", StringComparison.OrdinalIgnoreCase);
                if (important || messageCount % 100 == 0)
                    Log.Info(message);
            }

            var source = _source;
            var report = await Task.Run(() => RecoveryCopier.CopyAsync(
                source, files, options, progress, OnMessage, _cts.Token), _cts.Token);

            _lblStatus.Text = $"Hotovo: {report.Copied:N0} OK, {report.Skipped:N0} preskočených, {report.Failed:N0} chýb.";
            Log.Info($"Záchrana dokončená: {report.Copied} OK, {report.Skipped} preskočených, {report.Failed} chýb, {Fmt.Bytes(report.Bytes)} za {report.Duration:hh\\:mm\\:ss}.");

            var msg = $"Skopírované: {report.Copied:N0}\nPreskočené: {report.Skipped:N0}\nChyby: {report.Failed:N0}\n" +
                      $"Dáta: {Fmt.Bytes(report.Bytes)}\nČas: {report.Duration:hh\\:mm\\:ss}";
            if (report.Errors.Count > 0)
                msg += "\n\nPrvé chyby:\n" + string.Join("\n", report.Errors.Take(10));

            MessageBox.Show(msg, "Záchrana dokončená", MessageBoxButtons.OK,
                report.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = "Kopírovanie zrušené.";
            Log.Warn("Kopírovanie zrušené používateľom.");
        }
        catch (Exception ex)
        {
            Log.Error("Kopírovanie zlyhalo: " + ex);
            MessageBox.Show("Kopírovanie zlyhalo:\n\n" + ex.Message, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _copying = false;
            SetBusy(false, null);
        }
    }

    private static List<RecoveryEntry> CollectCheckedFiles(TreeNodeCollection nodes)
    {
        var result = new List<RecoveryEntry>();
        foreach (TreeNode node in nodes)
            CollectChecked(node, result);
        return result;
    }

    private static void CollectChecked(TreeNode node, List<RecoveryEntry> result)
    {
        if (node.Checked && node.Tag is RecoveryEntry entry && !entry.IsDirectory)
            result.Add(entry);
        foreach (TreeNode child in node.Nodes)
            CollectChecked(child, result);
    }

    private static List<RecoveryEntry> CollectFiles(TreeNode node)
    {
        var result = new List<RecoveryEntry>();
        if (node.Tag is RecoveryEntry entry && !entry.IsDirectory)
            result.Add(entry);
        foreach (TreeNode child in node.Nodes)
            result.AddRange(CollectFiles(child));
        return result;
    }

    private async Task ExportCsvAsync()
    {
        if (_filteredEntries.Count == 0)
        {
            MessageBox.Show("Najprv načítajte súbory.", AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Directory.CreateDirectory(AppPaths.Inventories);
        using var dialog = new SaveFileDialog
        {
            Title = "Uložiť zoznam súborov",
            Filter = "CSV súbor (*.csv)|*.csv",
            FileName = $"inventar-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            InitialDirectory = AppPaths.Inventories
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            await CsvInventory.WriteAsync(_filteredEntries, dialog.FileName, CancellationToken.None);
            MessageBox.Show("Zoznam uložený:\n" + dialog.FileName, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("CSV sa nepodarilo uložiť:\n" + ex.Message, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BrowseDestination()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Vyberte cieľový priečinok pre záchranu súborov (iný fyzický disk).",
            SelectedPath = Directory.Exists(_dest.Text) ? _dest.Text : AppPaths.RecoveryDefault,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _dest.Text = dialog.SelectedPath;
    }

    private void SetBusy(bool busy, string? status)
    {
        _btnLoad.Enabled = !busy;
        _btnSave.Enabled = !busy;
        _btnCsv.Enabled = !busy;
        _chkDeleted.Enabled = !busy;
        _chkOverwrite.Enabled = !busy;
        _filter.Enabled = !busy;
        _tree.Enabled = !busy;
        _btnCancel.Enabled = busy;
        _btnCancel.Visible = busy;

        try { BusyChanged?.Invoke(busy); } catch { }

        if (status is not null) _lblStatus.Text = status;

        if (!busy)
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _cts?.Cancel(); } catch { }
            CloseSource();
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
