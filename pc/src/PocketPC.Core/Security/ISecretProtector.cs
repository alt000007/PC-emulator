using System.Security.Cryptography;

namespace PocketPC.Core.Security;

/// <summary>
/// Encrypts secrets before they touch the disk. On Windows the companion uses DPAPI
/// (bound to the signed-in Windows user), so nothing secret is ever stored in plain text.
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] protectedData);
}

/// <summary>
/// AES-GCM with a random key that only lives in memory. Secrets written with it are
/// unreadable after the process exits. Used by tests and the headless test host only.
/// </summary>
public sealed class EphemeralSecretProtector : ISecretProtector
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public byte[] Protect(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        var nonce = protectedData.AsSpan(0, nonceSize);
        var tag = protectedData.AsSpan(nonceSize, tagSize);
        var ciphertext = protectedData.AsSpan(nonceSize + tagSize);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, tagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
