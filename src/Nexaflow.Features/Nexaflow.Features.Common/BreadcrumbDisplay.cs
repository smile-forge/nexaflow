namespace Nexaflow.Features.Common;

/// <summary>
/// How one crumb reads on screen, built by the shell when it draws the bar: first as the page stated the crumb
/// (<see cref="Of"/>), then restated by each <see cref="IBreadcrumbDisplayFilter"/> in turn, and last shortened by the
/// shell to fit — by eliding middle <see cref="Parts"/> where there are more than two, else by letters.
/// </summary>
public sealed class BreadcrumbDisplay
{
    /// <summary>The label's parts, first to last (<c>D:</c>, <c>temp</c>, <c>sub</c>); a single part when the label
    /// does not divide.</summary>
    public IReadOnlyList<string> Parts { get; set; } = [];

    /// <summary>What joins <see cref="Parts"/> into the label (<c>\</c>, <c>/</c>).</summary>
    public string Separator { get; set; } = string.Empty;

    /// <summary>What <b>Copy path</b> copies; null when the crumb names nothing to copy.</summary>
    public string? CopyText { get; set; }

    /// <summary>Shown on hover, so a shortened crumb is still legible.</summary>
    public string ToolTip { get; set; } = string.Empty;

    /// <summary>The label whole: <see cref="Parts"/> joined by <see cref="Separator"/>.</summary>
    public string Text => string.Join(Separator, Parts);

    /// <summary>The crumb as its page stated it: its label, its path to copy, the label on hover.</summary>
    public static BreadcrumbDisplay Of(BreadcrumbSegment crumb) => new()
    {
        Parts    = [crumb.Label],
        CopyText = string.IsNullOrEmpty(crumb.Path) ? null : crumb.Path,
        ToolTip  = crumb.Label,
    };
}
