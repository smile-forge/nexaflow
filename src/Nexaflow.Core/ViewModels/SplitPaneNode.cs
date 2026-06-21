using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// A side-by-side split of two <see cref="Pane"/>s — the window's root content when the user has
/// split the tab area in two. v1 is a single, vertical (left/right) split: a window root is either a
/// lone <see cref="Pane"/> or one <see cref="SplitPaneNode"/> wrapping two panes (no nesting).
/// </summary>
public partial class SplitPaneNode : ObservableObject, IPaneNode
{
    public Pane First  { get; }
    public Pane Second { get; }

    public SplitPaneNode(Pane first, Pane second)
    {
        First  = first;
        Second = second;
    }
}
