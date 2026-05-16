namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by classes that can handle shell input bar queries.
/// Register globally via <see cref="FeatureManager.RegisterQueryHandler"/> for app-wide handling,
/// or implement on a page's DataContext for tab-scoped handling.
/// </summary>
public interface IQueryHandler
{
    /// <summary>Human-readable description used in LLM tool-selection prompts.</summary>
    string Description { get; }

    /// <summary>Optional single-character prefix that explicitly routes input to this handler.</summary>
    string? Symbol => null;

    /// <summary>
    /// Returns a confidence score (0–1) that this handler can process the given input.
    /// <paramref name="page"/> is the currently active tab view (may be null for unmigrated views).
    /// Use <c>page?.GetContextObject()</c> for typed context and <c>page?.ViewModel</c> for
    /// direct ViewModel access.
    /// </summary>
    float CanProcess(string input, IPageView? page = null);

    /// <summary>
    /// Processes the input. Returns a response string to show in AI Chat,
    /// or null if the action was handled silently with no output.
    /// <paramref name="page"/> carries the same context as passed to <see cref="CanProcess"/>.
    /// </summary>
    Task<string?> ProcessAsync(string input, IPageView? page = null);
}
