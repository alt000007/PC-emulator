using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using PocketPC.Core;
using PocketPC.Core.Devices;
using PocketPC.Core.Logging;
using PocketPC.Core.Net;
using PocketPC.Core.Pairing;
using PocketPC.Core.Security;
using PocketPC.Core.Server;
using PocketPC.Core.Sunshine;

namespace PocketPC.Companion;

/// <summary>Owns everything that runs in the background: settings, devices, Sunshine and the phone API.</summary>
internal sealed class CompanionHost : IAsyncDisposable
{
    private readonly X509Certificate2 _certificate;

    public CompanionHost(CompanionPaths paths)
    {
        Paths = paths;
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b =>
            b.AddProvider(new FileLoggerProvider(paths.Log)).SetMinimumLevel(LogLevel.Information));
        Log = LoggerFactory.CreateLogger("Companion");

        var protector = new DpapiSecretProtector();
        Settings = CompanionSettings.LoadOrCreate(paths.Settings);
        Devices = new DeviceStore(paths.Devices);
        SunshineLogins = new SunshineLoginStore(paths.SunshineCredentials, protector);
        Sunshine = new SunshineConnection(SunshineLogins);
        Pairing = new PairingWindow();
        Revoker = new DeviceRevoker(Devices, Sunshine, LoggerFactory.CreateLogger<DeviceRevoker>());
        _certificate = ServerCertificate.LoadOrCreate(paths.ServerCertificate, protector, Environment.MachineName);

        Server = new CompanionServer(
            new CompanionServerOptions
            {
                Certificate = _certificate,
                PcId = Settings.PcId,
                PcName = () => Environment.MachineName,
                Port = Settings.Port,
                Sunshine = () => new SunshineInfo(Sunshine.Installation is not null, Sunshine.BasePort, Sunshine.CertificateFingerprint),
            },
            Devices,
            Pairing,
            new SunshinePairing(Sunshine, Devices, LoggerFactory.CreateLogger<SunshinePairing>()),
            Revoker,
            LoggerFactory);
    }

    public CompanionPaths Paths { get; }

    public ILoggerFactory LoggerFactory { get; }

    public ILogger Log { get; }

    public CompanionSettings Settings { get; }

    public DeviceStore Devices { get; }

    public SunshineLoginStore SunshineLogins { get; }

    public SunshineConnection Sunshine { get; }

    public PairingWindow Pairing { get; }

    public DeviceRevoker Revoker { get; }

    public CompanionServer Server { get; }

    public Task StartAsync() => Server.StartAsync();

    public void SaveSettings() => Settings.Save(Paths.Settings);

    /// <summary>Opens a pairing window and builds the QR link for it.</summary>
    public (PairingSession Session, PairingUri Uri) StartPairing()
    {
        var session = Pairing.Open();
        var hosts = HostAddresses.Discover();
        var uri = new PairingUri(
            Settings.PcId,
            Environment.MachineName,
            hosts.Count > 0 ? hosts : [Environment.MachineName],
            Server.BoundPort,
            Server.ServerFingerprint,
            session.Code);
        return (session, uri);
    }

    public async ValueTask DisposeAsync()
    {
        await Server.DisposeAsync();
        Sunshine.Dispose();
        _certificate.Dispose();
        LoggerFactory.Dispose();
    }
}
