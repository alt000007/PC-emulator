using System.Security.Cryptography;
using System.Text;

namespace PocketPC.Core.Pairing;

/// <summary>
/// One-time pairing codes: 12 Crockford base32 characters (60 bits), shown as XXXX-XXXX-XXXX.
/// The same code is embedded in the QR, so typing it is only the fallback.
/// </summary>
public static class PairingCode
{
    public const int Length = 12;

    // Crockford base32: no I, L, O, U so codes are easy to read aloud and type.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>XXXXXXXXXXXX → XXXX-XXXX-XXXX for display.</summary>
    public static string Format(string code) =>
        $"{code[..4]}-{code[4..8]}-{code[8..12]}";

    /// <summary>
    /// Accepts what a human might type: lower case, dashes, spaces, and the usual
    /// look-alikes (O→0, I/L→1). Returns null if it can't be a valid code.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var sb = new StringBuilder(Length);
        foreach (var raw in input)
        {
            if (raw is '-' or ' ' or '\t')
            {
                continue;
            }

            var c = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            };

            if (Alphabet.IndexOf(c) < 0)
            {
                return null;
            }

            sb.Append(c);
        }

        return sb.Length == Length ? sb.ToString() : null;
    }
}
