namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by classes that can handle shell input bar queries. Implementations are
/// auto-discovered (the feature catalog) and built per workspace runtime — there is no
/// registration step. Scope is expressed in <see cref="CanProcess"/>: the active page's
/// ViewModel is passed in, so a tab-scoped handler is a type check returning 0 for pages
/// it doesn't own, and an app-wide handler scores on the input alone.
/// </summary>
public interface IQueryHandler
{
    /// <summary>Human-readable description used in LLM tool-selection prompts.</summary>
    string Description { get; }

    /// <summary>Optional single-character prefix that explicitly routes input to this handler.</summary>
    string? Symbol => null;

    /// <summary>
    /// Returns a confidence score (0–1) that this handler can process the given input.
    /// <paramref name="pageVm"/> is the active tab's ViewModel; use <c>pageVm?.GetContextObject()</c>
    /// for typed context.
    /// </summary>
    float CanProcess(string input, IPageViewModel? pageVm = null);

    /// <summary>
    /// Processes the input. Returns a response string to show in AI Chat,
    /// or null if the action was handled silently with no output.
    /// <paramref name="pageVm"/> carries the same context as passed to <see cref="CanProcess"/>.
    /// </summary>
    Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null);

    /// <summary>
    /// Optional background "code completion" for in-progress input. Returns the text
    /// to append after <paramref name="input"/> (the ghost-text remainder), or null
    /// for no suggestion. Only consulted for the clear-winner handler; must be fast
    /// and side-effect free. Default: no completion.
    /// </summary>
    Task<string?> CompleteAsync(string input, IPageViewModel? pageVm = null) => Task.FromResult<string?>(null);
}
