using System.IO;
using System.Security.Cryptography;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// The image codec a measurement is of: the filter that marks its images in a PDF, the prefix of the environment
/// variables that steer its runs and of the files they write, the native library it is held to, and what a kept image's
/// file is called.
/// </summary>
internal sealed record PdfImageCodec(
    string Name, string FilterName, Func<IFilter, bool> Marks, string Variables, string LibraryVariable, string LibraryFile,
    Func<byte[], string> Extension)
{
    /// <summary>JPEG 2000 (<c>JPXDecode</c>), held to libopenjp2.</summary>
    public static readonly PdfImageCodec Jpx = new("jpx", "JPXDecode", static filter => filter is JpxDecodeFilter,
        "NEXAFLOW_JPX", "NEXAFLOW_OPENJP2", "openjp2.dll", static data => JpxStreams.IsRawCodestream(data) ? ".j2k" : ".jp2");

    /// <summary>JPEG (<c>DCTDecode</c>), held to libjpeg-turbo.</summary>
    public static readonly PdfImageCodec Dct = new("dct", "DCTDecode", static filter => filter is DctDecodeFilter,
        "NEXAFLOW_DCT", "NEXAFLOW_TURBOJPEG", "turbojpeg.dll", static _ => ".jpg");

    /// <summary>JBIG2 (<c>JBIG2Decode</c>), held to jbig2dec.</summary>
    public static readonly PdfImageCodec Jbig2 = new("jbig2", "JBIG2Decode", static filter => filter is Jbig2DecodeFilter,
        "NEXAFLOW_JBIG2", "NEXAFLOW_JBIG2DEC", "jbig2.dll", static _ => ".jb2");

    /// <summary>The variable naming the folder a run writes to.</summary>
    public string FolderVariable => $"{Variables}_BENCH";
}

/// <summary>
/// One image as a PDF carries it: the bytes its codec's filter is handed, the image dictionary they came with — its
/// decode parameters resolved, as the reader hands them to a filter, so a JBIG2 image's globals stream comes with it —
/// and where in the dictionary's chain of filters the codec's stands.
/// </summary>
internal sealed record PdfImageStream(string Name, byte[] Data, DictionaryToken Dictionary, int FilterIndex);

/// <summary>
/// Where one image is and how big, as <see cref="PdfImageCorpus.Scan"/> finds it — without its bytes, which
/// <see cref="PdfImageCorpus.Load"/> reads for the images a run measures.
/// </summary>
internal sealed record PdfImageEntry(string Pdf, IndirectReference Reference, int Bytes, double DeclaredMegapixels)
{
    public string Name => $"{Path.GetFileName(Pdf)}#{Reference.ObjectNumber}";
}

