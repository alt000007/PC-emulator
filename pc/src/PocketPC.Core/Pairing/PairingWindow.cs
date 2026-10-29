namespace PocketPC.Core.Pairing;

public sealed record PairingSession(string Code, DateTimeOffset ExpiresAt);

public enum PairingAttemptResult
{
    Accepted,
    NotOpen,
    Expired,
    WrongCode,
    LockedOut,
}

/// <summary>
/// Pairing is only possible while the user has the "Pair new device" window open on the PC.
/// One code at a time, valid for a few minutes, single use, and closed after too many wrong guesses.
/// </summary>
public sealed class PairingWindow
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private PairingSession? _session;
    private int _failedAttempts;

    public PairingWindow(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised (on a background thread) whenever the window opens, closes or a device pairs.</summary>
    public event EventHandler? Changed;

    public PairingSession? Current
    {
        get
        {
            lock (_lock)
            {
                return _session is not null && _session.ExpiresAt > _time.GetUtcNow() ? _session : null;
            }
        }
    }

    public PairingSession Open(TimeSpan? lifetime = null)
    {
        PairingSession session;
        lock (_lock)
        {
            session = new PairingSession(PairingCode.Generate(), _time.GetUtcNow() + (lifetime ?? DefaultLifetime));
            _session = session;
            _failedAttempts = 0;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return session;
    }

    public void Close()
    {
        lock (_lock)
        {
            _session = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Checks a phone's proof. On success the code is consumed, and the server proof to send
    /// back is returned in <paramref name="serverProof"/>.
    /// </summary>
    public PairingAttemptResult TryAccept(
        string clientFingerprint,
        string serverFingerprint,
        string? clientProof,
        out string? serverProof)
    {
        serverProof = null;
        PairingAttemptResult result;

        lock (_lock)
        {
            if (_session is null)
            {
                return PairingAttemptResult.NotOpen;
            }

            if (_session.ExpiresAt <= _time.GetUtcNow())
            {
                _session = null;
                result = PairingAttemptResult.Expired;
            }
            else
            {
                var expected = PairingProof.ForClient(_session.Code, clientFingerprint, serverFingerprint);
                if (PairingProof.Verify(expected, clientProof))
                {
                    serverProof = PairingProof.ForServer(_session.Code, serverFingerprint, clientFingerprint);
                    _session = null;
                    result = PairingAttemptResult.Accepted;
                }
                else if (++_failedAttempts >= MaxFailedAttempts)
                {
                    _session = null;
                    result = PairingAttemptResult.LockedOut;
                }
                else
                {
                    return PairingAttemptResult.WrongCode;
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }
}
