using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>A model in memory, of whatever kind its runner made it.</summary>
internal interface IHostModel : IDisposable
{
    ModelSpec Spec { get; }
}

/// <summary>
/// Loads one family of model from its prepared folder — a chat model or a recogniser through onnxruntime-genai, a voice
/// activity detector through a plain ONNX Runtime session. A new family of model is a new runner.
/// </summary>
internal interface IModelRunner
{
    /// <summary>What a <see cref="ModelSpec.Runner"/> names it.</summary>
    string Id { get; }

    /// <summary>Loads the model where its spec places it: on <see cref="ModelSpec.Device"/>, or the CPU — a runner for a
    /// model small enough for the CPU keeps it there.</summary>
    IHostModel Load(ModelSpec spec);
}

/// <summary>The runners this host has.</summary>
internal sealed class ModelRunners(IEnumerable<IModelRunner> runners)
{
    private readonly Dictionary<string, IModelRunner> _runners = runners.ToDictionary(r => r.Id, StringComparer.Ordinal);

    public static ModelRunners Default { get; } = new(
    [
        new ChatModelRunner(),
        new Speech.SileroVoiceActivityRunner(),
        new Speech.StreamingRecognizerRunner(),
        new Speech.WhisperTranscriberRunner(),
    new Speech.SmartTurnRunner(),
    new Speech.KokoroSynthesizerRunner(),
    new Drawing.ZImagePromptEncoderRunner(),
    new Drawing.ZImageDenoiserRunner(),
    new Drawing.ZImageDecoderRunner(),
    new Drawing.HiDreamDenoiserRunner(),
    ]);

    /// <exception cref="HostRequestException">This host has no runner of that name.</exception>
    public IHostModel Load(ModelSpec spec)
    {
        if (!_runners.TryGetValue(spec.Runner, out var runner))
            throw new HostRequestException(HostErrorKind.Invalid, $"This host has no '{spec.Runner}' runner for {spec.Id}.");
        return runner.Load(spec);
    }
}
