using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Help;

/// <summary>
/// What <see cref="HelpPaneController"/> needs of a window: its panes, and the few moves that open, show and close a
/// tab in a particular one. Implemented by <see cref="ShellViewModel"/>; faked over real <see cref="Pane"/>s in tests.
/// </summary>
internal interface IHelpPaneHost
{
    /// <summary>The window's panes — one, or two side by side (left first).</summary>
    IReadOnlyList<Pane> LeafPanes { get; }

    /// <summary>The pane the reader last worked in.</summary>
    Pane FocusedPane { get; }

    /// <summary>Splits an unsplit window, keeping <paramref name="subject"/> on the left; returns the new right pane.</summary>
    Pane SplitBeside(Pane subject);

    /// <summary>Opens a fresh <paramref name="pageKind"/> tab in <paramref name="pane"/> and shows it.</summary>
    void OpenInPane(Pane pane, string pageKind, Dictionary<string, string> pageParams);

    /// <summary>Makes <paramref name="page"/> the shown tab of its pane.</summary>
    void Activate(Page page);

    /// <summary>Closes <paramref name="page"/>; a pane it leaves empty collapses the split.</summary>
    void Close(Page page);
}
