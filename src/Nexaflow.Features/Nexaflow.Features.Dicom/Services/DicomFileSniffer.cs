using System.IO;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// Recognises DICOM Part-10 files. CDs routinely store instances with no extension (<c>IM_0001</c>), so
/// extension alone is unreliable — the authoritative test is the <c>DICM</c> marker at byte offset 128
/// (after the 128-byte preamble).
/// </summary>
internal static class DicomFileSniffer
{
    private static readonly string[] KnownExtensions = [".dcm", ".dicom"];

    public static bool HasDicomExtension(string path)
        => KnownExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>True if the file carries the <c>DICM</c> magic at offset 128.</summary>
    public static bool HasDicmMagic(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 132) return false;
            Span<byte> buf = stackalloc byte[4];
            fs.Seek(128, SeekOrigin.Begin);
            if (fs.Read(buf) < 4) return false;
            return buf[0] == (byte)'D' && buf[1] == (byte)'I' && buf[2] == (byte)'C' && buf[3] == (byte)'M';
        }
        catch { return false; }
    }

    /// <summary>Cheap-first recognition: trust a known extension, else sniff the magic.</summary>
    public static bool IsDicom(string path)
        => HasDicomExtension(path) || HasDicmMagic(path);
}
