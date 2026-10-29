using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocketPC.Core.Devices;
using PocketPC.Core.Pairing;
using PocketPC.Core.Security;
using PocketPC.Core.Sunshine;

namespace PocketPC.Core.Server;

public sealed class CompanionServerOptions
{
    public required X509Certificate2 Certificate { get; init; }

    public required string PcId { get; init; }

    public required Func<string> PcName { get; init; }

    /// <summary>0 picks a free port (tests).</summary>
    public int Port { get; init; } = CompanionSettings.DefaultPort;

    /// <summary>Snapshot of what the phone should know about Sunshine.</summary>
    public required Func<SunshineInfo> Sunshine { get; init; }
}

/// <summary>
/// The phone-facing HTTPS API. Every connection must present a TLS client certificate;
/// only <c>/v1/hello</c> and <c>/v1/pair</c> accept certificates we don't know yet.
/// </summary>
public sealed class CompanionServer : IAsyncDisposable
{
    public const int ApiVersion = 1;

    private const string DeviceItemKey = "pocketpc.device";

    private readonly CompanionServerOptions _options;
    private readonly DeviceStore _devices;
    private readonly PairingWindow _pairing;
    private readonly SunshinePairing _sunshinePairing;
    private readonly DeviceRevoker _revoker;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly string _serverFingerprint;
    private WebApplication? _app;

    public CompanionServer(
        CompanionServerOptions options,
        DeviceStore devices,
        PairingWindow pairing,
        SunshinePairing sunshinePairing,
        DeviceRevoker revoker,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? time = null)
    {
        _options = options;
        _devices = devices;
        _pairing = pairing;
        _sunshinePairing = sunshinePairing;
        _revoker = revoker;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _log = _loggerFactory.CreateLogger<CompanionServer>();
        _time = time ?? TimeProvider.System;
        _serverFingerprint = Fingerprint.Of(options.Certificate);
    }

    public string ServerFingerprint => _serverFingerprint;

    /// <summary>The port actually bound (useful when <see cref="CompanionServerOptions.Port"/> is 0).</summary>
    public int BoundPort { get; private set; }

    public event EventHandler<PairedDevice>? DevicePaired;

    public async Task StartAsync(CancellationToken ct = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_loggerFactory);

