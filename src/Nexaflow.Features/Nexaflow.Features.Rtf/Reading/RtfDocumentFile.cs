using System.IO;
using System.Text;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Rtf.Reading;

/// <summary>
/// A rich-text file as the text the RTF reader takes: each byte one character, as Latin-1 maps them.
/// <para>
/// RTF is written in seven-bit characters, with anything past them escaped. A byte past seven bits that a writer left in
/// anyway is a character of whichever code page the document names at that point, and only the reader knows which that
/// is — so bytes reach it as bytes, never guessed at by a text encoding on the way in.
/// </para>
/// </summary>
internal static class RtfDocumentFile
{
    /// <summary>How every RTF document opens: its outermost group, and the control word naming the format.</summary>
    private const string Opening = @"{\rtf";

    private const int Chunk = 81920;

    /// <summary>
    /// The document at <paramref name="path"/>, read no further than <paramref name="maxBytes"/> — through the VFS, so one
    /// inside an archive opens like one on disk, and with the file shared, so one open in a word processor still reads.
    /// </summary>
    public static string Read(string path, long maxBytes = long.MaxValue)
    {
        using var stream = VirtualFileSystem.Instance.OpenRead(path);
        using var bytes = new MemoryStream();

        var buffer = new byte[Chunk];
        var left = maxBytes;
        int read;

        while (left > 0 && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left))) > 0)
        {
            bytes.Write(buffer, 0, read);
            left -= read;
        }

        return Encoding.Latin1.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }

    /// <summary>Whether <paramref name="source"/> is an RTF document — whether it opens as one, which is what every reader goes by.</summary>
    public static bool IsRtf(string source) => source.StartsWith(Opening, StringComparison.Ordinal);
}
