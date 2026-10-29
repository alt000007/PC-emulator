namespace PocketPC.Core.Server;

// JSON contracts of the phone ↔ companion API (camelCase on the wire). See docs/PAIRING.md.

public sealed record HelloResponse(int ApiVersion, string PcId, string PcName, bool PairingOpen);

public sealed record PairRequest(string? DeviceName, string? DeviceModel, string? Proof);

public sealed record PairResponse(
    string DeviceId,
    string PcId,
    string PcName,
    string ServerProof,
    SunshineInfo Sunshine);

public sealed record SunshineInfo(bool Installed, int HttpPort, string? CertificateFingerprint);

public sealed record SunshinePairRequest(string? Pin);

public sealed record SunshinePairResponse(bool Paired, string? Error);

public sealed record StatusResponse(string PcId, string PcName, string DeviceId, string DeviceName, SunshineInfo Sunshine);

public sealed record ErrorResponse(string Error);
