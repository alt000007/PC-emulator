using Microsoft.Extensions.Logging;
using PocketPC.Core;
using PocketPC.Core.Devices;
using PocketPC.Core.Net;
using PocketPC.Core.Pairing;
using PocketPC.Core.Security;
using PocketPC.Core.Server;
using PocketPC.Core.Sunshine;
using PocketPC.TestHost;

// Usage: PocketPC.TestHost [--port 47880] [--uri-file path] [--host 127.0.0.1]
// Prints "PAIRING_URI=pocketpc://pair?..." and keeps a pairing window open (re-opened after each pairing).

var port = IntArg("--port") ?? CompanionSettings.DefaultPort;
var uriFile = StringArg("--uri-file");
var hostOverride = StringArg("--host");

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
var log = loggerFactory.CreateLogger("TestHost");

var dataDir = Path.Combine(Path.GetTempPath(), "pocketpc-testhost-" + Environment.ProcessId);
var paths = new CompanionPaths(dataDir);
var protector = new EphemeralSecretProtector();
using var certificate = ServerCertificate.LoadOrCreate(paths.ServerCertificate, protector, Environment.MachineName);
var settings = CompanionSettings.LoadOrCreate(paths.Settings);
var devices = new DeviceStore(paths.Devices);

var sunshine = new SimulatedSunshine { SupportsPendingList = false, AutoCreatePendingRequests = true };
var pairing = new PairingWindow();
var server = new CompanionServer(
    new CompanionServerOptions
    {
        Certificate = certificate,
        PcId = settings.PcId,
        PcName = () => "TestHost " + Environment.MachineName,
        Port = port,
        Sunshine = () => new SunshineInfo(true, SunshineInstallation.DefaultBasePort, null),
    },
    devices,
    pairing,
    new SunshinePairing(sunshine, devices, loggerFactory.CreateLogger<SunshinePairing>()),
    new DeviceRevoker(devices, sunshine, loggerFactory.CreateLogger<DeviceRevoker>()),
    loggerFactory);

await server.StartAsync();

void Announce()
{
    var session = pairing.Open(TimeSpan.FromHours(1));
    var hosts = hostOverride is not null ? [hostOverride] : HostAddresses.Discover().DefaultIfEmpty("127.0.0.1").ToList();
    var uri = new PairingUri(settings.PcId, "TestHost", hosts, server.BoundPort, server.ServerFingerprint, session.Code).ToString();
    Console.WriteLine($"PAIRING_CODE={PairingCode.Format(session.Code)}");
    Console.WriteLine($"PAIRING_URI={uri}");
    if (uriFile is not null)
    {
        File.WriteAllText(uriFile, uri);
    }
}

server.DevicePaired += (_, device) =>
{
    log.LogInformation("Paired {Name} ({Id}); opening a new pairing window", device.Name, device.Id);
    Announce();
};

Announce();

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
await stop.Task;
await server.DisposeAsync();
Directory.Delete(dataDir, recursive: true);

string? StringArg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

int? IntArg(string name) => StringArg(name) is { } s && int.TryParse(s, out var v) ? v : null;
