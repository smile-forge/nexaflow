namespace Nexaflow.Features.Common;

/// <summary>
/// Restates how a crumb reads, for the content it knows about. The shell draws every crumb through the active
/// runtime's filters, stacked in <see cref="Order"/>, each modifying only what it knows and leaving the rest as the
/// filters before it left it; the shell shortens what they produce to fit. Discovered by reflection and built per
/// runtime with constructor injection, like the AI-bar handlers.
/// <para>A filter is called on the UI thread for every crumb the bar draws, so it answers from what the runtime
/// already holds — no IO, nothing awaited.</para>
/// </summary>
public interface IBreadcrumbDisplayFilter
{
    /// <summary>Where it stands in the stack: lower runs first, so a filter that refines another's output runs after it.</summary>
    int Order { get; }

    /// <summary>Restates <paramref name="display"/> when <paramref name="crumb"/>'s content is something this filter
    /// knows (<see cref="BreadcrumbSegment.Content"/>); otherwise leaves it untouched.</summary>
    void Apply(BreadcrumbSegment crumb, BreadcrumbDisplay display);
}
