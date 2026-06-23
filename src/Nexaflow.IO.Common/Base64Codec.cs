using System;
using System.Text;

namespace Nexaflow.IO.Common;

/// <summary>Base64 text codec behind the editor's Encode/Decode commands. UTF-8 unless an encoding is given.</summary>
public static class Base64Codec
{
    public static string Encode(string text, Encoding? encoding = null)
        => Convert.ToBase64String((encoding ?? Encoding.UTF8).GetBytes(text));

    /// <summary>Decodes Base64 (surrounding whitespace tolerated). Returns false on malformed input instead of throwing.</summary>
    public static bool TryDecode(string base64, out string text, Encoding? encoding = null)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64.Trim());
            text = (encoding ?? Encoding.UTF8).GetString(bytes);
            return true;
        }
        catch (FormatException)
        {
            text = string.Empty;
            return false;
        }
    }
}
