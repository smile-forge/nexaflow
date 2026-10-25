using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// One KV cache for a model's open conversations, each a sequence of its own in one unified cache: the cache is
/// allocated once, as long as the model's context, and conversations share it. A read appends tokens to each of several
/// sequences at their next positions, all in one llama.cpp call; the token after a sequence's last is chosen, where asked, by
/// its reader's sampler — the likeliest, when it gives none — from the logits the read left, so a read and the samples after it
/// happen together, before another read can replace them. Every call takes the context's lock: llama.cpp serves one caller at a
/// time.
/// </summary>
internal sealed unsafe class LlamaContext : IDisposable
{
    /// <summary>Tokens read per call where the model's spec gives no more room.</summary>
    public const int DefaultBatch = ModelSpec.DefaultBatch;

    private readonly object _gate = new();
    private readonly IntPtr _memory;
    private IntPtr _context;
    private readonly int _vocabulary;
    private LlamaNative.Batch _batch;

    /// <param name="batch">The tokens read per call, every sequence's together.</param>
    /// <param name="cache">How the cache holds each token. Held in 8 bits, flash attention reads it: llama.cpp reads quantized
    /// values no other way.</param>
    public LlamaContext(LlamaModel model, int contextTokens, int sequences, int batch = DefaultBatch, ContextCache cache = ContextCache.F16)
    {
        var parameters = LlamaNative.ContextDefaults();
        parameters.Context = (uint)contextTokens;
        parameters.Batch = (uint)batch;
        parameters.MicroBatch = (uint)batch;
        parameters.MaxSequences = (uint)sequences;
        parameters.KvUnified = 1;
        // Sliding-window layers keep their window alone: a conversation is appended to or forgotten whole, never cut
        // back, so nothing older than the window is read again — and a large Gemma 4 holds half the memory.
        parameters.SwaFull = 0;
        parameters.FlashAttention = cache == ContextCache.Q8 ? LlamaNative.FlashAttention.Enabled : LlamaNative.FlashAttention.Auto;
        if (cache == ContextCache.Q8) parameters.KeyType = parameters.ValueType = (int)LlamaNative.GgmlType.Q8_0;
        _context = LlamaNative.ContextInit(model.Handle, parameters);
        if (_context == IntPtr.Zero)
            throw new HostRequestException(HostErrorKind.Runtime, $"llama.cpp could not make a context of {contextTokens} tokens; see the host's log.");
        _memory = LlamaNative.ContextMemory(_context);
        _vocabulary = model.VocabSize;
        _batch = LlamaNative.BatchInit(batch, 0, 1);
        Tokens = (int)LlamaNative.ContextSize(_context);
        Sequences = sequences;
        Batch = batch;
    }

    /// <summary>The tokens the cache holds, all sequences together.</summary>
    public int Tokens { get; }

    public int Sequences { get; }

    /// <summary>The tokens one call reads, every sequence's together.</summary>
    public int Batch { get; }

    /// <summary>Reads each of <paramref name="reads"/> into its sequence, all in one call — <see cref="Batch"/> tokens at most — and
    /// chooses, for each that asks, the token after its last by its sampler, into <paramref name="chosen"/> in the same order;
    /// -1 for one that does not ask.</summary>
    /// <remarks>
    /// Each choice is made over the whole vocabulary — a quarter of a million scores for a Gemma — and on the CPU, so a step's
    /// choices, made one after another, would take longer than the step itself once several replies are written together, the
    /// GPU waiting on them. They are made side by side instead: each reply has its own sampler and its own row of scores, and
    /// what llama.cpp settles the first time scores are read — the order of its outputs — is settled before any is chosen.
    /// </remarks>
    public void Read(SequenceRead[] reads, int[] chosen)
    {
        int count = 0;
        foreach (var read in reads) count += read.Tokens.Length;
        if (reads.Length == 0 || count > Batch) throw new ArgumentOutOfRangeException(nameof(reads), count, $"1 to {Batch} tokens per read.");
        lock (_gate)
        {
            var batch = _batch;
            batch.TokenCount = count;
            var last = new int[reads.Length];
            int at = 0, asking = 0;
            for (int r = 0; r < reads.Length; r++)
            {
                var tokens = reads[r].Tokens.Span;
                for (int i = 0; i < tokens.Length; i++, at++)
                {
                    batch.Token[at] = tokens[i];
                    batch.Position[at] = reads[r].Position + i;
                    batch.SequenceIdCount[at] = 1;
                    batch.SequenceId[at][0] = reads[r].Sequence;
                    batch.Logits[at] = (sbyte)(reads[r].Sample && i == tokens.Length - 1 ? 1 : 0);
                }
                last[r] = at - 1;
                if (reads[r].Sample) asking++;
            }
        Check(LlamaNative.Decode(Handle, batch));

            if (asking > 1)
            {
                LlamaNative.Logits(Handle, last[Array.FindIndex(reads, read => read.Sample)]);   // its outputs put in order, once
                Parallel.For(0, reads.Length, r => chosen[r] = reads[r].Sample ? Sample(reads[r].Sampler, last[r]) : -1);
            }
            else
            for (int r = 0; r < reads.Length; r++) chosen[r] = reads[r].Sample ? Sample(reads[r].Sampler, last[r]) : -1;
        }
    }

    /// <summary>Runs <paramref name="read"/> — a read llama.cpp's own helpers make, such as an image's — under the
    /// context's lock, then samples the next token when asked.</summary>
    public int? Read(Func<IntPtr, int> read, bool sample, LlamaSampler? sampler = null)
    {
        lock (_gate)
        {
            Check(read(Handle));
            return sample ? Sample(sampler) : null;
        }
    }

    /// <summary>Drops a sequence's tokens at positions from <paramref name="from"/> up to <paramref name="to"/> — all of
    /// them by default.</summary>
    public void Forget(int sequence, int from = -1, int to = -1)
    {
        lock (_gate)
            if (_context != IntPtr.Zero) LlamaNative.MemoryRemove(_memory, sequence, from, to);
    }

    /// <summary>Has sequence <paramref name="to"/> hold the first <paramref name="tokens"/> of what <paramref name="from"/> has read, as
    /// it holds them: the same cells, which the two then share.</summary>
    public void Share(int from, int to, int tokens)
    {
        lock (_gate)
            LlamaNative.MemoryShare(_memory, from, to, 0, tokens);
    }

    /// <summary>The next token, from the scores the last read left for its <paramref name="index"/>-th output — -1 the last:
    /// by the reader's sampler, or the likeliest.</summary>
    private int Sample(LlamaSampler? sampler, int index = -1)
        => sampler?.Sample(Handle, index, _vocabulary)
           ?? LlamaScores.Likeliest(new ReadOnlySpan<float>(LlamaNative.Logits(Handle, index), _vocabulary));

    private IntPtr Handle => _context != IntPtr.Zero ? _context : throw new ObjectDisposedException(nameof(LlamaContext));

    internal static void Check(int result)
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

            LlamaNative.ContextFree(_context);

            _context = IntPtr.Zero;
        }
    }
}

/// <summary>Tokens to read into a sequence from a position — one sequence's share of a <see cref="LlamaContext.Read(ReadOnlySpan{SequenceRead}, Span{int})"/>
/// — and whether the token after its last is chosen, by what sampler: null for the likeliest.</summary>
internal readonly record struct SequenceRead(int Sequence, int Position, ReadOnlyMemory<int> Tokens, bool Sample, LlamaSampler? Sampler);
