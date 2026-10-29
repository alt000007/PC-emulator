using PocketPC.Core.Devices;
using PocketPC.Core.Net;
using PocketPC.Core.Sunshine;

namespace PocketPC.Companion;

/// <summary>Status, paired phones (with Revoke) and the pairing button. Closing hides it to the tray.</summary>
internal sealed class MainForm : Form
{
    private readonly CompanionHost _host;
    private readonly Label _connectionStatus = new() { AutoSize = true };
    private readonly Label _sunshineStatus = new() { AutoSize = true };
    private readonly Button _sunshineAction = new() { AutoSize = true, Visible = false };
    private readonly ListView _devices = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Dock = DockStyle.Fill,
    };

    private readonly Button _revoke = new() { Text = "Revoke selected phone", AutoSize = true, Enabled = false };
    private readonly CheckBox _autoStart = new() { Text = "Start PocketPC with Windows", AutoSize = true };
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 10_000 };
    private bool _allowClose;
    private SunshineAccess? _lastAccess;

    public MainForm(CompanionHost host)
    {
        _host = host;
        Text = "PocketPC";
        Icon = AppIcon.Get();
        MinimumSize = new Size(640, 460);
        Size = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(12);

        var header = new Label
        {
            Text = "PocketPC companion",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        var pair = new Button { Text = "Pair new phone…", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        pair.Click += (_, _) => StartPairing();
        var openSunshine = new Button { Text = "Open Sunshine web page", AutoSize = true };
        openSunshine.Click += (_, _) => TrayContext.OpenUrl(_host.Sunshine.WebUi.ToString());
        _sunshineAction.Click += (_, _) => OnSunshineAction();

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.AddRange([pair, openSunshine]);

        var sunshineRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        sunshineRow.Controls.AddRange([_sunshineStatus, _sunshineAction]);
        _sunshineStatus.Margin = new Padding(3, 8, 3, 3);

        _devices.Columns.Add("Phone", 180);
        _devices.Columns.Add("Model", 140);
        _devices.Columns.Add("Paired", 110);
        _devices.Columns.Add("Last seen", 130);
        _devices.Columns.Add("Streaming", 90);
        _devices.SelectedIndexChanged += (_, _) => _revoke.Enabled = _devices.SelectedItems.Count == 1;
        _revoke.Click += async (_, _) => await RevokeSelectedAsync();

        var devicesLabel = new Label { Text = "Paired phones", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 12, 0, 4) };

        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        bottom.Controls.AddRange([_revoke, _autoStart]);
        _autoStart.Margin = new Padding(24, 8, 3, 3);
        _autoStart.CheckedChanged += (_, _) => OnAutoStartChanged();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header);
        layout.Controls.Add(_connectionStatus);
        layout.Controls.Add(sunshineRow);
        layout.Controls.Add(actions);
        layout.Controls.Add(devicesLabel);
        layout.Controls.Add(_devices);
        layout.Controls.Add(bottom);
        Controls.Add(layout);

        _host.Devices.Changed += OnDevicesChanged;
        _refreshTimer.Tick += async (_, _) => await RefreshSunshineAsync();
        _refreshTimer.Start();

        RefreshAutoStart();
        RefreshConnection();
        RefreshDevices();
        _ = RefreshSunshineAsync();
    }

    public void AllowClose() => _allowClose = true;

    /// <summary>Syncs the checkbox with the registry (OnAutoStartChanged ignores no-op changes).</summary>
    public void RefreshAutoStart() => _autoStart.Checked = AutoStart.IsEnabled;

    public void StartPairing()
    {
        if (_lastAccess is not SunshineAccess.Ok)
        {
            var answer = MessageBox.Show(
                this,
                "Sunshine isn't ready yet, so the phone can pair with PocketPC but not start streaming.\n\nPair anyway?",
                "PocketPC",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        using var dialog = new PairingForm(_host);
        dialog.ShowDialog(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            // Keep running in the tray; phones still need the companion.
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.Devices.Changed -= OnDevicesChanged;
            _refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            try
            {
                BeginInvoke(RefreshDevices);
            }
            catch (InvalidOperationException)
            {
                // Window is being destroyed.
            }
        }
    }

    private void OnAutoStartChanged()
    {
        if (_autoStart.Checked == AutoStart.IsEnabled)
        {
            return;
        }

        AutoStart.Set(_autoStart.Checked);
        _host.Settings.StartWithWindows = _autoStart.Checked;
        _host.SaveSettings();
    }

    private void RefreshConnection()
    {
        var hosts = HostAddresses.Discover();
        var where = hosts.Count == 0 ? "no network found" : string.Join(", ", hosts.Select(h => HostAddresses.IsTailnet(System.Net.IPAddress.Parse(h)) ? h + " (Tailscale)" : h));
        _connectionStatus.Text = $"Phones connect to: {where}  ·  port {_host.Server.BoundPort} (home network / Tailscale only)";
    }

    private void RefreshDevices()
    {
        _devices.BeginUpdate();
        _devices.Items.Clear();
        foreach (var d in _host.Devices.All)
        {
            var item = new ListViewItem(d.Name) { Tag = d.Id };
            item.SubItems.Add(d.Model ?? "");
            item.SubItems.Add(d.PairedAt.ToLocalTime().ToString("g"));
            item.SubItems.Add(d.LastSeenAt?.ToLocalTime().ToString("g") ?? "never");
            item.SubItems.Add(d.SunshineClientUuid is not null ? "✔ paired" : "not yet");
            _devices.Items.Add(item);
        }

        _devices.EndUpdate();
        _revoke.Enabled = _devices.SelectedItems.Count == 1;
    }

    private async Task RefreshSunshineAsync()
    {
        _host.Sunshine.Reload();
        if (_host.Sunshine.Installation is null)
        {
            ShowSunshine(null, "Sunshine: ✖ not installed. Run scripts\\install-pc.ps1 (or install Sunshine from lizardbyte.dev).", "Check again");
            return;
        }

        var access = await _host.Sunshine.CheckAccessAsync();
        switch (access)
        {
            case SunshineAccess.Ok:
                ShowSunshine(access, "Sunshine: ✔ ready for streaming", null);
                break;
            case SunshineAccess.NotRunning:
                ShowSunshine(access, "Sunshine: ✖ installed but not running (start the \"SunshineService\" service or reboot).", "Check again");
                break;
            case SunshineAccess.NeedsInitialSetup:
                ShowSunshine(access, "Sunshine: first-time setup needed.", "Set up automatically…");
                break;
            default:
                ShowSunshine(access, "Sunshine: PocketPC needs your Sunshine login to approve phones.", "Enter Sunshine login…");
                break;
        }
    }

    private void ShowSunshine(SunshineAccess? access, string text, string? action)
    {
        _lastAccess = access;
        _sunshineStatus.Text = text;
        _sunshineStatus.ForeColor = access == SunshineAccess.Ok ? Color.SeaGreen : Color.DarkOrange;
        _sunshineAction.Visible = action is not null;
        _sunshineAction.Text = action ?? "";
    }

    private async void OnSunshineAction()
    {
        if (_lastAccess is SunshineAccess.NeedsInitialSetup or SunshineAccess.Unauthorized)
        {
            using var dialog = new SunshineLoginForm(_host, _lastAccess.Value);
            dialog.ShowDialog(this);
        }

        await RefreshSunshineAsync();
    }

    private async Task RevokeSelectedAsync()
    {
        if (_devices.SelectedItems.Count != 1 || _devices.SelectedItems[0].Tag is not string id)
        {
            return;
        }

        var name = _devices.SelectedItems[0].Text;
        var confirm = MessageBox.Show(
            this,
            $"Revoke \"{name}\"?\n\nIt is disconnected right away (including a running stream) and has to pair again to come back.",
            "Revoke phone",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        _revoke.Enabled = false;
        var result = await _host.Revoker.RevokeAsync(id);
        var message = result.Outcome switch
        {
            RevokeOutcome.Revoked => $"\"{name}\" was revoked and disconnected.",
            RevokeOutcome.RevokedNoSunshineClient => $"\"{name}\" was revoked. (It had no streaming pairing in Sunshine.)",
            RevokeOutcome.RevokedSunshineFailed =>
                $"\"{name}\" can no longer use PocketPC, but Sunshine couldn't be reached to remove its streaming access.\n\n" +
                "Remove it manually in the Sunshine web page (Clients).",
            _ => $"\"{name}\" was already removed.",
        };
        MessageBox.Show(this, message, "Revoke phone", MessageBoxButtons.OK,
            result.Outcome == RevokeOutcome.RevokedSunshineFailed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }
}