/// <summary>
/// One codec's images in a set of PDFs — every image XObject whose filters include the codec's, found by walking each
/// document's objects rather than its pages, so an image drawn only from a form, an annotation or nowhere at all is
/// still measured. The same bytes met twice are kept once.
/// </summary>
/// <remarks>
/// Two passes, so a corpus of any size costs little memory: <see cref="Scan"/> keeps where each image is and how
/// big, and <see cref="Load"/> reads the bytes of the few a run measures, opening each of their PDFs once.
/// </remarks>
internal static class PdfImageCorpus
{
    public static (List<PdfImageEntry> Entries, List<string> Unread) Scan(IEnumerable<string> pdfs, PdfImageCodec codec)
    {
        var entries = new List<PdfImageEntry>();
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

                    if (stream is null || !IsImage(stream, codec, structure.FilterProvider, structure.TokenScanner, out var data, out _, out _)) continue;
                    if (!seen.Add(Convert.ToHexString(SHA256.HashData(data)))) continue;

                    entries.Add(new PdfImageEntry(path, reference, data.Length, DeclaredMegapixels(stream.StreamDictionary)));
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
    public static List<PdfImageStream> Load(IReadOnlyList<PdfImageEntry> entries, PdfImageCodec codec)
    {
        var loaded = new Dictionary<PdfImageEntry, PdfImageStream>();

        foreach (var pdf in entries.GroupBy(entry => entry.Pdf))
        {
            using var document = Open(pdf.Key);
            var structure = document.Structure;

            foreach (var entry in pdf)
            {
                var stream = (StreamToken)structure.GetObject(entry.Reference).Data;
                IsImage(stream, codec, structure.FilterProvider, structure.TokenScanner, out var data, out var dictionary, out var index);
                loaded[entry] = new PdfImageStream(entry.Name, data, dictionary, index);
            }
        }

        return [.. entries.Select(entry => loaded[entry])];
    }

    /// <summary>
    /// Every image handed to <paramref name="visit"/> as it is read, on <paramref name="threads"/> threads of the
    /// caller's own rather than the thread pool — which a managed decoder hands its own work to, so a pool thread
    /// waiting on one would starve it. Images go out in batches from one PDF, a few to a batch so a PDF of thousands
    /// spreads over every thread, and nothing is held but what is being decoded. Returns the batches that could not be read.
    /// </summary>
    public static IReadOnlyList<string> Visit(IReadOnlyList<PdfImageEntry> entries, int threads, PdfImageCodec codec,
        Action<PdfImageEntry, PdfImageStream> visit)
    {
        const int Batch = 16;
        var batches = new System.Collections.Concurrent.BlockingCollection<PdfImageEntry[]>();
        foreach (var pdf in entries.GroupBy(entry => entry.Pdf))
            foreach (var batch in pdf.Chunk(Batch)) batches.Add(batch);
        batches.CompleteAdding();

        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var workers = Enumerable.Range(0, threads).Select(at => new System.Threading.Thread(() =>
        {
            foreach (var batch in batches.GetConsumingEnumerable())
            {
                try
                {
                    using var document = Open(batch[0].Pdf);
                    var structure = document.Structure;
                    foreach (var entry in batch)
                    {
                        var stream = (StreamToken)structure.GetObject(entry.Reference).Data;
                        IsImage(stream, codec, structure.FilterProvider, structure.TokenScanner, out var data, out var dictionary, out var index);
                        visit(entry, new PdfImageStream(entry.Name, data, dictionary, index));
                    }
                }
                catch (Exception failure) { failures.Enqueue($"{batch[0].Pdf}: {failure.GetType().Name}: {failure.Message}"); }
            }
        }) { IsBackground = true, Name = $"{codec.Name} visit {at}" }).ToList();

        workers.ForEach(worker => worker.Start());
        workers.ForEach(worker => worker.Join());
        return [.. failures];
    }

    private static PdfDocument Open(string path) =>
        PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });

    /// <summary>The size the image dictionary declares, before anything is decoded — what a sample is chosen by.</summary>
    private static double DeclaredMegapixels(DictionaryToken dictionary) =>
        dictionary.TryGet(NameToken.Width, out NumericToken width) && dictionary.TryGet(NameToken.Height, out NumericToken height)
            ? width.Double * height.Double / 1_000_000
            : 0;

    /// <summary>
    /// An image whose filters include the codec's, with <paramref name="data"/> the bytes that filter is handed: any
    /// filter before it in the chain is applied first, as a reader would. <paramref name="dictionary"/> is the image's,
    /// its decode parameters resolved, and <paramref name="index"/> the codec's place in its chain.
    /// </summary>
    private static bool IsImage(StreamToken stream, PdfImageCodec codec, ILookupFilterProvider provider,
        UglyToad.PdfPig.Tokenization.Scanner.IPdfTokenScanner scanner, out byte[] data, out DictionaryToken dictionary, out int index)
    {
        data = [];
        dictionary = stream.StreamDictionary;
        index = -1;
        if (!dictionary.TryGet(NameToken.Subtype, out NameToken subtype) || subtype != NameToken.Image) return false;

        var filters = provider.GetFilters(dictionary, scanner);
        for (var at = 0; at < filters.Count && index < 0; at++)
            if (codec.Marks(filters[at])) index = at;
        if (index < 0) return false;

        if (dictionary.TryGet(NameToken.DecodeParms, out var parameters))
            dictionary = dictionary.With(NameToken.DecodeParms, Resolved(parameters, scanner, depth: 0));

        var bytes = stream.Data;
        for (var at = 0; at < index; at++) bytes = filters[at].Decode(bytes, dictionary, provider, at);

        data = bytes.ToArray();
        return data.Length > 0;
    }

    /// <summary>
    /// Decode parameters with what they hold by reference read in: a dictionary of them, or an array of one per filter,
    /// and the objects those name — a JBIG2 image's globals stream among them.
    /// </summary>
    private static IToken Resolved(IToken token, UglyToad.PdfPig.Tokenization.Scanner.IPdfTokenScanner scanner, int depth)
    {
        if (token is IndirectReferenceToken reference) token = scanner.Get(reference.Data)?.Data ?? token;
        if (depth >= 2) return token;
        return token switch
        {
            DictionaryToken dictionary => new DictionaryToken(dictionary.Data.ToDictionary(
                entry => NameToken.Create(entry.Key), entry => Resolved(entry.Value, scanner, depth + 1))),
            ArrayToken array => new ArrayToken([.. array.Data.Select(item => Resolved(item, scanner, depth + 1))]),
            _ => token,
        };
    }
}
