using System.Security.Cryptography;
using System.Text.Json;

namespace PocketPC.Core;

/// <summary>Non-secret companion settings. Saved automatically on every change.</summary>
public sealed class CompanionSettings
{
    public const int DefaultPort = 47880;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Stable random id so phones recognise this PC even if its name or IP changes.</summary>
    public string PcId { get; set; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public int Port { get; set; } = DefaultPort;

    public bool StartWithWindows { get; set; }

    public static CompanionSettings LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var loaded = JsonSerializer.Deserialize<CompanionSettings>(File.ReadAllText(path), JsonOptions);
            if (loaded is not null)
            {
                return loaded;
            }
        }

        var created = new CompanionSettings();
        created.Save(path);
        return created;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
