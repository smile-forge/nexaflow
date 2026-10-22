namespace Nexaflow.IO.Usb.Descriptors;

/// <summary>Bytes as spaced hex pairs — "12 01 00 02" — the one spelling every USB surface uses.</summary>
public static class UsbHex
{
    public static string Format(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return "";
        return string.Create(data.Length * 3 - 1, data.ToArray(), static (span, bytes) =>
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) span[i * 3 - 1] = ' ';
                bytes[i].TryFormat(span[(i * 3)..], out _, "X2");
            }
        });
    }

    /// <summary>
    /// Bytes typed in hex, however they are spaced or prefixed — "02 01 FF", "0x02,0x01", "0201ff". A token of odd length
    /// is read with a leading zero, so "1 2" is 01 02. False when <paramref name="text"/> holds no digits or anything
    /// that is not one.
    /// </summary>
    public static bool TryParse(string text, out byte[] bytes)
    {
        var digits = new System.Text.StringBuilder();
        foreach (var token in text.Split([' ', ',', ';', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
            digits.Append(t.Length % 2 == 1 ? "0" + t : t);
        }
        var s = digits.ToString();
        if (s.Length == 0 || !s.All(Uri.IsHexDigit))
        {
            bytes = [];
            return false;
        }
        bytes = Convert.FromHexString(s);
        return true;
    }
}
