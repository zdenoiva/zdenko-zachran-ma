using System.Diagnostics;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class AboutPanel : UserControl
{
    public AboutPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(18, 16, 18, 16),
            BackColor = Theme.Background
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = Theme.Background
        };

        layout.Controls.Add(Title(AppInfo.Name));
        layout.Controls.Add(Line($"Verzia {AppInfo.VersionDisplay}  ·  {AppInfo.Copyright}"));
        layout.Controls.Add(Line($"Autor: {AppInfo.Author}"));
        layout.Controls.Add(Line(AppInfo.LicenseSummary));
        layout.Controls.Add(Spacer());
        layout.Controls.Add(EnvironmentSection());

        layout.Controls.Add(Section("Ako to funguje",
            "1. Program prečíta partition table a boot sector. Zistí NTFS, FAT32 alebo RAW.\n" +
            "2. Spraví rýchlu diagnostiku: boot sector, záložný boot sector, MFT/FAT, root adresár, dirty bit a S.M.A.R.T.\n" +
            "3. Navrhne najbezpečnejší ďalší krok. Nič nemení bez potvrdenia.\n" +
            "4. Záchrana súborov je vždy len na čítanie zdrojového disku.\n" +
            "5. Oprava najprv uloží zálohu metadát, potom spustí chkdsk alebo obnoví boot/FAT. " +
            "Nakoniec overí prístup k súborom; ak ho blokuje ACL, automaticky spustí takeown + icacls.\n" +
            "6. Ak Windows volume nenamontuje (Access Denied), program ho vie odpojiť a znova " +
            "pripojiť a obnoviť ACL na koreni.\n" +
            "7. Obraz disku sa ukladá mimo zdrojového disku; nečitateľné sektory sa zapíšu do logu.\n" +
            "8. V Diagnóze vie overiť systémové súbory Windows cez DISM a opraviť ich cez " +
            "DISM RestoreHealth + SFC."));

        layout.Controls.Add(Section("Použité nástroje a autori",
            "• Windows chkdsk — oprava NTFS/FAT32 (Microsoft)\n" +
            "• PowerShell Storage reliability counters — S.M.A.R.T. (Microsoft)\n" +
            "• PowerShell Storage cmdlets — dočasné písmeno a stav volume (Microsoft)\n" +
            "• DISM a SFC — automatická obnova systémových nástrojov (Microsoft)\n" +
            "• rust-ntfs-recover (0BSD) — inšpirácia pre algoritmus hĺbkového NTFS skenu, autor mjgil-rust\n" +
            "• baize (MIT) — inšpirácia pre trojvrstvový koncept, autor karminski\n" +
            "• Microsoft FAT specification — verejná dokumentácia pre FAT32 parser\n" +
            "• Vlastný kód: Zdenko Ivančík"));

        layout.Controls.Add(Section("Nepoužité nástroje",
            "TestDisk/PhotoRec (GPL), RecuperaBit (GPL), The Sleuth Kit (CPL), " +
            "ntfs-3g (GPL), dfir_ntfs (GPL), dissect.ntfs (AGPL) a ntfstool (MIT, voliteľný) " +
            "nie sú v tejto verzii použité."));

        layout.Controls.Add(Section("Upozornenie",
            "Program pracuje s diskovými zariadeniami a pri oprave zapisuje do metadát. " +
            "Používate ho na vlastné riziko. Pred opravou si vždy skopírujte dôležité dáta. " +
            "Komerčné použitie je možné len s výslovným súhlasom autora."));

        scroll.Controls.Add(layout);
        Controls.Add(scroll);
    }

    private Control EnvironmentSection()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.Surface,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 0, 10)
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1
        };
        content.Controls.Add(new Label
        {
            Text = "Kontrola prostredia",
            Font = Theme.H3,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        });
        content.Controls.Add(new Label
        {
            Text = "Program overí Windows, PowerShell, Storage cmdlety, chkdsk, takeown a icacls. " +
                   "Ak niečo chýba, vie to sám stiahnuť a nainštalovať.",
            Font = Theme.Body,
            ForeColor = Theme.Muted,
            AutoSize = true,
            MaximumSize = new Size(850, 0),
            Margin = new Padding(0, 0, 0, 10)
        });

        var button = Theme.SecondaryButton("Skontrolovať prostredie…");
        button.Margin = new Padding(0, 0, 0, 8);
        button.Click += (_, _) =>
        {
            using var form = new PrerequisitesForm(firstRun: false);
            form.ShowDialog(FindForm());
        };
        content.Controls.Add(button);

        var updateButton = Theme.SecondaryButton("Skontrolovať aktualizácie…");
        updateButton.Margin = new Padding(0);
        updateButton.Click += async (_, _) => await CheckUpdatesAsync();
        content.Controls.Add(updateButton);

        panel.Controls.Add(content);

        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };
        return panel;
    }

    private async Task CheckUpdatesAsync()
    {
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info is null)
            {
                MessageBox.Show(
                    "Nepodarilo sa spojiť s GitHubom. Skontrolujte internet alebo skúste neskôr.",
                    "Kontrola aktualizácií", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!info.Available)
            {
                MessageBox.Show($"Máte najnovšiu verziu {AppInfo.VersionDisplay}.",
                    "Kontrola aktualizácií", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var page = string.IsNullOrWhiteSpace(info.PageUrl) ? UpdateService.PageUrl : info.PageUrl;
            var answer = MessageBox.Show(
                $"Dostupná je novšia verzia {info.Version}. Aktuálna je {AppInfo.VersionDisplay}.\n\n" +
                "Otvoriť stránku na stiahnutie?",
                "Kontrola aktualizácií", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Kontrola aktualizácií zlyhala:\n\n" + ex.Message,
                "Kontrola aktualizácií", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Label Title(string text) => new()
    {
        Text = text,
        Font = Theme.H1,
        ForeColor = Theme.Text,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 6)
    };

    private static Label Line(string text) => new()
    {
        Text = text,
        Font = Theme.Body,
        ForeColor = Theme.Muted,
        AutoSize = true,
        MaximumSize = new Size(900, 0),
        Margin = new Padding(0, 0, 0, 3)
    };

    private static Label Spacer() => new() { Text = "", Height = 14, AutoSize = false };

    private static Panel Section(string title, string text)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.Surface,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 0, 10)
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1
        };
        content.Controls.Add(new Label
        {
            Text = title,
            Font = Theme.H3,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        });
        content.Controls.Add(new Label
        {
            Text = text,
            Font = Theme.Body,
            ForeColor = Theme.Muted,
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            Margin = new Padding(0)
        });
        panel.Controls.Add(content);

        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };
        return panel;
    }
}
