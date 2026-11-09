namespace NtfsRescue.Ui;

public sealed class ConfirmForm : Form
{
    private readonly CheckBox _check;
    private readonly TextBox _text;
    private readonly Button _ok;
    private readonly string _required;

    public ConfirmForm(string title, string message, string requiredText, string checkText)
    {
        _required = requiredText;

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = SystemColors.Window;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(620, 120);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var lbl = new Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Size(570, 0),
            Margin = new Padding(0, 0, 0, 14)
        };

        _check = new CheckBox
        {
            Text = checkText,
            AutoSize = true,
            MaximumSize = new Size(570, 0),
            Margin = new Padding(0, 0, 0, 12)
        };

        var textRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 14)
        };
        textRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        textRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var lblText = new Label
        {
            Text = $"Napíšte \"{requiredText}\" pre potvrdenie:",
            AutoSize = true,
            Margin = new Padding(0, 6, 10, 0)
        };
        _text = new TextBox { Width = 240, Margin = new Padding(0, 2, 0, 0) };
        textRow.Controls.Add(lblText, 0, 0);
        textRow.Controls.Add(_text, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0)
        };

        var cancel = new Button { Text = "Zrušiť", AutoSize = true, DialogResult = DialogResult.Cancel };
        _ok = new Button { Text = "Pokračovať", AutoSize = true, Enabled = false };
        _ok.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_ok);

        _check.CheckedChanged += (_, _) => ValidateInput();
        _text.TextChanged += (_, _) => ValidateInput();

        layout.Controls.Add(lbl, 0, 0);
        layout.Controls.Add(_check, 0, 1);
        layout.Controls.Add(textRow, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);

        CancelButton = cancel;
        AcceptButton = _ok;
    }

    private void ValidateInput()
    {
        _ok.Enabled = _check.Checked &&
                      string.Equals(_text.Text.Trim(), _required, StringComparison.OrdinalIgnoreCase);
    }
}
