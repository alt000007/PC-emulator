using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace PocketPC.Core.Security;

/// <summary>
/// Certificate fingerprints are the identity of every PC and phone:
/// lowercase hex SHA-256 of the DER-encoded certificate.
/// </summary>
public static class Fingerprint
{
    public static string Of(X509Certificate2 certificate) => Of(certificate.RawData);

    public static string Of(byte[] derCertificate) =>
        Convert.ToHexStringLower(SHA256.HashData(derCertificate));

    /// <summary>Constant-time comparison so timing never leaks how much of a fingerprint matched.</summary>
    public static bool Matches(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(b.ToLowerInvariant()));
    }
}
