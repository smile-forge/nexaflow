using System.Runtime.InteropServices;
using System.Text;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// A model's multimodal projector (its <c>mmproj</c> GGUF): what turns a picture or a recording into the embeddings the
/// model reads in its place. A prompt holds one <see cref="Marker"/> where each goes; the projector splits it there into
/// text and media, and reads each medium into the context.
/// </summary>
internal sealed unsafe class MediaProjector : IDisposable
{
    private IntPtr _context;

    private MediaProjector(IntPtr context)
    {
        _context = context;
        Marker = Marshal.PtrToStringUTF8((IntPtr)MtmdNative.DefaultMarker())!;
        SeesImages = MtmdNative.SupportsVision(context);
        HearsAudio = MtmdNative.SupportsAudio(context);
    }

    /// <summary>What a prompt holds where a medium is read.</summary>
    public string Marker { get; }

    public bool SeesImages { get; }

    public bool HearsAudio { get; }

    /// <exception cref="HostRequestException">The projector would not load.</exception>
    public static MediaProjector Load(string path, LlamaModel model, IntPtr? device)
    {
        var parameters = MtmdNative.ContextDefaults();
        parameters.UseGpu = (byte)(device is null ? 0 : 1);
        parameters.Device = device ?? IntPtr.Zero;
        parameters.FlashAttention = LlamaNative.FlashAttention.Auto;
        IntPtr context = MtmdNative.Init(path, model.Handle, parameters);
        return context == IntPtr.Zero
            ? throw new HostRequestException(HostErrorKind.Runtime, $"llama.cpp could not load the projector {path}; see the host's log.")
            : new MediaProjector(context);
    }

    /// <summary>The prompt split at its markers, each read in turn: <paramref name="media"/> are the files — an image
    /// llama.cpp decodes, a WAV — in the order their markers come.</summary>
    /// <exception cref="HostRequestException">A medium cannot be read, or the markers and the media do not agree.</exception>
    public PreparedPrompt Prepare(string text, IReadOnlyList<byte[]> media)
    {
        var bitmaps = new IntPtr[media.Count];
        IntPtr chunks = IntPtr.Zero;
        try
        {
            for (int i = 0; i < media.Count; i++)
                fixed (byte* bytes = media[i])
                {
                    bitmaps[i] = MtmdNative.BitmapFromBuffer(Handle, bytes, (nuint)media[i].Length, placeholder: false, MtmdNative.HelperDefaults()).Bitmap;
                    if (bitmaps[i] == IntPtr.Zero)
                        throw new HostRequestException(HostErrorKind.Invalid, $"Medium {i + 1} of the prompt is not an image or a recording llama.cpp reads.");
                }

            chunks = MtmdNative.ChunksInit();
            var utf8 = Encoding.UTF8.GetBytes(text + "\0");
            fixed (byte* bytes = utf8)
            fixed (IntPtr* images = bitmaps)
            {
                var input = new MtmdNative.InputText { Text = bytes, Length = (nuint)(utf8.Length - 1), AddSpecial = 0, ParseSpecial = 1 };
                int result = MtmdNative.Tokenize(Handle, chunks, &input, images, (nuint)bitmaps.Length);
                if (result != 0)
                    throw new HostRequestException(HostErrorKind.Invalid, result == 1
                        ? $"The prompt's markers and its {media.Count} media do not agree."
                        : $"llama.cpp could not read the prompt's media ({result}).");
            }

            var pieces = new List<PromptPiece>();
            for (nuint i = 0, n = MtmdNative.ChunksSize(chunks); i < n; i++)
            {
                IntPtr chunk = MtmdNative.ChunksGet(chunks, i);
                if (MtmdNative.ChunkGetType(chunk) == MtmdNative.ChunkType.Text)
                {
                    nuint count;
                    int* tokens = MtmdNative.ChunkTextTokens(chunk, &count);
                    pieces.Add(new PromptPiece(new ReadOnlySpan<int>(tokens, (int)count).ToArray(), IntPtr.Zero, (int)count, (int)count));
                }
                else
                {
                    pieces.Add(new PromptPiece(null, chunk, (int)MtmdNative.ChunkTokens(chunk), MtmdNative.ChunkPositions(chunk)));
                }
            }
            var prompt = new PreparedPrompt(pieces, this, chunks, bitmaps);
            chunks = IntPtr.Zero;
            return prompt;
        }
        finally
        {
            if (chunks != IntPtr.Zero)
            {
                MtmdNative.ChunksFree(chunks);
                foreach (var bitmap in bitmaps) if (bitmap != IntPtr.Zero) MtmdNative.BitmapFree(bitmap);
            }
        }
    }

    /// <summary>Reads a medium's chunk into a sequence at <paramref name="position"/>: encoded, then decoded into the
    /// context. Returns the next position.</summary>
    internal int Read(LlamaContext context, PromptPiece piece, int sequence, int position, bool sample, out int? next, LlamaSampler? sampler = null)
    {
        int after = 0;
        next = context.Read(handle =>
        {
            int past;
            int result = MtmdNative.EvalChunk(Handle, handle, piece.Chunk, position, sequence, LlamaContext.Batch, sample, &past);
            after = past;
            return result;
        }, sample, sampler);
        return after;
    }

    private IntPtr Handle => _context != IntPtr.Zero ? _context : throw new ObjectDisposedException(nameof(MediaProjector));

    public void Dispose()
    {
        if (_context == IntPtr.Zero) return;
        MtmdNative.Free(_context);
        _context = IntPtr.Zero;
    }

    internal static void Release(IntPtr chunks, IntPtr[] bitmaps)
    {
        MtmdNative.ChunksFree(chunks);
        foreach (var bitmap in bitmaps) if (bitmap != IntPtr.Zero) MtmdNative.BitmapFree(bitmap);
    }
}

/// <summary>One piece of a prepared prompt: text, as its tokens, or a medium, as llama.cpp's chunk of it.</summary>
/// <param name="Tokens">The text's tokens; null for a medium.</param>
/// <param name="Chunk">The medium's chunk; zero for text.</param>
/// <param name="TokenCount">The cache cells it takes.</param>
/// <param name="Positions">The positions it advances by — fewer than its tokens where the model numbers an image's
/// rows and columns rather than its tokens.</param>
internal sealed record PromptPiece(int[]? Tokens, IntPtr Chunk, int TokenCount, int Positions)
{
    public bool IsText => Tokens is not null;
}

/// <summary>A prompt ready to read: its pieces in order, and what they take.</summary>
internal sealed class PreparedPrompt : IDisposable
{
    private readonly IntPtr _chunks;
    private readonly IntPtr[] _bitmaps;
    private bool _disposed;

    internal PreparedPrompt(IReadOnlyList<PromptPiece> pieces, MediaProjector? projector, IntPtr chunks = default, IntPtr[]? bitmaps = null)
    {
        Pieces = pieces;
        Projector = projector;
        _chunks = chunks;
        _bitmaps = bitmaps ?? [];
    }

    /// <summary>A prompt of text alone.</summary>
    public static PreparedPrompt Text(int[] tokens) => new([new PromptPiece(tokens, IntPtr.Zero, tokens.Length, tokens.Length)], null);

    public IReadOnlyList<PromptPiece> Pieces { get; }

    /// <summary>What reads its media; null for text alone.</summary>
    public MediaProjector? Projector { get; }

    public int Tokens => Pieces.Sum(p => p.TokenCount);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_chunks != IntPtr.Zero) MediaProjector.Release(_chunks, _bitmaps);
    }
}
