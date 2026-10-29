namespace PocketPC.Core;

/// <summary>Write-to-temp-then-rename so a crash or power cut never leaves a half-written settings file.</summary>
internal static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    public static void WriteAllText(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
