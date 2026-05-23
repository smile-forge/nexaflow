using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// A pane = the strip of pages on one side of the shell plus the currently active page.
/// The TabStrip and BreadcrumbBar are views onto a Pane; the Pane is not aware of them.
/// One Pane per window today; the future SplitPaneNode wraps two.
/// </summary>
public partial class Pane : ObservableObject, IPaneNode
{
    public ObservableCollection<Page> Pages { get; } = [];

    [ObservableProperty] private Page? _activePage;

    partial void OnActivePageChanged(Page? oldValue, Page? newValue)
    {
        foreach (var p in Pages)
            p.IsActive = (p == newValue);
    }

    /// <summary>Inserts the page at the front and makes it active.</summary>
    public void Add(Page page)
    {
        Pages.Insert(0, page);
        ActivePage = page;
    }

    /// <summary>Removes the page; if it was active, activates the adjacent page (or null when empty).</summary>
    public void Remove(Page page)
    {
        int idx = Pages.IndexOf(page);
        if (idx < 0) return;
        bool wasActive = page == ActivePage;
        Pages.Remove(page);
        if (wasActive)
            ActivePage = Pages.Count > 0 ? Pages[Math.Min(idx, Pages.Count - 1)] : null;
    }

    /// <summary>Moves the page to position 0 without changing the active page.</summary>
    public void BringToFront(Page page)
    {
        int idx = Pages.IndexOf(page);
        if (idx > 0) Pages.Move(idx, 0);
    }

    /// <summary>Reorders the page to <paramref name="newIndex"/> (clamped).</summary>
    public void Move(Page page, int newIndex)
    {
        int idx = Pages.IndexOf(page);
        if (idx < 0 || idx == newIndex) return;
        if (newIndex < 0) newIndex = 0;
        if (newIndex >= Pages.Count) newIndex = Pages.Count - 1;
        Pages.Move(idx, newIndex);
    }
}
