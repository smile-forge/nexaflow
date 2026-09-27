using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a gesture on a diagram comes to where the diagram answers it itself: opening a node, or folding it away. The host may
/// take an opening on — writing the diagram again with more of it walked — rather than have it answered here.
/// </summary>
/// <param name="expand">The host's say in a node opened or folded away: true where it took it on.</param>
/// <param name="view">This diagram's own state, where whatever shows it keeps one per diagram.</param>
internal sealed class DiagramActions(Func<string, bool, bool>? expand, DiagramViewState? view) : ILayoutActions
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
            _ => false,
        };

    /// <summary>
    /// Opens a node, or folds it away again.
    ///
    /// <para>
    /// The opening is written down before bound content is told, because what that content supplies is read into the
    /// diagram again when it has walked — and an opening made here has to survive that. Where no bound content takes it on,
    /// the element lays itself out again, which reads the view state back and draws what is now shown.
    /// </para>
    /// </summary>
    private bool Folded(LayoutAct act, bool open)
    {
        if (act.Intent.Target is not { Length: > 0 } id) return true;

        var key = Folds(act).KeyFor(id);
        View.Expansion[key] = open;

        if (expand?.Invoke(key, open) == true) return true;

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
}
