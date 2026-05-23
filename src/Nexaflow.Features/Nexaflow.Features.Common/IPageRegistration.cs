namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by each feature assembly to advertise one page kind that it can create.
/// Instances are registered with <see cref="FeatureManager"/> at application startup so
/// the shell never needs a direct reference to a feature's view or view-model types.
/// </summary>
public interface IPageRegistration
{
    /// <summary>
    /// The stable string identifier for this page kind (e.g. <c>"Console"</c>).
    /// Used as the key in <see cref="FeatureManager"/> and persisted in ribbon.json.
    /// </summary>
    string PageKind { get; }

    /// <summary>Creates a ready-to-open <see cref="Page"/> for this page kind.</summary>
    /// <param name="pageParams">Optional parameters (e.g. <c>{"folder":"MyProject"}</c>).</param>
    Page CreatePage(Dictionary<string, string>? pageParams = null);
}
