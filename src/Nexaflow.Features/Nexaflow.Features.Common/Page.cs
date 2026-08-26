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

    /// <summary>
    /// The <see cref="PageParams"/> keys this page kind declared as non-identity
    /// (<see cref="PageParameter.Identity"/> false) — where the page is looking, not which document
    /// it is. Stamped by the shell from the registration, like <see cref="PageKind"/>; a feature
    /// never sets it. Tab dedup ignores these keys, so a re-open that only moves the location
    /// re-points this page instead of creating a second one. Null means every key is identity.
    /// </summary>
    public IReadOnlySet<string>? LocationParams { get; set; }

    /// <summary>Factory for the page's content UserControl.</summary>
    public Func<UserControl>? ContentFactory { get; set; }

    /// <summary>Cached content instance (created on first activation).</summary>
    public UserControl? Content { get; private set; }

    /// <summary>
    /// Set when <see cref="ContentFactory"/> threw, and null while the page is fine. The shell renders its
    /// own explanatory surface for a page in this state — see Core's <c>PageLoadErrorView</c>.
    /// <para>
    /// A feature's view constructor is the last unguarded step of opening a tab, and it is a rich place to
    /// fail: XAML parsing, a missing native dependency behind a hosted control, a bad path reaching a
    /// <see cref="Uri"/>. Left to propagate it escapes through tab activation to the app-level dispatcher
    /// handler, which can only say "something went wrong" — the failure loses the one piece of context
    /// (which page, and why) that would let anyone act on it. Capturing it here keeps it attached to the
    /// page it belongs to.
    /// </para>
    /// </summary>
    public Exception? LoadException { get; private set; }

    /// <summary>
    /// Builds the page's content on first use and caches it. Never throws: a failing factory is recorded on
    /// <see cref="LoadException"/> and an empty placeholder is returned, so one broken page cannot take
    /// down the tab activation that opened it. The failure is not retried — a second call returns the same
    /// placeholder rather than re-running a factory that has already proven it throws.
    /// </summary>
    public UserControl GetOrCreateContent()
    {
        if (Content is not null) return Content;

        try
        {
            Content = ContentFactory?.Invoke() ?? new UserControl();
        }
        catch (Exception ex)
        {
            LoadException = ex;
            Content       = new UserControl();
        }
        return Content;
    }

    /// <summary>
    /// Swaps in content the shell built on the page's behalf, and caches it like any other content.
    /// <para>
    /// Exists for exactly one case: a page whose <see cref="LoadException"/> is set needs a replacement
    /// surface explaining the failure, and only the shell can build one (it is the side that knows how to
    /// render, and <c>Features.Common</c> cannot reference Core). The page still owns the caching, so
    /// switching tabs away and back doesn't rebuild it.
    /// </para>
    /// </summary>
    public UserControl ReplaceContent(UserControl replacement)
    {
        Content = replacement;
        return replacement;
    }

    /// <summary>Raised when the page is permanently closed (not merely deactivated).</summary>
    public event EventHandler? Closed;

    public void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);
}
