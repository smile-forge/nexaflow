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

    /// <summary>Returns a confidence score (0–1) that this handler can process the given input.</summary>
    float CanProcess(string input);

    /// <summary>
    /// Processes the input. Returns a response string to show in AI Chat,
    /// or null if the action was handled silently with no output.
    /// </summary>
    Task<string?> ProcessAsync(string input);
}
