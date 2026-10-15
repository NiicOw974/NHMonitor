namespace NHMonitor;

/// <summary>Fenêtre de progression du téléchargement d'une mise à jour, annulable.</summary>
public sealed class UpdateProgressForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _label;
    public CancellationTokenSource Cancellation { get; } = new();

    public UpdateProgressForm(Version version)
    {
        Text = "NHMonitor - Mise à jour";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 120);
        ShowIcon = false;
        TopMost = true;

        _label = new Label
        {
            Text = $"Téléchargement de NHMonitor {version.ToString(3)}…",
            AutoSize = false,
            Bounds = new Rectangle(16, 14, 348, 20),
        };
        _bar = new ProgressBar { Bounds = new Rectangle(16, 40, 348, 20), Minimum = 0, Maximum = 100 };
        var cancel = new Button { Text = "Annuler", Bounds = new Rectangle(274, 76, 90, 28) };
        cancel.Click += (_, _) => Cancellation.Cancel();
        CancelButton = cancel;

        Controls.AddRange([_label, _bar, cancel]);
        FormClosing += (_, _) => Cancellation.Cancel();
    }

    public void Report(int percent) => _bar.Value = Math.Clamp(percent, 0, 100);

    public void SetStatus(string text) => _label.Text = text;

    protected override void Dispose(bool disposing)
    {
        if (disposing) Cancellation.Dispose();
        base.Dispose(disposing);
    }
}
