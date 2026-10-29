namespace PocketPC.Core.Devices;

/// <summary>
/// A phone that completed pairing. Contains no secrets: the phone proves it is this device
/// by presenting the TLS client certificate whose fingerprint is stored here.
/// </summary>
public sealed record PairedDevice
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Model { get; init; }

    public required string CertificateFingerprint { get; init; }

    public required DateTimeOffset PairedAt { get; init; }

    public DateTimeOffset? LastSeenAt { get; init; }

    public string? LastAddress { get; init; }

    /// <summary>The client name we registered in Sunshine, so revoking can find it again.</summary>
    public string? SunshineClientName { get; init; }

    /// <summary>Sunshine's id for this phone's streaming certificate, once Sunshine pairing finished.</summary>
    public string? SunshineClientUuid { get; init; }
}
