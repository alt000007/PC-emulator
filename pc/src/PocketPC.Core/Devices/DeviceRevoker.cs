using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocketPC.Core.Sunshine;

namespace PocketPC.Core.Devices;

public enum RevokeOutcome
{
    /// <summary>Removed from PocketPC and from Sunshine; any running stream was ended.</summary>
    Revoked,

    /// <summary>Removed from PocketPC; Sunshine had no streaming pairing for it.</summary>
    RevokedNoSunshineClient,

    /// <summary>Removed from PocketPC, but Sunshine couldn't be reached to remove its streaming access.</summary>
    RevokedSunshineFailed,

    NotFound,
}

public sealed record RevokeResult(RevokeOutcome Outcome, string? Detail = null);

/// <summary>
/// Revoking a phone removes both of its keys at once:
/// its PocketPC certificate (blocks the companion API on the next request) and its Sunshine
/// streaming certificate (Sunshine disables it, which ends a running stream immediately).
/// </summary>
public sealed class DeviceRevoker(DeviceStore devices, ISunshineApi sunshine, ILogger? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger.Instance;

    public async Task<RevokeResult> RevokeAsync(string deviceId, CancellationToken ct = default)
    {
        // Companion access goes first and can't fail: from here on the phone is blocked.
        var removed = devices.Remove(deviceId);
        if (removed is null)
        {
            return new(RevokeOutcome.NotFound);
        }

        _log.LogInformation("Revoked device {Device} ({Name})", removed.Id, removed.Name);

        try
        {
            var uuid = removed.SunshineClientUuid;
            if (uuid is null && removed.SunshineClientName is { } name)
            {
                uuid = (await sunshine.ListClientsAsync(ct)).LastOrDefault(c => c.Name == name)?.Uuid;
            }

            if (uuid is null)
            {
                return new(RevokeOutcome.RevokedNoSunshineClient);
            }

            var disabled = await sunshine.SetClientEnabledAsync(uuid, false, ct);
            await sunshine.UnpairAsync(uuid, ct);
            if (disabled is null)
            {
                // Older Sunshine can't end a single client's session; stop the stream outright.
                await sunshine.CloseRunningAppAsync(ct);
            }

            return new(RevokeOutcome.Revoked);
        }
        catch (Exception e) when (e is SunshineApiException or HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(e, "Could not remove {Device} from Sunshine", removed.Id);
            return new(RevokeOutcome.RevokedSunshineFailed, e.Message);
        }
    }
}
