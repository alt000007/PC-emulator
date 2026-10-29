using System.Diagnostics;

namespace PocketPC.Companion;

/// <summary>The tray icon and its menu. The app lives here; windows come and go.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly CompanionHost _host;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _startWithWindows;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showWait;
    private readonly SynchronizationContext _ui;
    private MainForm? _main;

    public TrayContext(CompanionHost host, bool showWindow)
    {
        _host = host;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _startWithWindows = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutoStart())
        {
            Checked = AutoStart.IsEnabled,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open PocketPC", null, (_, _) => ShowMain());
        menu.Items.Add("Pair new phone…", null, (_, _) => ShowMain().StartPairing());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Sunshine web page", null, (_, _) => OpenUrl(_host.Sunshine.WebUi.ToString()));
        menu.Items.Add(_startWithWindows);
        menu.Items.Add("Open log folder", null, (_, _) => OpenUrl(_host.Paths.Root));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit PocketPC", null, (_, _) => Quit());
        menu.Opening += (_, _) => _startWithWindows.Checked = AutoStart.IsEnabled;

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "PocketPC",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowMain();

        _host.Server.DevicePaired += (_, device) => _ui.Post(_ =>
            _tray.ShowBalloonTip(4000, "PocketPC", $"Paired with {device.Name}.", ToolTipIcon.Info), null);

        _showEvent = SingleInstance.CreateShowWindowEvent();
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => _ui.Post(_ => ShowMain(), null), null, -1, false);

        if (showWindow)
        {
            ShowMain();
        }
    }

    public static void OpenUrl(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showWait.Unregister(null);
            _showEvent.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _main?.Dispose();
        }

        base.Dispose(disposing);
    }

    private MainForm ShowMain()
    {
        if (_main is null || _main.IsDisposed)
        {
            _main = new MainForm(_host);
        }

        _main.Show();
        if (_main.WindowState == FormWindowState.Minimized)
        {
            _main.WindowState = FormWindowState.Normal;
        }

        _main.Activate();
        return _main;
    }

    private void ToggleAutoStart()
    {
        var enable = !AutoStart.IsEnabled;
        AutoStart.Set(enable);
        _host.Settings.StartWithWindows = enable;
        _host.SaveSettings();
        _startWithWindows.Checked = enable;
        _main?.RefreshAutoStart();
    }

    private void Quit()
    {
        _main?.AllowClose();
        _main?.Close();
        ExitThread();
    }
}
