using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// How one reply chooses its tokens: llama.cpp's sampler chain, built for the request from the model's recommended
/// sampling — a penalty on what it has already written, the likeliest few, the fewest of those that make up most of
/// the chance, a temperature, and a draw — or the likeliest each time when the request gives none. A reply that always
/// takes the likeliest token can go round the same thought for ever; a model's makers tune it to be sampled. A reply
/// its makers decode with a <see cref="RepeatBan"/> has the banned tokens' scores struck out before each choice, so
/// it remembers what it chose.
/// </summary>
internal sealed unsafe class LlamaSampler : IDisposable
{
    private readonly RepeatBan? _ban;
    private readonly List<int> _written = [];

    private readonly List<int> _banned = [];

    /// <param name="contextTokens">The model's context: a presence penalty remembers what was written for as long.</param>
    public LlamaSampler(TokenSampling? sampling, int contextTokens, RepeatBan? ban = null)
    {
        _ban = ban;
        if (sampling is null)
        {
            Handle = LlamaNative.SamplerGreedy();
            return;
        }
        Handle = LlamaNative.SamplerChain(LlamaNative.SamplerChainDefaults());
        if (sampling.PresencePenalty != 0) Add(LlamaNative.SamplerPenalties(contextTokens, 1f, 0f, sampling.PresencePenalty));
        if (sampling.TopK > 0)             Add(LlamaNative.SamplerTopK(sampling.TopK));
        if (sampling.TopP is > 0 and < 1)  Add(LlamaNative.SamplerTopP(sampling.TopP, 1));
        Add(LlamaNative.SamplerTemperature(sampling.Temperature));
        Add(LlamaNative.SamplerDistribution(sampling.Seed ?? LlamaNative.DefaultSeed));
    }

    public IntPtr Handle { get; private set; }

    /// <summary>Chooses the token after the read that left <paramref name="context"/>'s last scores.</summary>
    public int Sample(IntPtr context)
    {
        if (_ban is { } ban) Strike(LlamaNative.Logits(context, -1), ban);
        int token = LlamaNative.SamplerSample(Handle, context, -1);
        if (_ban is not null) _written.Add(token);
        return token;
    }

    /// <summary>Strikes out the tokens <see cref="Banned"/> names.</summary>
    private void Strike(float* scores, RepeatBan ban)
    {
        Banned(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_written), ban, _banned);
        foreach (int token in _banned) scores[token] = float.NegativeInfinity;
    }

    /// <summary>The tokens that would end a run of <see cref="RepeatBan.Run"/> tokens already standing, whole, among the
    /// last <see cref="RepeatBan.Within"/> <paramref name="written"/>: wherever the run's first tokens stand just as the
    /// reply now ends, what followed them there may not follow again — the makers' sliding-window n-gram ban.</summary>
    internal static void Banned(ReadOnlySpan<int> written, RepeatBan ban, List<int> into)
    {
        into.Clear();
        int count = written.Length, lead = ban.Run - 1;
        if (count < ban.Run) return;
        var ending = written[(count - lead)..];
        for (int at = Math.Max(0, count - ban.Within); at <= count - ban.Run; at++)
            if (written.Slice(at, lead).SequenceEqual(ending)) into.Add(written[at + lead]);
    }

    private void Add(IntPtr sampler) => LlamaNative.SamplerChainAdd(Handle, sampler);

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        LlamaNative.SamplerFree(Handle);
        Handle = IntPtr.Zero;
    }
}
