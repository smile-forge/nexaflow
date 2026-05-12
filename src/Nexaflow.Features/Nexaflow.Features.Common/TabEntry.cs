using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Controls;

namespace Nexaflow.Features.Common;

/// <summary>
/// Represents a tab open in the tab strip.
/// </summary>
public partial class TabEntry : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _icon  = string.Empty;
    [ObservableProperty] private bool   _isActive;

    /// <summary>The breadcrumb segments shown when this tab is active.</summary>
    public List<BreadcrumbSegment> Breadcrumbs { get; set; } = [];

    /// <summary>The page kind string this tab was created for (e.g. "Projects", "ProjectDetail").</summary>
    public string? PageKind { get; set; }

    /// <summary>The page parameters this tab was created with.</summary>
    public Dictionary<string, string>? PageParams { get; set; }

    /// <summary>Factory for the page content UserControl.</summary>
    public Func<UserControl>? PageFactory { get; set; }

    /// <summary>Cached page instance (created on first activation).</summary>
    public UserControl? Page { get; private set; }

    public UserControl GetOrCreatePage()
    {
        Page ??= PageFactory?.Invoke() ?? new UserControl();
        return Page;
    }
}

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
