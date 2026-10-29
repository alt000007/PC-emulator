using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocketPC.Core.Devices;

namespace PocketPC.Core.Sunshine;

public sealed record SunshinePairResult(bool Paired, string? Error = null);

/// <summary>
/// One-tap streaming pairing. The phone starts a normal Moonlight pairing with a random PIN and
/// sends the same PIN to the companion over the authenticated channel; the companion finds the
/// pending request coming from that phone's address and enters the PIN in Sunshine for you.
/// </summary>
public sealed class SunshinePairing(
    ISunshineApi sunshine,
    DeviceStore devices,
    ILogger? logger = null,
    TimeProvider? time = null,
    TimeSpan? waitForRequest = null)
{
    /// <summary>How long to wait for Moonlight's pairing request to show up in Sunshine.</summary>
    public static readonly TimeSpan DefaultWaitForRequest = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    private readonly ILogger _log = logger ?? NullLogger.Instance;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TimeSpan _waitForRequest = waitForRequest ?? DefaultWaitForRequest;

    public static bool IsValidPin(string? pin) => pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    /// <summary>Sunshine shows this name in its client list; it includes a short id so names never collide.</summary>
    public static string ClientNameFor(PairedDevice device)
    {
        var name = $"PocketPC {device.Name} ({device.Id[..6]})";
        while (Encoding.UTF8.GetByteCount(name) > 128)
        {
            name = name[..^1];
        }

        return name;
    }

    public async Task<SunshinePairResult> PairAsync(PairedDevice device, string phoneAddress, string pin, CancellationToken ct)
    {
        if (!IsValidPin(pin))
        {
            return new(false, "invalid_pin");
        }

        var clientName = ClientNameFor(device);
        var deadline = _time.GetUtcNow() + _waitForRequest;

        try
        {
            while (true)
            {
                var pending = await sunshine.GetPendingPairingsAsync(ct);
                bool accepted;

                if (pending is null)
                {
                    // Older Sunshine: a single pending pairing, no per-request id.
                    accepted = await sunshine.SubmitPinAsync(null, pin, clientName, ct);
                    if (!accepted && _time.GetUtcNow() < deadline)
                    {
                        await Task.Delay(PollInterval, _time, ct);
                        continue;
                    }
                }
                else
                {
                    var fromPhone = pending.Where(p => p.Address == phoneAddress).ToList();
                    if (fromPhone.Count == 0)
                    {
                        if (_time.GetUtcNow() >= deadline)
                        {
                            return new(false, "no_pairing_request");
                        }

                        await Task.Delay(PollInterval, _time, ct);
                        continue;
                    }

                    if (fromPhone.Count > 1)
                    {
                        _log.LogWarning("Several Sunshine pairing requests from {Address}; refusing to guess", phoneAddress);
                        return new(false, "ambiguous_pairing_request");
                    }

                    accepted = await sunshine.SubmitPinAsync(fromPhone[0].Id, pin, clientName, ct);
                }

                if (!accepted)
                {
                    return new(false, "sunshine_rejected");
                }

                break;
            }

            var uuid = (await sunshine.ListClientsAsync(ct)).LastOrDefault(c => c.Name == clientName)?.Uuid;
            devices.Update(device.Id, d => d with { SunshineClientName = clientName, SunshineClientUuid = uuid });
            _log.LogInformation("Sunshine paired {Device} as {ClientName}", device.Id, clientName);
            return new(true);
        }
        catch (SunshineApiException e) when (e.Status is System.Net.HttpStatusCode.Unauthorized or (>= System.Net.HttpStatusCode.MultipleChoices and < System.Net.HttpStatusCode.BadRequest))
        {
            _log.LogWarning("Sunshine rejected the stored login; set it up in the tray app");
            return new(false, "sunshine_login_needed");
        }
        catch (Exception e) when (e is SunshineApiException or HttpRequestException)
        {
            _log.LogWarning(e, "Sunshine pairing failed for {Device}", device.Id);
            return new(false, "sunshine_unavailable");
        }
    }
}
