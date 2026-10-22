using System.Drawing;

namespace Bunker;

/// <summary>What happens when the X in the corner of the main window is clicked.</summary>
public static class CloseButtonSetting
{
    public const string Ask = "ask";
    public const string Minimise = "minimise";
    public const string Close = "close";

    /// <summary>Anything unrecognised (an old or hand-edited settings file) means "ask".</summary>
    public static string Normalise(string? value) => value switch
    {
        Minimise => Minimise,
        Close => Close,
        _ => Ask,
    };
}

public enum CloseChoice { Cancel, Minimise, Close }

/// <summary>Asks whether to minimise Bunker or close it when the X is clicked, with a tick box to remember the answer.</summary>
public class CloseChoiceForm : Form
{
    private readonly CheckBox _remember;

    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;
    public bool Remember => _remember.Checked;

    public CloseChoiceForm(bool toTray = false)
    {
        Theme.Apply(this);
        Text = "Close Bunker?";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var minimise = Theme.PrimaryButton("Minimise");
        var close = Theme.GhostButton("Close Bunker");
        var cancel = Theme.GhostButton("Cancel");
        _remember = new CheckBox { Text = "Remember my choice", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Margin = new Padding(0, 14, 0, 0) };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 22, 28, 16) };
        flow.Controls.Add(new Label
        {
            Text = (toTray ? "Minimise keeps Bunker running in the system tray" : "Minimise keeps Bunker running in the taskbar") + ", so it stays connected and your schedules and chat watch-words keep working.\n\nClosing disconnects from your server and stops those until you open Bunker again.",
            ForeColor = Theme.Text, AutoSize = true, MaximumSize = new Size(444, 0), Margin = new Padding(0, 0, 0, 0),
        });
        flow.Controls.Add(_remember);
        flow.Controls.Add(new Label { Text = "You can change this later in Settings → General.", ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 4, 0, 0) });

        var bar = new FlowLayoutPanel { Width = 444, Height = 48, Margin = new Padding(0, 16, 0, 0), BackColor = Theme.Bg };
        minimise.Margin = new Padding(0, 0, 8, 0);
        close.Margin = new Padding(0, 0, 8, 0);
        bar.Controls.Add(minimise);
        bar.Controls.Add(close);
        bar.Controls.Add(cancel);
        flow.Controls.Add(bar);

        Controls.Add(flow);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        AcceptButton = minimise;
        CancelButton = cancel;
        ClientSize = new Size(500, 280);

        minimise.Click += (_, _) => Finish(CloseChoice.Minimise);
        close.Click += (_, _) => Finish(CloseChoice.Close);
        cancel.Margin = new Padding(0);
        cancel.Click += (_, _) => Finish(CloseChoice.Cancel);
    }

    private void Finish(CloseChoice choice)
    {
        Choice = choice;
        DialogResult = choice == CloseChoice.Cancel ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }
}
