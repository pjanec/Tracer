using System.Security.Cryptography;
using System.Text;

namespace Tracer.Bundle.Format;

public static class BundleNaming
{
    /// <summary>
    /// Returns a filesystem-safe version of <paramref name="input"/> by replacing
    /// every character not in [a-zA-Z0-9._-] with '_', then appending '_' and a
    /// 4-character lowercase hex hash derived from the original string to prevent
    /// collisions between distinct inputs that produce the same replaced form.
    /// </summary>
    public static string SafeFileName(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder(input.Length + 5);
        foreach (var c in input)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-')
                sb.Append(c);
            else
                sb.Append('_');
        }

        // 4-char hex hash of the original input to prevent collisions
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var suffix = Convert.ToHexString(hashBytes).ToLowerInvariant()[..4];

        sb.Append('_');
        sb.Append(suffix);

        return sb.ToString();
    }
}
