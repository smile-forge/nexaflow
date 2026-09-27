using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a gesture on a diagram comes to where the diagram answers it itself: opening a node, folding it away, choosing it.
/// The host is told of each through the hook it gave for it, and may take an opening on — writing the diagram again with more
/// of it walked, or opening the node in a tab of its own — rather than have it answered here.
/// </summary>
/// <param name="expand">The host's say in a node opened or folded away: true where it took it on.</param>
/// <param name="select">What is told which node was chosen.</param>
/// <param name="view">This diagram's own state, where whatever shows it keeps one per diagram.</param>
internal sealed class DiagramActions(Func<DiagramExpandRequest, bool>? expand, Action<DiagramSelection>? select, DiagramViewState? view)
    : ILayoutActions
{
    /// <summary>
    /// Where what the reader opens and folds is kept: the host's, where it keeps one, and this element's own where it
    /// does not.
    ///
    /// <para>
    /// A diagram in a plain markdown document has no host following it, and must still open when its chip is pressed —
    /// so there is always somewhere to write the opening down, even if it lives only as long as the element does.
    /// </para>
    /// </summary>
    public DiagramViewState View { get; } = view ?? new DiagramViewState();

    /// <summary>The element the diagram is shown in, once there is one: what a verb answered here redraws.</summary>
    public Editing.ContentElement? Shown { get; set; }

    /// <inheritdoc/>
    public bool Invoke(LayoutAct act) =>
        act.Intent.Verb switch
        {
            LayoutVerbs.Expand => Folded(act, open: true),
            LayoutVerbs.Collapse => Folded(act, open: false),
            LayoutVerbs.Select => Chose(act),
            _ => false,
        };

    /// <summary>
    /// Opens a node, or folds it away again.
    ///
    /// <para>
    /// The opening is written down before the host is offered anything, because a host that takes this on answers by
    /// re-emitting the whole diagram — and an opening made here has to survive that. Where nothing takes it on, the
    /// element lays itself out again, which reads the view state back and draws what is now shown.
    /// </para>
    /// </summary>
    private bool Folded(LayoutAct act, bool open)
    {
        if (act.Intent.Target is not { Length: > 0 } id) return true;

        var key = Folds(act).KeyFor(id);
        View.Expansion[key] = open;

        if (expand?.Invoke(new DiagramExpandRequest(id, key, act.Intent.Tip ?? id, open)) == true) return true;

        Shown?.Refresh();
        return true;
    }

    /// <summary>
    /// What the diagram pressed in is folded by: what its reading hung on it, found up the syntax tree from the piece
    /// pressed — the same the drawing was made with, so the opening is kept under the key the drawing looks it up by.
    /// </summary>
    private static NexaflowConfig Folds(LayoutAct act)
    {
        for (var piece = act.Piece; piece.Exists; piece = piece.Parent)
            if (piece.Part is ContentPart part && WithFolds.Holding(part) is { } folds) return folds;

        return NexaflowConfig.None;
    }

    /// <summary>
    /// Tells a host following the selection which node was picked. Never takes the press on: the piece is chosen
    /// either way, and what the host shows beside the diagram happens alongside that rather than instead of it.
    /// </summary>
    private bool Chose(LayoutAct act)
    {
        select?.Invoke(new DiagramSelection(act.Intent.Target, act.Intent.Target, act.Intent.Tip));
        return false;
    }
}
