namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by tab <see cref="System.Windows.Controls.UserControl"/> views to expose
/// their ViewModel and page context to the AI routing pipeline in the shell.
///
/// Replaces <see cref="IContextProvider"/> (which was incorrectly placed on ViewModels).
/// <see cref="IContextProvider"/> is kept for backward compatibility but new views
/// should implement this interface instead.
/// </summary>
public interface IPageView
{
    /// <summary>The view's primary ViewModel (same object as DataContext).</summary>
    object? ViewModel { get; }

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
}
