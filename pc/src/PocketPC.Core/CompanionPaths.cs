namespace PocketPC.Core;

/// <summary>Where the companion keeps its files (default: %LOCALAPPDATA%\PocketPC).</summary>
public sealed class CompanionPaths(string root)
{
    public static CompanionPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketPC"));

    public string Root { get; } = root;

    public string Settings => Path.Combine(Root, "settings.json");

    public string Devices => Path.Combine(Root, "devices.json");

    /// <summary>Encrypted (DPAPI) PKCS#12 with the companion's TLS key.</summary>
    public string ServerCertificate => Path.Combine(Root, "server-identity.bin");

    /// <summary>Encrypted (DPAPI) Sunshine admin login.</summary>
    public string SunshineCredentials => Path.Combine(Root, "sunshine-login.bin");

    public string Log => Path.Combine(Root, "companion.log");
}
