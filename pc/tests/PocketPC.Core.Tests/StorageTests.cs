using PocketPC.Core.Devices;
using PocketPC.Core.Security;
using PocketPC.Core.Sunshine;

namespace PocketPC.Core.Tests;

public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pocketpc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public CompanionPaths Paths => new(Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

public class DeviceStoreTests
{
    private static PairedDevice Device(string id, string fp) => new()
    {
        Id = id,
        Name = "Phone " + id,
        CertificateFingerprint = fp,
        PairedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Persists_and_reloads()
    {
        using var dir = new TempDir();
        var store = new DeviceStore(dir.Paths.Devices);
        store.Add(Device("a1", new string('a', 64)));
        store.Add(Device("b2", new string('b', 64)));

        var reloaded = new DeviceStore(dir.Paths.Devices);
        Assert.Equal(["a1", "b2"], reloaded.All.Select(d => d.Id));
        Assert.Equal("b2", reloaded.FindByFingerprint(new string('b', 64))?.Id);
    }

    [Fact]
    public void Re_pairing_same_certificate_replaces_entry()
    {
        using var dir = new TempDir();
        var store = new DeviceStore(dir.Paths.Devices);
        store.Add(Device("old", new string('a', 64)));
        store.Add(Device("new", new string('a', 64)));
        Assert.Equal(["new"], store.All.Select(d => d.Id));
    }

    [Fact]
    public void Remove_takes_effect_immediately_and_on_disk()
    {
        using var dir = new TempDir();
        var store = new DeviceStore(dir.Paths.Devices);
        store.Add(Device("a1", new string('a', 64)));

        Assert.NotNull(store.Remove("a1"));
        Assert.Null(store.FindByFingerprint(new string('a', 64)));
        Assert.Empty(new DeviceStore(dir.Paths.Devices).All);
        Assert.Null(store.Remove("a1"));
    }

    [Fact]
    public void Update_keeps_id()
    {
        using var dir = new TempDir();
        var store = new DeviceStore(dir.Paths.Devices);
        store.Add(Device("a1", new string('a', 64)));
        var updated = store.Update("a1", d => d with { SunshineClientUuid = "uuid", Id = "ignored" });
        Assert.Equal("a1", updated?.Id);
        Assert.Equal("uuid", store.FindById("a1")?.SunshineClientUuid);
    }
}

public class SecretStorageTests
{
    [Fact]
    public void Server_certificate_survives_restart_and_is_not_plaintext()
    {
        using var dir = new TempDir();
        var protector = new EphemeralSecretProtector();
        using var first = ServerCertificate.LoadOrCreate(dir.Paths.ServerCertificate, protector, "TEST-PC");
        using var second = ServerCertificate.LoadOrCreate(dir.Paths.ServerCertificate, protector, "TEST-PC");

        Assert.True(first.HasPrivateKey);
        Assert.Equal(Fingerprint.Of(first), Fingerprint.Of(second));

        // The stored blob is encrypted, so it can't be loaded as a PKCS#12 directly.
        var stored = File.ReadAllBytes(dir.Paths.ServerCertificate);
        Assert.ThrowsAny<Exception>(() =>
            System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(stored, null));
    }

    [Fact]
    public void Sunshine_login_is_encrypted_at_rest()
    {
        using var dir = new TempDir();
        var store = new SunshineLoginStore(dir.Paths.SunshineCredentials, new EphemeralSecretProtector());
        var login = SunshineLoginStore.Generate();
        store.Save(login);

        Assert.Equal(login, store.Load());
        var raw = File.ReadAllText(dir.Paths.SunshineCredentials);
        Assert.DoesNotContain(login.Password, raw);
        Assert.DoesNotContain("pocketpc", raw);
    }

    [Fact]
    public void Unreadable_login_returns_null()
    {
        using var dir = new TempDir();
        new SunshineLoginStore(dir.Paths.SunshineCredentials, new EphemeralSecretProtector()).Save(SunshineLoginStore.Generate());

        // A different key (e.g. another Windows user) can't decrypt it.
        Assert.Null(new SunshineLoginStore(dir.Paths.SunshineCredentials, new EphemeralSecretProtector()).Load());
    }
}

public class SunshineInstallationTests
{
    [Fact]
    public void Parses_config()
    {
        var config = SunshineInstallation.ParseConfig(["# comment", "port = 48000", "  cert=  my/cert.pem ", "bogus"]);
        Assert.Equal("48000", config["port"]);
        Assert.Equal("my/cert.pem", config["cert"]);
        Assert.False(config.ContainsKey("bogus"));
    }

    [Fact]
    public void Detects_install_and_ports()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "sunshine.exe"), "");
        Directory.CreateDirectory(Path.Combine(dir.Path, "config"));

        var defaults = SunshineInstallation.Detect([Path.Combine(dir.Path, "missing"), dir.Path]);
        Assert.NotNull(defaults);
        Assert.Equal(47989, defaults.BasePort);
        Assert.Equal(47990, defaults.WebUiPort);
        Assert.Equal(Path.Combine(dir.Path, "config", "credentials", "cacert.pem"), defaults.CertificatePath);
        Assert.Null(defaults.TryReadCertificateFingerprint());

        File.WriteAllText(Path.Combine(dir.Path, "config", "sunshine.conf"), "port = 50000\n");
        Assert.Equal(50001, SunshineInstallation.Detect([dir.Path])!.WebUiPort);
    }

    [Fact]
    public void Missing_install_returns_null()
    {
        using var dir = new TempDir();
        Assert.Null(SunshineInstallation.Detect([dir.Path]));
    }
}
