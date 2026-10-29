using System.Security.Cryptography;
using System.Text.Json;
using PocketPC.Core.Security;

namespace PocketPC.Core.Sunshine;

/// <summary>Keeps the Sunshine admin login encrypted on disk (DPAPI on Windows).</summary>
public sealed class SunshineLoginStore(string path, ISecretProtector protector)
{
    public bool HasLogin => File.Exists(path);

    public SunshineLogin? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SunshineLogin>(protector.Unprotect(File.ReadAllBytes(path)));
        }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    public void Save(SunshineLogin login)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllBytes(path, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(login)));
    }

    public void Delete()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>A strong random login for when the companion sets Sunshine up itself.</summary>
    public static SunshineLogin Generate() =>
        new("pocketpc", Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', 'x').Replace('/', 'y'));
}
