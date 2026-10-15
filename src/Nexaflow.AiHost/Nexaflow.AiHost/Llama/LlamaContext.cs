using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// One KV cache for a model's open conversations, each a sequence of its own in one unified cache: the cache is
/// allocated once, as long as the model's context, and conversations share it. Reading appends a sequence's tokens at
/// its next positions; the next token is chosen by the reader's sampler — the likeliest, when it gives none — from the
/// logits the last read left, so a read and the sample after it happen together, before another conversation's read can
/// replace them. Every call takes the context's lock: llama.cpp
/// serves one caller at a time.
/// </summary>
internal sealed unsafe class LlamaContext : IDisposable
{
    /// <summary>Tokens read per call — llama.cpp's batch — and so the slice a prompt is read in.</summary>
    public const int Batch = 512;

    private readonly object _gate = new();
    private readonly IntPtr _memory;
    private IntPtr _context;
    private IntPtr _sampler;
    private LlamaNative.Batch _batch;

    public LlamaContext(LlamaModel model, int contextTokens, int sequences)
    {
        var parameters = LlamaNative.ContextDefaults();
        parameters.Context = (uint)contextTokens;
        parameters.Batch = Batch;
        parameters.MicroBatch = Batch;
        parameters.MaxSequences = (uint)sequences;
        parameters.KvUnified = 1;
        // Sliding-window layers keep their window alone: a conversation is appended to or forgotten whole, never cut
        // back, so nothing older than the window is read again — and a large Gemma 4 holds half the memory.
        parameters.SwaFull = 0;
        parameters.FlashAttention = LlamaNative.FlashAttention.Auto;
        _context = LlamaNative.ContextInit(model.Handle, parameters);
        if (_context == IntPtr.Zero)
            throw new HostRequestException(HostErrorKind.Runtime, $"llama.cpp could not make a context of {contextTokens} tokens; see the host's log.");
        _memory = LlamaNative.ContextMemory(_context);
        _sampler = LlamaNative.SamplerGreedy();
        _batch = LlamaNative.BatchInit(Batch, 0, 1);
        Tokens = (int)LlamaNative.ContextSize(_context);
        Sequences = sequences;
    }

    /// <summary>The tokens the cache holds, all sequences together.</summary>
    public int Tokens { get; }

    public int Sequences { get; }

    /// <summary>Reads <paramref name="tokens"/> into a sequence from <paramref name="position"/>, at most
    /// <see cref="Batch"/> of them; with <paramref name="sample"/>, the next token is chosen from the last one's
    /// logits and returned.</summary>
    public int? Read(int sequence, int position, ReadOnlySpan<int> tokens, bool sample, LlamaSampler? sampler = null)
    {
        if (tokens.Length is 0 or > Batch) throw new ArgumentOutOfRangeException(nameof(tokens), tokens.Length, $"1 to {Batch} tokens per read.");
        lock (_gate)
        {
            var batch = _batch;
            batch.TokenCount = tokens.Length;
            for (int i = 0; i < tokens.Length; i++)
            {
                batch.Token[i] = tokens[i];
                batch.Position[i] = position + i;
                batch.SequenceIdCount[i] = 1;
                batch.SequenceId[i][0] = sequence;
                batch.Logits[i] = (sbyte)(sample && i == tokens.Length - 1 ? 1 : 0);
            }
            Check(LlamaNative.Decode(Handle, batch));
            return sample ? LlamaNative.SamplerSample(sampler?.Handle ?? _sampler, Handle, -1) : null;
        }
    }

    /// <summary>Runs <paramref name="read"/> — a read llama.cpp's own helpers make, such as an image's — under the
    /// context's lock, then samples the next token when asked.</summary>
    public int? Read(Func<IntPtr, int> read, bool sample, LlamaSampler? sampler = null)
    {
        lock (_gate)
        {
            Check(read(Handle));
            return sample ? LlamaNative.SamplerSample(sampler?.Handle ?? _sampler, Handle, -1) : null;
        }
    }

    /// <summary>Drops a sequence's tokens from <paramref name="from"/> on — all of them by default.</summary>
    public void Forget(int sequence, int from = -1)
    {
        lock (_gate)
            if (_context != IntPtr.Zero) LlamaNative.MemoryRemove(_memory, sequence, from, -1);
    }

    private IntPtr Handle => _context != IntPtr.Zero ? _context : throw new ObjectDisposedException(nameof(LlamaContext));

    private static void Check(int result)
    {
        switch (result)
        {
            case 0:
                return;
            case 1:
                throw new HostRequestException(HostErrorKind.Refused, "The model's context has no room left for what was read.");
            case 2:
                throw new HostRequestException(HostErrorKind.Cancelled, "The read was stopped.");
            default:
                throw new HostRequestException(HostErrorKind.Runtime, $"llama.cpp failed to read ({result}); see the host's log.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_context == IntPtr.Zero) return;
            LlamaNative.BatchFree(_batch);
            LlamaNative.SamplerFree(_sampler);
            LlamaNative.ContextFree(_context);
            _sampler = IntPtr.Zero;
            _context = IntPtr.Zero;
        }
    }
}
