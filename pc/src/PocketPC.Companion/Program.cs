using PocketPC.Core;

namespace PocketPC.Companion;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // One companion per Windows user; a second launch just shows the running one's window.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\PocketPC.Companion", out var isFirstInstance);
        if (!isFirstInstance)
        {
            SingleInstance.SignalShowWindow();
            return;
        }

        ApplicationConfiguration.Initialize();

        CompanionHost host;
        try
        {
            host = new CompanionHost(CompanionPaths.Default);
            host.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            MessageBox.Show(
                $"PocketPC could not start:\n\n{e.Message}\n\nIf another program uses port {CompanionSettings.DefaultPort}, close it and try again.",
                "PocketPC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var startHidden = args.Contains("--background");
        using var context = new TrayContext(host, showWindow: !startHidden);
        Application.Run(context);
        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>Lets a second launch ask the first instance to show its window.</summary>
internal static class SingleInstance
{
    private const string EventName = @"Local\PocketPC.Companion.Show";

    public static void SignalShowWindow()
    {
        if (EventWaitHandle.TryOpenExisting(EventName, out var handle))
        {
            handle.Set();
            handle.Dispose();
        }
    }

    public static EventWaitHandle CreateShowWindowEvent() => new(false, EventResetMode.AutoReset, EventName);
}
