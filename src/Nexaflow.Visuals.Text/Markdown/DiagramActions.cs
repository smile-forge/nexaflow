using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a gesture on a diagram comes to: the host first, then the verbs the renderer knows how to answer itself.
///
/// <para>
/// The host is offered everything before anything is resolved, so it can take on a verb that would otherwise be
/// answered here — opening a node in a tab of its own rather than in place. What it declines falls to the typed hooks
/// on <see cref="DiagramRenderOptions"/>, which is why a host wired up before any of this existed goes on working
/// unchanged.
/// </para>
/// </summary>
/// <param name="source">The block, for the front matter — which is where a node's own name for the host is written.</param>
/// <param name="view">This diagram's own state, where whatever shows it keeps one per diagram.</param>
internal sealed class DiagramActions(DiagramRenderOptions options, string source, DiagramViewState? view = null) : ILayoutActions
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
    public DiagramViewState View { get; } = view ?? options.ViewState ?? new DiagramViewState();

    /// <summary>The element the diagram is shown in, once there is one: what a verb answered here redraws.</summary>
    public Editing.ContentElement? Shown { get; set; }

    /// <inheritdoc/>
    public bool Invoke(LayoutAct act)
    {
        if (options.OnAction?.Invoke(act) == true) return true;

        return act.Intent.Verb switch
        {
            LayoutVerbs.Navigate => act.Intent.Target is { Length: > 0 } href && options.OnNavigate?.Invoke(href) == true,
            LayoutVerbs.Expand => Folded(act, open: true),
            LayoutVerbs.Collapse => Folded(act, open: false),
            LayoutVerbs.Select => Chose(act),
            _ => false,
        };
    }

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

        var key = NexaflowConfig.Read(MermaidBlock.Read(source).Config).KeyFor(id);
        View.Expansion[key] = open;

        if (options.OnExpand?.Invoke(new DiagramExpandRequest(id, key, act.Intent.Tip ?? id, open)) == true) return true;

        Shown?.Refresh();
        return true;
    }

    /// <summary>
    /// Tells a host following the selection which node was picked. Never takes the press on: the piece is chosen
    /// either way, and what the host shows beside the diagram happens alongside that rather than instead of it.
    /// </summary>
    private bool Chose(LayoutAct act)
    {
        options.OnSelect?.Invoke(new DiagramSelection(act.Intent.Target, act.Intent.Target, act.Intent.Tip));
        return false;
    }
}
