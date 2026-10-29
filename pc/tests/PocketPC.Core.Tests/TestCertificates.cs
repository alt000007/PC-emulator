using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PocketPC.Core.Security;

namespace PocketPC.Core.Tests;

internal static class TestCertificates
{
    /// <summary>A phone-like identity: self-signed P-256 client certificate.</summary>
    public static X509Certificate2 CreateClient(string name = "Test Phone")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false));
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        using var ephemeral = request.CreateSelfSigned(now, now.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
    }

    /// <summary>An HttpClient that behaves like the phone app: pins the PC fingerprint, presents its certificate.</summary>
    public static HttpClient PhoneClient(int port, string pinnedServerFingerprint, X509Certificate2? clientCertificate)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                    cert is not null && Fingerprint.Matches(Fingerprint.Of(cert.GetRawCertData()), pinnedServerFingerprint),
                ClientCertificates = clientCertificate is null ? null : new X509CertificateCollection { clientCertificate },
                LocalCertificateSelectionCallback = clientCertificate is null ? null : (_, _, _, _, _) => clientCertificate,
            },
        };

        return new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{port}/") };
    }

    public static async Task<T> ReadJson<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>())!;
}
