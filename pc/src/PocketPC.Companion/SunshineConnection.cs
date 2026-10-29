using PocketPC.Core.Sunshine;

namespace PocketPC.Companion;

/// <summary>
/// The current Sunshine install and API client. Re-detected on demand, so installing Sunshine
/// (or changing its port) while the companion runs only needs a "Refresh".
/// </summary>
internal sealed class SunshineConnection : ISunshineApi, IDisposable
{
    private readonly SunshineLoginStore _logins;
    private SunshineApiClient? _client;
    private int _clientPort;

    public SunshineConnection(SunshineLoginStore logins)
    {
        _logins = logins;
        Reload();
    }

    public SunshineInstallation? Installation { get; private set; }

    public string? CertificateFingerprint { get; private set; }

    public int BasePort => Installation?.BasePort ?? SunshineInstallation.DefaultBasePort;

    /// <summary>Re-detects Sunshine; the API client is only replaced if its port or certificate changed.</summary>
    public void Reload()
    {
        Installation = SunshineInstallation.Detect(SunshineInstallation.DefaultWindowsCandidates());
        var fingerprint = Installation?.TryReadCertificateFingerprint();
        var port = Installation?.WebUiPort ?? SunshineInstallation.DefaultBasePort + 1;
        if (_client is not null && port == _clientPort && fingerprint == CertificateFingerprint)
        {
            return;
        }

        CertificateFingerprint = fingerprint;
        _clientPort = port;

        // The old client is not disposed: a pairing request may still be using it (they last up to a
        // minute). This only happens when Sunshine is (re)installed, so the small leak is fine.
        _client = new SunshineApiClient(port, _logins.Load, fingerprint);
    }

    public Uri WebUi => new($"https://localhost:{Installation?.WebUiPort ?? SunshineInstallation.DefaultBasePort + 1}/");

    public Task<SunshineAccess> CheckAccessAsync(CancellationToken ct = default) => _client!.CheckAccessAsync(ct);

    public Task<IReadOnlyList<SunshinePendingPairing>?> GetPendingPairingsAsync(CancellationToken ct = default) =>
        _client!.GetPendingPairingsAsync(ct);

    public Task<bool> SubmitPinAsync(string? pairingId, string pin, string clientName, CancellationToken ct = default) =>
        _client!.SubmitPinAsync(pairingId, pin, clientName, ct);

    public Task<IReadOnlyList<SunshineClient>> ListClientsAsync(CancellationToken ct = default) => _client!.ListClientsAsync(ct);

    public Task<bool?> SetClientEnabledAsync(string uuid, bool enabled, CancellationToken ct = default) =>
        _client!.SetClientEnabledAsync(uuid, enabled, ct);

    public Task<bool> UnpairAsync(string uuid, CancellationToken ct = default) => _client!.UnpairAsync(uuid, ct);

    public Task CloseRunningAppAsync(CancellationToken ct = default) => _client!.CloseRunningAppAsync(ct);

    public Task<bool> CreateInitialLoginAsync(SunshineLogin login, CancellationToken ct = default) =>
        _client!.CreateInitialLoginAsync(login, ct);

    public void Dispose() => _client?.Dispose();
}
