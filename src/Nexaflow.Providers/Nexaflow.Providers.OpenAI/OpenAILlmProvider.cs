using Nexaflow.Providers.Common;
using Nexaflow.Providers.OpenAICompatible;
using OpenAI;

namespace Nexaflow.Providers.OpenAI;

/// <summary>OpenAI's chat models, over the chat-completions wire format they set.</summary>
public sealed class OpenAILlmProvider : ChatCompletionsLlmProvider
{
    public const  string ProviderName = "OpenAI";
    public override string Name => ProviderName;

    /// <summary>Vision is model-bound: the chat families accept image parts, the specialised
    /// models (embeddings, audio, images, legacy 3.5/instruct) don't.</summary>
    public override bool SupportsImages => ModelSupportsVision(Model);
    public override bool SupportsAudio  => ModelHearsAudio(Model);

    /// <summary>OpenAI's chat models that take audio input are named for it (gpt-audio, gpt-4o-audio-preview…);
    /// its realtime, transcription and speech models are reached by other APIs.</summary>
    internal static bool ModelHearsAudio(string model)
        => model.Contains("audio", StringComparison.OrdinalIgnoreCase)
        && !model.Contains("realtime", StringComparison.OrdinalIgnoreCase)
        && !model.Contains("transcribe", StringComparison.OrdinalIgnoreCase)
        && !model.Contains("tts", StringComparison.OrdinalIgnoreCase);

    /// <summary>OpenAI's API, unless the base URL points at an OpenAI-compatible server on this machine.</summary>
    public override DataResidency Residency => EndpointResidency.Of(_config.BaseUrl, DataResidency.LeavesMachine);

    public override bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    private readonly OpenAIConfig _config;

    public OpenAILlmProvider(IBackgroundActivityManager activityManager, OpenAIConfig config, ProviderModel model)
        : base(activityManager, model)
    {
        _config = config;
    }

    protected override OpenAIClient CreateClient() => OpenAIClients.Create(_config);

    /// <summary>Name-based vision heuristic — specialised/legacy families are text-only, current
    /// chat families (gpt-4o/4.1/5, o-series) accept image parts.</summary>
    internal static bool ModelSupportsVision(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        string[] textOnly = ["embedding", "whisper", "tts", "dall-e", "audio", "moderation",
                             "instruct", "gpt-3.5", "davinci", "babbage"];
        foreach (var t in textOnly)
            if (model.Contains(t, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>Context-window table by family; null when unknown (the host then skips budgeting).</summary>
    internal static int? ContextWindowTokens(string model) => model switch
    {
        _ when model.Contains("gpt-4.1",     StringComparison.OrdinalIgnoreCase) => 1_000_000,
        _ when model.Contains("gpt-5",       StringComparison.OrdinalIgnoreCase) => 400_000,
        _ when model.Contains("gpt-4o",      StringComparison.OrdinalIgnoreCase) => 128_000,
        _ when model.Contains("gpt-4-turbo", StringComparison.OrdinalIgnoreCase) => 128_000,
        _ when model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase)        => 200_000,
        _ when model.Contains("gpt-3.5",     StringComparison.OrdinalIgnoreCase) => 16_385,
        _ => null,
    };

    public override Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
    {
        ModelInfo? info = !string.IsNullOrWhiteSpace(Model) && ContextWindowTokens(Model) is { } window
            ? new ModelInfo(window, DisplayName: Model)
            : null;
        return Task.FromResult(info);
    }
}
