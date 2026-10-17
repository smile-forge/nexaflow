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
}
