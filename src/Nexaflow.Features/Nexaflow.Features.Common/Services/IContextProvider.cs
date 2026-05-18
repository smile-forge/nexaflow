namespace Nexaflow.Features.Common;

/// <summary>
/// Describes an action a tab can perform, surfaced to the AI when no query handler matched.
/// </summary>
public record ActionDescriptor(
    string Name,
    string Description,
    IReadOnlyDictionary<string, string>? Parameters = null);

