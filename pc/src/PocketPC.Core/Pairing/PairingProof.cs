using System.Security.Cryptography;
using System.Text;

namespace PocketPC.Core.Pairing;

/// <summary>
/// Both sides prove they know the pairing code without ever sending it.
/// Each proof is an HMAC keyed with the code over both TLS certificate fingerprints, so a
/// proof is bound to this exact phone↔PC TLS connection. A man-in-the-middle presents a
/// different certificate, which makes both proofs fail. See docs/PAIRING.md.
/// The Android implementation (com.pocketpc.core.PairingProof) must stay byte-identical.
/// </summary>
public static class PairingProof
{
    public const string Context = "pocketpc-pair-v1";

    public static string ForClient(string normalizedCode, string clientFingerprint, string serverFingerprint) =>
        Compute(normalizedCode, $"{Context}|client|{clientFingerprint}|{serverFingerprint}");

    public static string ForServer(string normalizedCode, string serverFingerprint, string clientFingerprint) =>
        Compute(normalizedCode, $"{Context}|server|{serverFingerprint}|{clientFingerprint}");

    public static bool Verify(string expectedHex, string? providedHex)
    {
        if (providedHex is null || providedHex.Length != expectedHex.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex),
            Encoding.ASCII.GetBytes(providedHex.ToLowerInvariant()));
    }

    private static string Compute(string normalizedCode, string message)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode), Encoding.UTF8.GetBytes(message));
        return Convert.ToHexStringLower(mac);
    }
}
