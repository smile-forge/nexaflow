namespace Nexaflow.Features.Common.Dependencies;

/// <summary>
/// One row of the machine's third-party component picture: what was declared, what the probe found,
/// and which features asked for it. Built by Core's registry from every discovered
/// <see cref="IExternalDependency"/>, keyed on <see cref="IExternalDependency.Id"/> so a component two
/// features both need appears once, naming both.
/// </summary>
/// <param name="Id">The component's stable id.</param>
/// <param name="DisplayName">Name as the user would recognise it.</param>
/// <param name="Description">What stops working without it.</param>
/// <param name="Kind">The strongest kind declared — <c>Required</c> wins over <c>Optional</c>.</param>
/// <param name="InstallUrl">Where to get it, when offered.</param>
/// <param name="Status">What the probe found.</param>
/// <param name="RequiredBy">Display names of the features that declared it, sorted.</param>
public sealed record ExternalDependencyReport(
    string Id,
    string DisplayName,
    string Description,
    ExternalDependencyKind Kind,
    string? InstallUrl,
    ExternalDependencyStatus Status,
    IReadOnlyList<string> RequiredBy)
{
    /// <summary>True when this is a <c>Required</c> component the probe could not find.</summary>
    public bool IsBlocking
        => Kind == ExternalDependencyKind.Required && Status.State == ExternalDependencyState.Missing;

    /// <summary>"needed by PDF, Web" — the features that declared it, for display.</summary>
    public string RequiredByLabel => string.Join(", ", RequiredBy);
}
