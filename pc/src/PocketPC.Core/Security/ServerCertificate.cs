using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PocketPC.Core.Security;

/// <summary>
/// The companion's long-lived TLS identity. Phones pin its fingerprint at pairing time,
/// so it must survive restarts: it is stored as a PKCS#12 blob encrypted by <see cref="ISecretProtector"/>.
/// </summary>
public static class ServerCertificate
{
    public static X509Certificate2 LoadOrCreate(string path, ISecretProtector protector, string machineName)
    {
        if (File.Exists(path))
        {
            var pfx = protector.Unprotect(File.ReadAllBytes(path));
            return Load(pfx);
        }

        var created = Create(machineName);
        var bytes = created.Export(X509ContentType.Pkcs12);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllBytes(path, protector.Protect(bytes));
        return Load(bytes);
    }

    public static X509Certificate2 Create(string machineName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=PocketPC Companion ({machineName})", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], false)); // serverAuth
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(SanitizeDnsName(machineName));
        request.CertificateExtensions.Add(san.Build());

        var now = DateTimeOffset.UtcNow.AddDays(-1);
        return request.CreateSelfSigned(now, now.AddYears(20));
    }

    /// <summary>
    /// Re-import through PKCS#12 so the private key is backed by a key container:
    /// Windows SChannel refuses ephemeral keys for TLS servers.
    /// </summary>
    private static X509Certificate2 Load(byte[] pfx) =>
        X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);

    private static string SanitizeDnsName(string name)
    {
        var chars = name.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray();
        return chars.Length == 0 ? "pocketpc" : new string(chars).ToLowerInvariant();
    }
}
