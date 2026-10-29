using PocketPC.Core.Sunshine;

namespace PocketPC.Companion;

/// <summary>
/// Gives PocketPC Sunshine's admin login so it can approve pairings and revoke phones.
/// On a fresh Sunshine install it can create a strong random login itself.
/// </summary>
internal sealed class SunshineLoginForm : Form
{
    private readonly CompanionHost _host;
    private readonly TextBox _username = new() { Width = 260, Text = "admin" };
    private readonly TextBox _password = new() { Width = 260, UseSystemPasswordChar = true };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(460, 0) };
    private readonly Button _save = new() { Text = "Save and test", AutoSize = true };
    private readonly Button _auto = new() { Text = "Create a login for me", AutoSize = true };

    public SunshineLoginForm(CompanionHost host, SunshineAccess access)
    {
        _host = host;
        Text = "Sunshine login";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Text = access == SunshineAccess.NeedsInitialSetup
                ? "Sunshine was just installed and has no login yet. PocketPC can create a strong random one and " +
                  "remember it for you. It is stored encrypted for your Windows user only."
                : "Enter the username and password you use for Sunshine's web page. PocketPC stores them encrypted " +
                  "for your Windows user only and uses them to approve phones and revoke them.",
            Margin = new Padding(0, 0, 0, 10),
        };

        var fields = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        fields.Controls.Add(new Label { Text = "Username", AutoSize = true, Anchor = AnchorStyles.Left });
        fields.Controls.Add(_username);
        fields.Controls.Add(new Label { Text = "Password", AutoSize = true, Anchor = AnchorStyles.Left });
        fields.Controls.Add(_password);
        fields.Visible = access != SunshineAccess.NeedsInitialSetup;

        _save.Visible = access != SunshineAccess.NeedsInitialSetup;
        _auto.Visible = access == SunshineAccess.NeedsInitialSetup;
        _save.Click += async (_, _) => await SaveAsync();
        _auto.Click += async (_, _) => await CreateAutomaticallyAsync();
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = close;
        AcceptButton = access == SunshineAccess.NeedsInitialSetup ? _auto : _save;

        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([_save, _auto, close]);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.AddRange([intro, fields, buttons, _result]);
        Controls.Add(layout);
    }

    private async Task SaveAsync()
    {
        var login = new SunshineLogin(_username.Text.Trim(), _password.Text);
        if (login.Username.Length == 0 || login.Password.Length == 0)
        {
            ShowResult("Enter both username and password.", ok: false);
            return;
        }

        _save.Enabled = false;
        var previous = _host.SunshineLogins.Load();
        _host.SunshineLogins.Save(login);
        var access = await _host.Sunshine.CheckAccessAsync();
        _save.Enabled = true;

        if (access == SunshineAccess.Ok)
        {
            ShowResult("✔ Sunshine accepted the login. You're all set.", ok: true);
            return;
        }

        // Don't keep a login that doesn't work.
        if (previous is not null)
        {
            _host.SunshineLogins.Save(previous);
        }
        else
        {
            _host.SunshineLogins.Delete();
        }

        ShowResult(access == SunshineAccess.NotRunning
            ? "Sunshine isn't running, so the login couldn't be checked."
            : "Sunshine rejected this username or password.", ok: false);
    }

    private async Task CreateAutomaticallyAsync()
    {
        _auto.Enabled = false;
        var login = SunshineLoginStore.Generate();
        var created = await _host.Sunshine.CreateInitialLoginAsync(login);
        if (!created)
        {
            _auto.Enabled = true;
            ShowResult("Sunshine didn't accept the new login. Open the Sunshine web page and create one there, then enter it here.", ok: false);
            return;
        }

        _host.SunshineLogins.Save(login);
        ShowResult(
            $"✔ Done. Sunshine's web page login is:\nusername: {login.Username}\npassword: {login.Password}\n\n" +
            "You only need it if you want to change Sunshine settings yourself. It was copied to the clipboard.",
            ok: true);
        Clipboard.SetText(login.Password);
    }

    private void ShowResult(string text, bool ok)
    {
        _result.Text = text;
        _result.ForeColor = ok ? Color.SeaGreen : Color.Firebrick;
    }
}
