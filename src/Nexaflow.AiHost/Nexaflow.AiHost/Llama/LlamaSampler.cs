using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// How one reply chooses its tokens: llama.cpp's sampler chain, built for the request from the model's recommended
/// sampling — a penalty on what it has already written, the likeliest few, the fewest of those that make up most of
/// the chance, a temperature, and a draw — or the likeliest each time when the request gives none. A reply that always
/// takes the likeliest token can go round the same thought for ever; a model's makers tune it to be sampled.
/// </summary>
internal sealed class LlamaSampler : IDisposable
{
    /// <param name="contextTokens">The model's context: a presence penalty remembers what was written for as long.</param>
    public LlamaSampler(TokenSampling? sampling, int contextTokens)
    {
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

    private void Add(IntPtr sampler) => LlamaNative.SamplerChainAdd(Handle, sampler);

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        LlamaNative.SamplerFree(Handle);
        Handle = IntPtr.Zero;
    }
}
