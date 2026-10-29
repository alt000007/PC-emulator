using PocketPC.Core.Sunshine;

namespace PocketPC.TestHost;

/// <summary>
/// Stands in for Sunshine. Every <see cref="SubmitPinAsync"/> for a pending request "pairs" a
/// client; tests can queue pending requests and inspect what the companion did.
/// </summary>
public sealed class SimulatedSunshine : ISunshineApi
{
    private readonly object _lock = new();
    private readonly List<SunshinePendingPairing> _pending = [];
    private readonly List<SunshineClient> _clients = [];

    /// <summary>
    /// Legacy mode only: pretend a Moonlight pairing request is always waiting, so any PIN the
    /// companion submits pairs a client. The headless host uses this.
    /// </summary>
    public bool AutoCreatePendingRequests { get; set; }

    public SunshineAccess Access { get; set; } = SunshineAccess.Ok;

    public bool SupportsPendingList { get; set; } = true;

    public bool SupportsClientUpdate { get; set; } = true;

    public int CloseRunningAppCalls { get; private set; }

    public List<string> SubmittedPins { get; } = [];

    public IReadOnlyList<SunshineClient> Clients
    {
        get
        {
            lock (_lock)
            {
                return _clients.ToList();
            }
        }
    }

    public void AddPendingRequest(string address)
    {
        lock (_lock)
        {
            _pending.Add(new SunshinePendingPairing(Guid.NewGuid().ToString("N"), "roth", address));
        }
    }

    public Task<SunshineAccess> CheckAccessAsync(CancellationToken ct = default) => Task.FromResult(Access);

    public Task<IReadOnlyList<SunshinePendingPairing>?> GetPendingPairingsAsync(CancellationToken ct = default)
    {
        if (!SupportsPendingList)
        {
            return Task.FromResult<IReadOnlyList<SunshinePendingPairing>?>(null);
        }

        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<SunshinePendingPairing>?>(_pending.ToList());
        }
    }

    public Task<bool> SubmitPinAsync(string? pairingId, string pin, string clientName, CancellationToken ct = default)
    {
        lock (_lock)
        {
            SubmittedPins.Add(pin);
            if (pairingId is null)
            {
                if (!SupportsPendingList && (_pending.Count > 0 || AutoCreatePendingRequests))
                {
                    _pending.Clear();
                    _clients.Add(new SunshineClient(clientName, Guid.NewGuid().ToString(), true));
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }

            var match = _pending.FirstOrDefault(p => p.Id == pairingId);
            if (match is null)
            {
                return Task.FromResult(false);
            }

            _pending.Remove(match);
            _clients.Add(new SunshineClient(clientName, Guid.NewGuid().ToString(), true));
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<SunshineClient>> ListClientsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SunshineClient>>(Clients);

    public Task<bool?> SetClientEnabledAsync(string uuid, bool enabled, CancellationToken ct = default)
    {
        if (!SupportsClientUpdate)
        {
            return Task.FromResult<bool?>(null);
        }

        lock (_lock)
        {
            var index = _clients.FindIndex(c => c.Uuid == uuid);
            if (index < 0)
            {
                return Task.FromResult<bool?>(false);
            }

            _clients[index] = _clients[index] with { Enabled = enabled };
            return Task.FromResult<bool?>(true);
        }
    }

    public Task<bool> UnpairAsync(string uuid, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_clients.RemoveAll(c => c.Uuid == uuid) > 0);
        }
    }

    public Task CloseRunningAppAsync(CancellationToken ct = default)
    {
        CloseRunningAppCalls++;
        return Task.CompletedTask;
    }

    public Task<bool> CreateInitialLoginAsync(SunshineLogin login, CancellationToken ct = default)
    {
        Access = SunshineAccess.Ok;
        return Task.FromResult(true);
    }
}
