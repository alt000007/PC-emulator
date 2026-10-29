using System.Security.Cryptography;
using System.Text;
using PocketPC.Core.Security;

namespace PocketPC.Companion;

/// <summary>
/// Windows DPAPI, scoped to the current Windows user: other users on the PC (and copies of the
/// files on another machine) can't decrypt the companion's key or the Sunshine login.
/// </summary>
internal sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PocketPC companion v1");

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData) =>
        ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}
