namespace PocketPC.Core.Sunshine;

public enum SunshineAccess
{
    /// <summary>Sunshine isn't installed or its service isn't running.</summary>
    NotRunning,

    /// <summary>Sunshine runs but no admin login was ever created (fresh install).</summary>
    NeedsInitialSetup,

    /// <summary>The stored login is missing or wrong.</summary>
    Unauthorized,

    Ok,
}

/// <summary>A Moonlight pairing request waiting for its PIN (Sunshine 2026.9+).</summary>
public sealed record SunshinePendingPairing(string Id, string Name, string Address);

/// <summary>A client (phone) Sunshine trusts for streaming.</summary>
public sealed record SunshineClient(string Name, string Uuid, bool Enabled);

public sealed record SunshineLogin(string Username, string Password);

/// <summary>
/// The subset of Sunshine's local admin API (https://localhost:47990/api/...) the companion uses.
/// We control Sunshine through this API instead of forking it.
/// </summary>
public interface ISunshineApi
{
    Task<SunshineAccess> CheckAccessAsync(CancellationToken ct = default);

    /// <summary>Pending pairing requests, or null if this Sunshine is too old to list them.</summary>
    Task<IReadOnlyList<SunshinePendingPairing>?> GetPendingPairingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Supplies the PIN for a pending Moonlight pairing. <paramref name="pairingId"/> is null for
    /// older Sunshine versions that only allow one pending pairing.
    /// </summary>
    Task<bool> SubmitPinAsync(string? pairingId, string pin, string clientName, CancellationToken ct = default);

    Task<IReadOnlyList<SunshineClient>> ListClientsAsync(CancellationToken ct = default);

    /// <summary>
    /// Disables a client and ends its running streams. Returns null if this Sunshine version
    /// doesn't support it.
    /// </summary>
    Task<bool?> SetClientEnabledAsync(string uuid, bool enabled, CancellationToken ct = default);

    Task<bool> UnpairAsync(string uuid, CancellationToken ct = default);

    /// <summary>Quits whatever app/game Sunshine is currently streaming.</summary>
    Task CloseRunningAppAsync(CancellationToken ct = default);

    /// <summary>Creates the very first admin login on a fresh Sunshine install.</summary>
    Task<bool> CreateInitialLoginAsync(SunshineLogin login, CancellationToken ct = default);
}
