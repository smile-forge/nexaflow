using Nexaflow.Media.Jbig2.Internal;

namespace Nexaflow.Media.Jbig2;

/// <summary>
/// The segments of a PDF's <c>JBIG2Globals</c> stream — chiefly a symbol dictionary every page of a scan shares — read
/// once and handed to each <see cref="Jbig2Decoder.Decode"/> of a stream that refers to them. What they decode to is
/// decoded the first time a page needs it and kept, so the pages of one document decode the dictionary once between
/// them; any number of decodes may share one at a time.
/// </summary>
public sealed class Jbig2Globals
{
    private Jbig2Globals(Jbig2Document document, Jbig2DecodeContext context)
    {
        Document = document;
        Context = context;
    }

    internal Jbig2Document Document { get; }

    internal Jbig2DecodeContext Context { get; }

    /// <summary>
    /// Reads the segment headers of a globals stream; what they hold is decoded when a page refers to it. The memory
    /// it takes is held to <paramref name="options"/>' limit, apart from that of the pages decoded with it.
    /// </summary>
    public static Jbig2Globals Read(ReadOnlyMemory<byte> data, Jbig2DecodeOptions? options = null)
    {
        options ??= Jbig2DecodeOptions.Default;
        var context = new Jbig2DecodeContext(options.MemoryLimit, options.WorkLimit);
        return new Jbig2Globals(new Jbig2Document(Jbig2Decoder.Reader(data), null, context), context);
    }

    /// <summary>The memory the segments have taken so far, in bytes: what a cache of them weighs.</summary>
    public long DecodedBytes => Context.Charged;

    /// <summary>What was wrong with the globals and decoded past, so far; each image decoded with them repeats it.</summary>
    public IReadOnlyList<string> Warnings => Context.Warnings;
}
