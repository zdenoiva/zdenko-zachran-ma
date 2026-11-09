using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class WizardPanel : UserControl
{
    private readonly Label _lblPartition = new();
    private readonly Label _lblIcon = new();
    private readonly Label _lblTitle = new();
    private readonly Label _lblDetails = new();
    private readonly Label _lblSmart = new();
    private readonly FlowLayoutPanel _actions = new();
    private readonly Label _lblHint = new();
    private readonly Panel _card = new();
    private readonly SmartHealthControl _smartCard = new();
    private bool _busy;

    public event Action<RecommendedAction>? ActionSelected;

    public WizardPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();
        ShowEmpty();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 16, 18, 16),
            BackColor = Theme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _card.Dock = DockStyle.Top;
        _card.AutoSize = true;
        _card.BackColor = Theme.Surface;
        _card.Padding = new Padding(24);
        _card.Margin = new Padding(0, 0, 0, 14);
        _card.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, _card.Width - 1, _card.Height - 1);
        };

        var cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2
        };
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblIcon.AutoSize = true;
        _lblIcon.Font = new Font("Segoe UI", 26f);
        _lblIcon.TextAlign = ContentAlignment.TopLeft;
        _lblIcon.Margin = new Padding(0, 2, 14, 0);

        _lblPartition.AutoSize = true;
        _lblPartition.Font = Theme.Small;
        _lblPartition.ForeColor = Theme.Muted;
        _lblPartition.Margin = new Padding(0, 0, 0, 4);

        _lblTitle.AutoSize = true;
        _lblTitle.Font = new Font("Segoe UI Semibold", 17f);
        _lblTitle.ForeColor = Theme.Text;
        _lblTitle.MaximumSize = new Size(900, 0);
        _lblTitle.Margin = new Padding(0, 0, 0, 8);

        _lblDetails.AutoSize = true;
        _lblDetails.Font = Theme.Body;
        _lblDetails.ForeColor = Theme.Muted;
        _lblDetails.MaximumSize = new Size(900, 0);
        _lblDetails.Margin = new Padding(0, 0, 0, 4);

        _lblSmart.AutoSize = true;
        _lblSmart.MaximumSize = new Size(860, 0);
        _lblSmart.TextAlign = ContentAlignment.MiddleLeft;
        _lblSmart.Padding = new Padding(12, 7, 12, 7);
        _lblSmart.Margin = new Padding(0, 8, 0, 0);
        _lblSmart.Font = new Font("Segoe UI Semibold", 9.5f);

        var titleStack = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        titleStack.Controls.Add(_lblPartition, 0, 0);
        titleStack.Controls.Add(_lblTitle, 0, 1);
        titleStack.Controls.Add(_lblDetails, 0, 2);

        cardLayout.Controls.Add(_lblIcon, 0, 0);
        cardLayout.SetRowSpan(_lblIcon, 2);
        cardLayout.Controls.Add(titleStack, 1, 0);
        cardLayout.Controls.Add(_lblSmart, 1, 1);
        _smartCard.Margin = new Padding(16, 2, 0, 2);
        _smartCard.Anchor = AnchorStyles.Top;
        cardLayout.Controls.Add(_smartCard, 2, 0);
        cardLayout.SetRowSpan(_smartCard, 2);

        _card.Controls.Add(cardLayout);

        _actions.Dock = DockStyle.Top;
        _actions.AutoSize = true;
        _actions.WrapContents = true;
        _actions.Padding = new Padding(0, 4, 0, 12);
        _actions.BackColor = Theme.Background;

        _lblHint.AutoSize = true;
        _lblHint.MaximumSize = new Size(900, 0);
        _lblHint.Font = Theme.Small;
        _lblHint.ForeColor = Theme.Muted;
        _lblHint.Text = "Program najprv číta štruktúru disku a nič nemení. " +
                        "Až po vašom potvrdení vykoná opravu alebo záchranu. " +
                        "Pred opravou odporúčame skopírovať dôležité súbory.";

        root.Controls.Add(_card, 0, 0);
        root.Controls.Add(_actions, 0, 1);
        root.Controls.Add(_lblHint, 0, 2);
        Controls.Add(root);
    }

    public void ShowEmpty()
    {
        _lblPartition.Text = "";
        _lblIcon.Text = "◎";
        _lblIcon.ForeColor = Theme.Accent;
        _lblTitle.Text = "Vyberte disk alebo partíciu";
        _lblDetails.Text = "Program zistí, či je tam NTFS, FAT32 alebo RAW, a navrhne ďalší krok.";
        _lblSmart.Text = "";
        _lblSmart.Visible = false;
        _lblSmart.BackColor = Theme.Surface;
        _smartCard.SetData(null);
        _card.BackColor = Theme.Surface;
        _actions.Controls.Clear();
    }

    public void SetBusy(bool busy, string? text = null)
    {
        _busy = busy;
        foreach (Control c in _actions.Controls) c.Enabled = !busy;
        if (text is not null) _lblDetails.Text = text;
    }

    public void SetReport(QuickDiagnosisReport report)
    {
        _lblPartition.Text = report.Partition.Display + "  ·  " + report.Partition.Model;
        _lblTitle.Text = report.Title;
        _lblDetails.Text = report.Details;
        _lblSmart.Visible = true;
        _lblSmart.Text = string.IsNullOrWhiteSpace(report.SmartText)
            ? "S.M.A.R.T: nedostupné"
            : report.SmartText;
        _lblSmart.Visible = report.SmartAlert;
        _smartCard.SetData(report.SmartAvailable ? report.Smart : null);

        if (!report.SmartAvailable)
        {
            _lblSmart.BackColor = Theme.SurfaceAlt;
            _lblSmart.ForeColor = Theme.Muted;
        }
        else if (report.SmartAlert)
        {
            _lblSmart.BackColor = Theme.DangerSoft;
            _lblSmart.ForeColor = Theme.Danger;
        }
        else
        {
            _lblSmart.BackColor = Theme.SuccessSoft;
            _lblSmart.ForeColor = Theme.Success;
        }

        Color accent;
        Color soft;
        string icon;
        switch (report.Severity)
        {
            case DiagnosisSeverity.Ok:
                accent = Theme.Success;
                soft = Theme.SuccessSoft;
                icon = "✓";
                break;
            case DiagnosisSeverity.Warning:
                accent = Theme.Warning;
                soft = Theme.WarningSoft;
                icon = "!";
                break;
            case DiagnosisSeverity.Critical:
                accent = Theme.Danger;
                soft = Theme.DangerSoft;
                icon = "✕";
                break;
            default:
                accent = Theme.Accent;
                soft = Theme.AccentSoft;
                icon = "i";
                break;
        }

        _lblIcon.Text = icon;
        _lblIcon.ForeColor = accent;
        _card.BackColor = soft;
        _lblTitle.ForeColor = Theme.Text;

        _actions.Controls.Clear();
        int index = 0;
        foreach (var action in report.Actions.Distinct())
        {
            var button = index == 0 ? Theme.PrimaryButton(LabelFor(action)) : Theme.SecondaryButton(LabelFor(action));
            button.Click += (_, _) => ActionSelected?.Invoke(action);
            _actions.Controls.Add(button);
            index++;
        }

        if (_busy)
            foreach (Control c in _actions.Controls) c.Enabled = false;
    }

    private static string LabelFor(RecommendedAction action) => action switch
    {
        RecommendedAction.CopyFiles => "1. Skopírovať súbory",
        RecommendedAction.Repair => "2. Opraviť",
        RecommendedAction.DeepScan => "3. Hĺbková záchrana",
        RecommendedAction.RestoreBoot => "Obnoviť boot sector",
        RecommendedAction.RestoreFat => "Obnoviť FAT1 z FAT2",
        RecommendedAction.AssignLetter => "Priradiť písmeno",
        RecommendedAction.TryNtfs => "Skúsiť NTFS",
        RecommendedAction.TryFat32 => "Skúsiť FAT32",
        _ => action.ToString()
    };
}
