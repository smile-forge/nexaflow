using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;


namespace Nexaflow.Features.Common;

/// <summary>
/// A page open in the shell: title + breadcrumb + its lazily made view-model, plus the
/// page kind and params used to recreate or reidentify it. How it is drawn is the UI layer's. Pages mutate their own
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

    /// <summary>The kind a tab being dragged is carried as: what a drop target that takes tabs names.</summary>
    public static string DragKind { get; } = typeof(Page).FullName!;

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

    /// <summary>
    /// Makes the page's view-model: everything the page is, with nothing of how it is drawn. The UI layer finds the view
    /// that draws it (one marked as drawing its type), so the same page can be drawn by another toolkit's view of it.
    /// </summary>
    public Func<IPageViewModel>? ViewModelFactory { get; set; }

    private IPageViewModel? _viewModel;

    /// <summary>The page's view-model once made: null before the page is first needed, or when making it failed.</summary>
    public IPageViewModel? ViewModel => _viewModel;

    /// <summary>
    /// Makes the page's view-model on first use and keeps it. Never throws: a failing factory is recorded on
    /// <see cref="LoadException"/> and null returned, and is not run again.
    /// </summary>
    public IPageViewModel? GetOrCreateViewModel()
    {
        if (_viewModel is not null || LoadException is not null || ViewModelFactory is null) return _viewModel;

        try
        {
            _viewModel = ViewModelFactory();
        }
        catch (Exception ex)
        {
            LoadException = ex;
        }
        return _viewModel;
    }

    /// <summary>
    /// Hands the page the parameters it is being shown with again (see <see cref="IPageViewModel.Reinitialize"/>). Only a page
    /// already made hears it: one made later is made with them.
    /// </summary>
    public void Reinitialize(Dictionary<string, string> pageParams) => _viewModel?.Reinitialize(pageParams);

    /// <summary>
    /// Set when <see cref="ViewModelFactory"/> threw, and null while the page is fine. The shell renders its
    /// own explanatory surface for a page in this state — see Core's <c>PageLoadErrorView</c>.
    /// <para>
    /// Making a page is the last unguarded step of opening a tab, and it is a rich place to fail: a missing
    /// native dependency, a bad path reaching a <see cref="Uri"/>. Left to propagate it escapes through tab activation to the app-level dispatcher
    /// handler, which can only say "something went wrong" — the failure loses the one piece of context
    /// (which page, and why) that would let anyone act on it. Capturing it here keeps it attached to the
    /// page it belongs to.
    /// </para>
    /// </summary>
    public Exception? LoadException { get; private set; }

    /// <summary>Raised when the page is permanently closed (not merely deactivated).</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Closes the page for good: tells whoever listens, then disposes its view-model where it holds anything. A page in a
    /// tab and a page pinned only as a conversation's context close the same way, so neither leaves a view-model running.
    /// </summary>
    public void RaiseClosed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
        if (_viewModel is not IDisposable disposable) return;
        try { disposable.Dispose(); }
        catch { /* a view-model failing to let go must not keep its page from closing */ }
    }
}
