using Nexaflow.Providers.Common;
using Nexaflow.Providers.OpenAICompatible;
using OpenAI;
using System.ClientModel;

namespace Nexaflow.Providers.XAI;

/// <summary>
/// xAI's Grok models, over its OpenAI-compatible chat-completions API. Nothing about a model is guessed from its name —
/// Grok's names change too often for a table to keep up: xAI says which models it serves, which of them take pictures,
/// and how much each reads.
/// </summary>
public sealed class XAILlmProvider : ChatCompletionsLlmProvider
{
    public const  string ProviderName = "xAI";
    public override string Name => ProviderName;

    /// <summary>xAI's API, unless the base URL points at a gateway on this machine.</summary>
    public override DataResidency Residency => EndpointResidency.Of(_config.BaseUrl, DataResidency.LeavesMachine);

    public override bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    /// <summary>Whether xAI lists pictures among the bound model's inputs, read with its details when the instance is
    /// made. Until they are read, a picture is described to it rather than sent.</summary>
    public override bool SupportsImages => _details is { IsCompletedSuccessfully: true, Result.TakesImages: true };

    private readonly XAIConfig _config;
    private XAIModelCatalog?   _catalog;

    /// <summary>The read of the bound model's details, begun when the instance is made. A read that failed comes back
    /// null, and the next ask for the model's details reads them again.</summary>
    private Task<XAIModelDetails?> _details = Task.FromResult<XAIModelDetails?>(null);

    public XAILlmProvider(IBackgroundActivityManager activityManager, XAIConfig config, ProviderModel model)
        : base(activityManager, model)
    {
        _config = config;
        // Read now, off the caller's thread, so whether the model takes pictures is known by its first request.
        if (KnowsItsModel) _details = Task.Run(() => ReadDetailsAsync());
    }

    /// <summary>There is a model to ask about, and a key to ask with.</summary>
    private bool KnowsItsModel => !string.IsNullOrWhiteSpace(Model) && IsAvailable;

    protected override OpenAIClient CreateClient()
        => new(new ApiKeyCredential(_config.ApiKey), new OpenAIClientOptions { Endpoint = _config.Endpoint });

    private XAIModelCatalog Catalog => LazyInitializer.EnsureInitialized(ref _catalog, () => new XAIModelCatalog(Client, _config.Endpoint));

    /// <summary>xAI's language models alone — not the picture and video models its OpenAI-compatible list names too.</summary>
    protected override Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Catalog.LanguageModelsAsync(ct);

    public override async Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
    {
        if (!KnowsItsModel) return null;
        var details = await _details.WaitAsync(ct) ?? await (_details = Task.Run(() => ReadDetailsAsync())).WaitAsync(ct);
        return details?.ContextLength is { } tokens ? new ModelInfo(tokens, Model) : null;
    }

    /// <summary>The bound model's details, or null when xAI could not say (unreachable, or a model it does not serve).
    /// Read whole on its own token: a caller that stops waiting leaves it to finish for the next.</summary>
    private async Task<XAIModelDetails?> ReadDetailsAsync()
    {
        try { return await Catalog.DetailsAsync(Model, CancellationToken.None); }
        catch { return null; }
    }
}
