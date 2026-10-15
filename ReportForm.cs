namespace NHMonitor;

/// <summary>
/// Affiche le rapport des capteurs dans une fenêtre de l'application.
/// On n'ouvre volontairement pas un fichier .txt avec le programme associé : l'application tournant
/// en administrateur, ce programme (configurable par n'importe quel processus de l'utilisateur via
/// HKCU) serait lui aussi lancé en administrateur.
/// </summary>
public sealed class ReportForm : Form
{
    public ReportForm(string report)
    {
        Text = "NHMonitor - Rapport des capteurs";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(900, 650);
        MinimumSize = new Size(400, 300);
        ShowIcon = false;

        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f),
            Text = report.ReplaceLineEndings("\r\n"),
        };

        var copy = new Button { Text = "Copier dans le presse-papiers", AutoSize = true };
        copy.Click += (_, _) => Clipboard.SetText(text.Text);
        var close = new Button { Text = "Fermer", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = close;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(6),
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);

        Controls.Add(text);
        Controls.Add(buttons);
        Shown += (_, _) => text.Select(0, 0);
    }
}
