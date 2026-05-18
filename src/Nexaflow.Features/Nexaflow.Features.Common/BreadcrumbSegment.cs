namespace Nexaflow.Features.Common;

/// <summary>One crumb in the breadcrumb nav bar.</summary>
public class BreadcrumbSegment
{
    public string Label { get; set; } = string.Empty;
    /// <summary>Optional child items for the drop-down picker.</summary>
    public List<string> Children { get; set; } = [];
    /// <summary>Optional action invoked when the crumb is clicked (same-tab navigation).</summary>
    public Action? Navigate { get; set; }
    /// <summary>
    /// When set, clicking this crumb asks the shell to open (or focus) a tab of the
    /// given page kind instead of navigating within the current tab.
    /// </summary>
    public string? TargetPageKind { get; set; }
    /// <summary>Optional parameters passed to the tab factory when <see cref="TargetPageKind"/> is set.</summary>
    public Dictionary<string, string>? TargetPageParams { get; set; }
}