        // This server is embedded in an app (tray app, test host); it must not grab Ctrl+C/SIGTERM.
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = 64 * 1024;
            kestrel.Limits.MaxConcurrentConnections = 64;
            kestrel.ListenAnyIP(_options.Port, listen =>
            {
                // Runs on the raw TCP connection, before TLS: public addresses never get a handshake.
                listen.Use(next => connection =>
                {
                    var remote = (connection.RemoteEndPoint as IPEndPoint)?.Address;
                    if (!NetworkPolicy.IsAllowed(remote))
                    {
                        _log.LogWarning("Dropped connection from non-private address {Address}", remote);
                        connection.Abort();
                        return Task.CompletedTask;
                    }

                    return next(connection);
                });

                listen.UseHttps(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = _options.Certificate,
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateMode = ClientCertificateMode.RequireCertificate,

                    // Phones use self-signed certificates; we authorise them by fingerprint per request.
                    ClientCertificateValidation = (_, _, _) => true,
                });
            });
        });

        var app = builder.Build();
        MapEndpoints(app);
        await app.StartAsync(ct);

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        BoundPort = new Uri(address.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost")).Port;
        _app = app;
        _log.LogInformation("Companion API listening on port {Port}", BoundPort);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/v1/hello", () =>
            Results.Ok(new HelloResponse(ApiVersion, _options.PcId, _options.PcName(), _pairing.Current is not null)));

        app.MapPost("/v1/pair", (PairRequest request, HttpContext http) => Pair(request, http));

        var device = app.MapGroup("/v1").AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var paired = Authenticate(http);
            if (paired is null)
            {
                return Results.Json(new ErrorResponse("not_paired"), statusCode: StatusCodes.Status403Forbidden);
            }

            http.Items[DeviceItemKey] = paired;
            return await next(context);
        });

        device.MapGet("/status", (HttpContext http) =>
        {
            var d = (PairedDevice)http.Items[DeviceItemKey]!;
            return Results.Ok(new StatusResponse(_options.PcId, _options.PcName(), d.Id, d.Name, _options.Sunshine()));
        });

        device.MapPost("/sunshine/pair", async (SunshinePairRequest request, HttpContext http) =>
        {
            var d = (PairedDevice)http.Items[DeviceItemKey]!;
            var address = RemoteAddress(http);
            if (address is null)
            {
                return Results.Json(new SunshinePairResponse(false, "no_address"), statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await _sunshinePairing.PairAsync(d, address, request.Pin ?? "", http.RequestAborted);
            return Results.Json(
                new SunshinePairResponse(result.Paired, result.Error),
                statusCode: result.Paired ? StatusCodes.Status200OK : StatusCodes.Status502BadGateway);
        });

        // "Forget this PC" on the phone also removes the phone from the PC.
        device.MapPost("/unpair", async (HttpContext http) =>
        {
            var d = (PairedDevice)http.Items[DeviceItemKey]!;
            await _revoker.RevokeAsync(d.Id, http.RequestAborted);
            return Results.NoContent();
        });
    }

    private IResult Pair(PairRequest request, HttpContext http)
    {
        var clientCertificate = http.Connection.ClientCertificate;
        if (clientCertificate is null)
        {
            return Results.Json(new ErrorResponse("client_certificate_required"), statusCode: StatusCodes.Status400BadRequest);
        }

        var name = CleanName(request.DeviceName);
        if (name is null)
        {
            return Results.Json(new ErrorResponse("invalid_device_name"), statusCode: StatusCodes.Status400BadRequest);
        }

        var clientFingerprint = Fingerprint.Of(clientCertificate);
        var outcome = _pairing.TryAccept(clientFingerprint, _serverFingerprint, request.Proof, out var serverProof);
        _log.LogInformation("Pairing attempt from {Address} ({Name}): {Outcome}", RemoteAddress(http), name, outcome);

        switch (outcome)
        {
            case PairingAttemptResult.Accepted:
                var device = new PairedDevice
                {
                    Id = DeviceStore.NewDeviceId(),
                    Name = name,
                    Model = CleanName(request.DeviceModel),
                    CertificateFingerprint = clientFingerprint,
                    PairedAt = _time.GetUtcNow(),
                    LastSeenAt = _time.GetUtcNow(),
                    LastAddress = RemoteAddress(http),
                };
                _devices.Add(device);
                DevicePaired?.Invoke(this, device);
                return Results.Ok(new PairResponse(device.Id, _options.PcId, _options.PcName(), serverProof!, _options.Sunshine()));
            case PairingAttemptResult.WrongCode:
                return Results.Json(new ErrorResponse("wrong_code"), statusCode: StatusCodes.Status401Unauthorized);
            case PairingAttemptResult.LockedOut:
                return Results.Json(new ErrorResponse("too_many_attempts"), statusCode: StatusCodes.Status429TooManyRequests);
            case PairingAttemptResult.Expired:
                return Results.Json(new ErrorResponse("pairing_expired"), statusCode: StatusCodes.Status410Gone);
            default:
                return Results.Json(new ErrorResponse("pairing_closed"), statusCode: StatusCodes.Status409Conflict);
        }
    }

    private PairedDevice? Authenticate(HttpContext http)
    {
        var certificate = http.Connection.ClientCertificate;
        if (certificate is null)
        {
            return null;
        }

        var device = _devices.FindByFingerprint(Fingerprint.Of(certificate));
        if (device is not null)
        {
            _devices.Touch(device.Id, RemoteAddress(http), _time.GetUtcNow());
        }

        return device;
    }

    private static string? RemoteAddress(HttpContext http) =>
        http.Connection.RemoteIpAddress is { } ip ? NetworkPolicy.ToComparableString(ip) : null;

    private static string? CleanName(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var cleaned = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }
}

/// <summary>Host lifetime that leaves process signals to the embedding application.</summary>
internal sealed class EmbeddedHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
