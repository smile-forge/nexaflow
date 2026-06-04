using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Controls;

namespace Nexaflow.Features.Common;

/// <summary>
/// A page open in the shell: title + breadcrumb + lazy content UserControl, plus the
/// page kind and params used to recreate or reidentify it. Pages mutate their own
/// observable surface directly (Title, Icon, Breadcrumbs) — the tab strip and breadcrumb
/// bar are views onto these properties, not their owners.
/// </summary>
public partial class Page : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _icon  = string.Empty;
    [ObservableProperty] private bool   _isActive;

    /// <summary>
    /// Advisory security risk of letting the AI act within this page, shown as a badge when the page is
    /// pinned as AI context. A display carrier only — kept current by whoever pins the page (it mirrors
    /// the page view-model's <see cref="IPageViewModel.GetContextSecurityRisk"/>). Default: Low.
    /// </summary>
    [ObservableProperty] private ContextSecurityRisk _securityRisk;

    /// <summary>Breadcrumb segments shown when this page is active. Stable identity — mutate in place.</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    /// <summary>The page kind string this page was created for (e.g. "Projects", "ProjectDetail").</summary>
    public string? PageKind { get; set; }

    /// <summary>The parameters this page was created with (kept in sync with display state).</summary>
    public Dictionary<string, string>? PageParams { get; set; }

    /// <summary>Factory for the page's content UserControl.</summary>
    public Func<UserControl>? ContentFactory { get; set; }

    /// <summary>Cached content instance (created on first activation).</summary>
    public UserControl? Content { get; private set; }

    public UserControl GetOrCreateContent()
    {
        Content ??= ContentFactory?.Invoke() ?? new UserControl();
        return Content;
    }

    /// <summary>Raised when the page is permanently closed (not merely deactivated).</summary>
    public event EventHandler? Closed;

    public void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);
}
