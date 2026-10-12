namespace Nexaflow.Providers.Common;

/// <summary>
/// What every provider instance has, whatever kind of model it serves: a name, where its requests go, whether it
/// can take one now, the models it offers, and a lifetime. A provider serves one <see cref="ModelKind"/>, through
/// the contract for that kind (<see cref="ILlmProvider"/>, <see cref="ISpeechToTextProvider"/>,
/// <see cref="ITextToSpeechProvider"/>); one backend offering
/// several kinds ships a provider type per kind under the same <see cref="Name"/>.
/// <para>
/// One instance is created per (config, model) the user assigns — the model is injected at construction via
/// <see cref="ProviderModel"/>. A model-agnostic instance (constructed with an empty <see cref="ProviderModel"/>) is
/// used by the options UI only to call <see cref="GetAvailableModelsAsync"/>; it never serves a request. Providers
/// report activity through an <see cref="IBackgroundActivityManager"/> supplied at construction, so they need no
/// knowledge of WPF or observable properties.
/// </para>
/// </summary>
public interface IAiProvider
{
    string Name { get; }

    /// <summary>
    /// Where a request's content goes. A page whose context is high-risk asks the user before its contents go to
    /// a provider whose data leaves this machine.
    /// </summary>
    DataResidency Residency { get; }

    /// <summary>
    /// Who runs the model's harness — its prompt, chat template and context. Independent of where it runs: Ollama is
    /// bridged though it is on this machine. Default: bridged; a provider that is Nexaflow's own harness says so.
    /// </summary>
    ModelHarness Harness => ModelHarness.Bridged;

    /// <summary>
    /// Whether this instance can take a request now. An ability's chain of models passes over an unavailable
    /// one to the next — unavailability, not a failed request, is what moves a chain on. Default: available.
    /// </summary>
    bool IsAvailable => true;

    /// <summary>
    /// Raised when <see cref="IsAvailable"/> changes — a model finished downloading, say — so whatever offers
    /// this provider's ability can appear or go. Default: never raised.
    /// </summary>
    event EventHandler? AvailabilityChanged { add { } remove { } }

    /// <summary>
    /// Returns the model identifiers this provider offers for its kind (independent of the bound model — a
    /// provider/config-level capability). Implementations may query a remote API or return a static list.
    /// Returns an empty list on failure; never throws.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Why the last <see cref="GetAvailableModelsAsync"/> came back empty (bad key, unreachable
    /// backend…), or null after a success. Lets the options UI distinguish "no models" from
    /// "misconfigured" without breaking the never-throws contract. Default: null (not tracked).
    /// </summary>
    string? LastModelListError => null;

    /// <summary>
    /// Called when this instance is created because its (config, model) became referenced by the first
    /// active workspace. Lets the provider pre-load / cache anything it needs — e.g. a local backend can
    /// load the model into memory. Best-effort and fire-and-forget: implementations must never throw.
    /// Default implementation is a no-op (remote providers need no warm-up).
    /// </summary>
    Task WarmupAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Called when this instance is being released because its (config, model) is no longer referenced
    /// by ANY active workspace. Lets the provider release resources — e.g. a local backend can unload
    /// the model from memory. Best-effort and fire-and-forget: implementations must never throw.
    /// Default implementation is a no-op.
    /// </summary>
    Task CooldownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Who runs a model's harness.</summary>
public enum ModelHarness
{
    /// <summary>Nexaflow is a client of a harness that is not its own — a cloud API, or a local server like Ollama.</summary>
    Bridged,
    /// <summary>Nexaflow is the whole harness: the model runs in Nexaflow's own process or its AI host.</summary>
    Owned,
}

/// <summary>The kind of model a provider serves — an ability-grid row takes models of its own kind.</summary>
public enum ModelKind
{
    /// <summary>A language model: text (and, for some, images) in, text out — <see cref="ILlmProvider"/>.</summary>
    Language,

    /// <summary>Speech in, text out — <see cref="ISpeechToTextProvider"/>.</summary>
    SpeechToText,

    /// <summary>Text in, speech out — <see cref="ITextToSpeechProvider"/>.</summary>
    TextToSpeech,
}

public static class ModelKinds
{
    /// <summary>The kinds a provider type serves, from the contracts it implements.</summary>
    public static IReadOnlyList<ModelKind> Of(Type providerType)
    {
        var kinds = new List<ModelKind>();
        if (typeof(ILlmProvider).IsAssignableFrom(providerType))          kinds.Add(ModelKind.Language);
        if (typeof(ISpeechToTextProvider).IsAssignableFrom(providerType)) kinds.Add(ModelKind.SpeechToText);
        if (typeof(ITextToSpeechProvider).IsAssignableFrom(providerType)) kinds.Add(ModelKind.TextToSpeech);
        return kinds;
    }

    /// <summary>Whether <paramref name="provider"/> serves models of <paramref name="kind"/>.</summary>
    public static bool Serves(this IAiProvider provider, ModelKind kind) => kind switch
    {
        ModelKind.Language     => provider is ILlmProvider,
        ModelKind.SpeechToText => provider is ISpeechToTextProvider,
        ModelKind.TextToSpeech => provider is ITextToSpeechProvider,
        _                      => false,
    };
}
