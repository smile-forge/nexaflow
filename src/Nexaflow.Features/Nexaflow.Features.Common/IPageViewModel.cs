namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by page ViewModels to expose context and actions to the AI pipeline.
/// </summary>
public interface IPageViewModel
{
    /// <summary>Short description of what the user is currently looking at, for the LLM.</summary>
    string GetContext();

    /// <summary>Actions this tab can perform, surfaced to the AI when no handler matches.</summary>
    IReadOnlyList<ActionDescriptor> GetAvailableActions();

    /// <summary>
    /// Optional strongly-typed context for query handlers. Default returns null.
    /// Override to let handlers gate on and extract structured data
    /// (e.g. <see cref="FileSystemContext"/>).
    /// </summary>
    IContext? GetContextObject() => null;

    /// <summary>Execute an AI-selected action on this page. Default is a no-op.</summary>
    void Execute(ActionDescriptor action) { }
}
