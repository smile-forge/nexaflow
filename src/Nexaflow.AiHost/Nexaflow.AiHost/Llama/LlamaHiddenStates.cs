using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// A model's last hidden state over a text, every token's row: what a model that reads prompts for another gives it,
/// in place of a reply. A context of its own that keeps hidden states and pools none of them. Each read starts from
/// nothing — a recurrent model's state included — takes the text a batch at a time, and keeps every token's row. Every
/// read takes the context's lock: llama.cpp serves one caller at a time.
/// </summary>
internal sealed unsafe class LlamaHiddenStates : IDisposable
{
    private readonly object _gate = new();
    private readonly IntPtr _memory;
    private IntPtr _context;
    private LlamaNative.Batch _batch;

    /// <param name="longest">The most tokens a read takes: the context is made that long.</param>
    /// <exception cref="HostRequestException">llama.cpp would not make the context.</exception>
    public LlamaHiddenStates(LlamaModel model, int longest)
    {
        var parameters = LlamaNative.ContextDefaults();
        parameters.Context = (uint)longest;
        parameters.Batch = LlamaContext.Batch;
        parameters.MicroBatch = LlamaContext.Batch;
        parameters.MaxSequences = 1;
        parameters.Embeddings = 1;
        parameters.PoolingType = LlamaNative.NoPooling;
        parameters.FlashAttention = LlamaNative.FlashAttention.Auto;
        _context = LlamaNative.ContextInit(model.Handle, parameters);
        if (_context == IntPtr.Zero)
            throw new HostRequestException(HostErrorKind.Runtime, $"llama.cpp could not make a context of {longest} tokens; see the host's log.");
        _memory = LlamaNative.ContextMemory(_context);
        _batch = LlamaNative.BatchInit(LlamaContext.Batch, 0, 1);
        Width = LlamaNative.ModelWidth(model.Handle);
        Longest = (int)LlamaNative.ContextSize(_context);
    }

    /// <summary>The values in each token's row.</summary>
    public int Width { get; }

    /// <summary>The most tokens a read takes.</summary>
    public int Longest { get; }

    /// <summary>The last hidden state over <paramref name="tokens"/>, [tokens, <see cref="Width"/>] row-major.</summary>
    public float[] Read(ReadOnlySpan<int> tokens)
    {
        if (tokens.Length == 0 || tokens.Length > Longest)
            throw new ArgumentOutOfRangeException(nameof(tokens), tokens.Length, $"1 to {Longest} tokens per read.");
        var rows = new float[tokens.Length * Width];
        lock (_gate)
        {
            var context = _context != IntPtr.Zero ? _context : throw new ObjectDisposedException(nameof(LlamaHiddenStates));
            LlamaNative.MemoryClear(_memory, data: true);
            var batch = _batch;
            for (int start = 0; start < tokens.Length; start += LlamaContext.Batch)
            {
                int count = Math.Min(LlamaContext.Batch, tokens.Length - start);
                batch.TokenCount = count;
                for (int i = 0; i < count; i++)
                {
                    batch.Token[i] = tokens[start + i];
                    batch.Position[i] = start + i;
                    batch.SequenceIdCount[i] = 1;
                    batch.SequenceId[i][0] = 0;
                    batch.Logits[i] = 1;
                }
                LlamaContext.Check(LlamaNative.Decode(context, batch));
                for (int i = 0; i < count; i++)
                {
                    float* row = LlamaNative.HiddenState(context, i);
                    if (row is null) throw new HostRequestException(HostErrorKind.Runtime, "llama.cpp kept no hidden state for a token it read.");
                    new ReadOnlySpan<float>(row, Width).CopyTo(rows.AsSpan((start + i) * Width, Width));
                }
            }
        }
        return rows;
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
