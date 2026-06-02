namespace Nexaflow.Providers.Common;

/// <summary>
/// The specific model an <see cref="ILlmProvider"/> instance is bound to. The host injects this at
/// construction so that each (config, model) pairing the user assigns gets its own provider instance
/// — a prerequisite for stateful/streaming providers that hold a per-model channel open across calls.
/// <para>
/// A provider declares a <c>ProviderModel</c> constructor parameter to receive it. A model-agnostic
/// "capability" instance (created only to enumerate models for the options UI) is given an empty model.
/// </para>
/// </summary>
public sealed record ProviderModel(string Model)
{
    /// <summary>True for a capability instance (no model bound) — used only for enumeration.</summary>
    public bool IsUnbound => string.IsNullOrEmpty(Model);
}
