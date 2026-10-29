namespace PocketPC.Core.Sunshine;

/// <summary>Finds a local Sunshine install and reads the few config values the companion needs.</summary>
public sealed class SunshineInstallation
{
    public const int DefaultBasePort = 47989;

    private SunshineInstallation(string installDirectory, IReadOnlyDictionary<string, string> config)
    {
        InstallDirectory = installDirectory;
        Config = config;
    }

    public string InstallDirectory { get; }

    public IReadOnlyDictionary<string, string> Config { get; }

    public string ConfigDirectory => Path.Combine(InstallDirectory, "config");

    /// <summary>Moonlight's HTTP port. Everything else is an offset from it.</summary>
    public int BasePort => Config.TryGetValue("port", out var p) && int.TryParse(p, out var port) ? port : DefaultBasePort;

    public int WebUiPort => BasePort + 1;

    public string CertificatePath =>
        Config.TryGetValue("cert", out var cert) && cert.Length > 0
            ? Path.GetFullPath(cert, ConfigDirectory)
            : Path.Combine(ConfigDirectory, "credentials", "cacert.pem");

    /// <summary>
    /// Locates Sunshine. Returns null if it isn't installed.
    /// </summary>
    /// <param name="candidates">Install folders to try, most likely first.</param>
    public static SunshineInstallation? Detect(IEnumerable<string> candidates)
    {
        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "sunshine.exe")) || File.Exists(Path.Combine(dir, "sunshine")))
            {
                var configFile = Path.Combine(dir, "config", "sunshine.conf");
                var config = File.Exists(configFile) ? ParseConfig(File.ReadAllLines(configFile)) : new Dictionary<string, string>();
                return new SunshineInstallation(dir, config);
            }
        }

        return null;
    }

    public static IEnumerable<string> DefaultWindowsCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (programFiles.Length > 0)
        {
            yield return Path.Combine(programFiles, "Sunshine");
        }

        yield return @"C:\Program Files\Sunshine";
    }

    /// <summary>Reads Sunshine's PEM certificate and returns its fingerprint, or null if unreadable.</summary>
    public string? TryReadCertificateFingerprint()
    {
        try
        {
            using var cert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(
                File.ReadAllText(CertificatePath));
            return Security.Fingerprint.Of(cert);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>sunshine.conf is "key = value" lines with # comments.</summary>
    public static Dictionary<string, string> ParseConfig(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        return result;
    }
}
