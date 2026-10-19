using System.IO;
using System.Security.Cryptography;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// One JPEG 2000 image as a PDF carries it: the bytes <c>JPXDecode</c> is handed, and the image dictionary
/// they came with, which decides whether an embedded palette is applied or left to the PDF's own
/// <c>/ColorSpace</c>, as <c>PdfJpxFilter</c> decides it.
/// </summary>
internal sealed record JpxStream(string Name, byte[] Data, DictionaryToken Dictionary)
{
    /// <summary>The PDF declares its own colour space, so the palette in the JPX data is not applied.</summary>
    public bool IgnorePalette => Dictionary.ContainsKey(NameToken.ColorSpace);

    /// <summary>A bare codestream (SOC marker first) rather than a JP2 file of boxes.</summary>
    public bool IsRawCodestream => Data.Length >= 4 && Data[0] == 0xFF && Data[1] == 0x4F && Data[2] == 0xFF && Data[3] == 0x51;
}

/// <summary>
/// Where one JPEG 2000 image is and how big, as <see cref="JpxCorpus.Scan"/> finds it — without its bytes, which
/// <see cref="JpxCorpus.Load"/> reads for the images a run measures.
/// </summary>
internal sealed record JpxEntry(string Pdf, IndirectReference Reference, int Bytes, double DeclaredMegapixels)
{
    public string Name => $"{Path.GetFileName(Pdf)}#{Reference.ObjectNumber}";
}

/// <summary>One decoded component, as either decoder hands it back before any colour work.</summary>
internal sealed record JpxPlane(int Width, int Height, int Precision, bool Signed, int[] Samples);

/// <summary>Every component of one decoded image.</summary>
internal sealed record JpxPlanes(IReadOnlyList<JpxPlane> Planes)
{
    /// <summary>
    /// How these samples stand beside another decoder's: <c>identical</c>, or the first thing that differs.
    /// </summary>
    public string Against(JpxPlanes other)
    {
        if (Planes.Count != other.Planes.Count) return $"components {Planes.Count} vs {other.Planes.Count}";

        for (var at = 0; at < Planes.Count; at++)
        {
            var (mine, theirs) = (Planes[at], other.Planes[at]);
            if (mine.Width != theirs.Width || mine.Height != theirs.Height)
                return $"component {at} is {mine.Width}x{mine.Height} vs {theirs.Width}x{theirs.Height}";
            if (mine.Precision != theirs.Precision || mine.Signed != theirs.Signed)
                return $"component {at} precision {mine.Precision}{(mine.Signed ? "s" : "u")} vs {theirs.Precision}{(theirs.Signed ? "s" : "u")}";
            if (mine.Samples.Length < mine.Width * mine.Height || theirs.Samples.Length < theirs.Width * theirs.Height)
                return $"component {at} holds {mine.Samples.Length} vs {theirs.Samples.Length} samples for {mine.Width}x{mine.Height}";

            var differing = 0;
            var furthest = 0L;
            var count = mine.Width * mine.Height;
            for (var sample = 0; sample < count; sample++)
            {
                var gap = Math.Abs((long)mine.Samples[sample] - theirs.Samples[sample]);
                if (gap == 0) continue;
                differing++;
                furthest = Math.Max(furthest, gap);
            }

            if (differing > 0) return $"component {at}: {differing} of {count} samples differ, by up to {furthest}";
        }

        return "identical";
    }
}

/// <summary>
/// The JPEG 2000 images in a set of PDFs — every image XObject whose filters include <c>JPXDecode</c>, found by
/// walking each document's objects rather than its pages, so an image drawn only from a form, an annotation or
/// nowhere at all is still measured. The same bytes met twice are kept once.
/// </summary>
/// <remarks>
/// Two passes, so a corpus of any size costs little memory: <see cref="Scan"/> keeps where each image is and how
/// big, and <see cref="Load"/> reads the bytes of the few a run measures, opening each of their PDFs once.
/// </remarks>
internal static class JpxCorpus
{
    public static (List<JpxEntry> Entries, List<string> Unread) Scan(IEnumerable<string> pdfs)
    {
        var entries = new List<JpxEntry>();
        var unread = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in pdfs)
        {
            try
            {
                using var document = Open(path);
                var structure = document.Structure;

                foreach (var reference in structure.CrossReferenceTable.ObjectOffsets.Keys.OrderBy(r => r.ObjectNumber))
                {
                    StreamToken? stream;
                    try { stream = structure.GetObject(reference).Data as StreamToken; }
                    catch (Exception) { continue; }   // a damaged object is the document's problem, not the bench's

                    if (stream is null || !IsJpxImage(stream, structure.FilterProvider, structure.TokenScanner, out var data)) continue;
                    if (!seen.Add(Convert.ToHexString(SHA256.HashData(data)))) continue;

                    entries.Add(new JpxEntry(path, reference, data.Length, DeclaredMegapixels(stream.StreamDictionary)));
                }
            }
            catch (Exception failure)
            {
                unread.Add($"{path}: {failure.GetType().Name}: {failure.Message}");
            }
        }

        return (entries, unread);
    }

    /// <summary>The images' bytes and dictionaries, in the order given.</summary>
    public static List<JpxStream> Load(IReadOnlyList<JpxEntry> entries)
    {
        var loaded = new Dictionary<JpxEntry, JpxStream>();

        foreach (var pdf in entries.GroupBy(entry => entry.Pdf))
        {
            using var document = Open(pdf.Key);
            var structure = document.Structure;

            foreach (var entry in pdf)
            {
                var stream = (StreamToken)structure.GetObject(entry.Reference).Data;
                IsJpxImage(stream, structure.FilterProvider, structure.TokenScanner, out var data);
                loaded[entry] = new JpxStream(entry.Name, data, stream.StreamDictionary);
            }
        }

        return [.. entries.Select(entry => loaded[entry])];
    }

    private static PdfDocument Open(string path) =>
        PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });

    /// <summary>The size the image dictionary declares, before anything is decoded — what a sample is chosen by.</summary>
    private static double DeclaredMegapixels(DictionaryToken dictionary) =>
        dictionary.TryGet(NameToken.Width, out NumericToken width) && dictionary.TryGet(NameToken.Height, out NumericToken height)
            ? width.Double * height.Double / 1_000_000
            : 0;

    /// <summary>
    /// An image whose filters include <c>JPXDecode</c>, with <paramref name="data"/> the bytes that filter is
    /// handed: any filter before it in the chain is applied first, as a reader would.
    /// </summary>
    private static bool IsJpxImage(StreamToken stream, ILookupFilterProvider provider, UglyToad.PdfPig.Tokenization.Scanner.IPdfTokenScanner scanner, out byte[] data)
    {
        data = [];
        var dictionary = stream.StreamDictionary;
        if (!dictionary.TryGet(NameToken.Subtype, out NameToken subtype) || subtype != NameToken.Image) return false;

        var filters = provider.GetFilters(dictionary, scanner);
        var jpx = -1;
        for (var at = 0; at < filters.Count && jpx < 0; at++)
            if (filters[at] is JpxDecodeFilter) jpx = at;
        if (jpx < 0) return false;

        var bytes = stream.Data;
        for (var at = 0; at < jpx; at++) bytes = filters[at].Decode(bytes, dictionary, provider, at);

        data = bytes.ToArray();
        return data.Length > 0;
    }
}
