namespace Nexaflow.Features.Common;

/// <summary>
/// Describes an action a tab can perform, surfaced to the AI when no query handler matched.
/// </summary>
public record ActionDescriptor(
    string Name,
    string Description,
    IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>
/// Implemented by page ViewModels (or pages) to expose the current context and available
/// actions to the AI routing pipeline in <see cref="Nexaflow.Core.ViewModels.ShellViewModel"/>.
/// </summary>
public interface IContextProvider
{
    /// <summary>A short description of what the user is currently looking at.</summary>
    string GetContext();

    /// <summary>Actions this tab can perform, surfaced to the AI when no handler matches.</summary>
    IReadOnlyList<ActionDescriptor> GetAvailableActions();
}
