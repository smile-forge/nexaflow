using System;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A node's expand chip was clicked.
/// <para>
/// This is the counterpart of the link hook rather than a variation on it: a link says "go here",
/// an expansion says "there is more behind this one — show it". Smuggling the second through the
/// first (a private scheme on the href) makes a node with both actions impossible, because a
/// rendered node then has one gesture to spend on two meanings.
/// </para>
/// </summary>
/// <param name="NodeId">The node's id in the diagram source.</param>
/// <param name="Key">The producer's own name for it, from the front-matter, else the id. What a host
/// that generated the diagram wants back — it thinks in module names, not in <c>n7</c>.</param>
/// <param name="Label">The node's rendered label, for a message or a prompt.</param>
/// <param name="Expand">True to open the node, false to close it again.</param>
public readonly record struct DiagramExpandRequest(string NodeId, string Key, string Label, bool Expand);

/// <summary>
/// The selected node changed. <paramref name="Key"/> is null when the selection was dropped.
/// <para>
/// Selection is the diagram's own state — it draws the node and its edges differently — but a host
/// can follow it to show detail beside the diagram, which is what turns "this node is here" into
/// "and this is why".
/// </para>
/// </summary>
public readonly record struct DiagramSelection(string? NodeId, string? Key, string? Label);

/// <summary>
/// Everything a diagram needs from its host for one render. Grouped rather than passed as a widening
/// parameter list, because every new hook otherwise ripples through the dispatcher and both handlers.
/// </summary>
public sealed class DiagramRenderOptions
{
    public required MarkdownPalette Palette { get; init; }

    /// <summary>
    /// Where the source handed to the renderer sits inside the markdown block it was taken from.
    /// <para>
    /// A fenced block arrives here as its content — the fence lines stripped and the ends trimmed — so
    /// an offset into it is not an offset into the block an editing host would splice back into. Only
    /// the caller knows the difference, and a renderer whose element is editable adds this to whatever
    /// it reports as its own <see cref="Editing.IEditableBlock.SourceStart"/>. Zero for a renderer that
    /// was handed the block whole, which is why it is safe to ignore.
    /// </para>
    /// </summary>
    public int SourceOffset { get; init; }

    /// <summary>In-app click handler for a node / class member carrying an <c>href</c>. Return true
    /// when handled; null means the host has no handler and no link affordance is drawn.</summary>
    public Func<string, bool>? OnNavigate { get; init; }

    /// <summary>
    /// Expand/collapse handler. Return true when the host took it on (it will re-emit the diagram);
    /// return false, or leave it null, and the diagram opens the node itself.
    /// </summary>
    public Func<DiagramExpandRequest, bool>? OnExpand { get; init; }

    /// <summary>Called when the selected node changes, for a host showing detail beside the diagram.</summary>
    public Action<DiagramSelection>? OnSelect { get; init; }

    /// <summary>The surface scales an over-wide diagram down to its column instead of giving it a
    /// viewport of its own. Set by editable surfaces, where scrollbars and pan gestures both fight
    /// text selection.</summary>
    public bool FitToWidth { get; init; }

    /// <summary>
    /// A single click on a node only selects it; opening it takes a double-click. For surfaces where
    /// opening costs something the user may not have meant — the PE inspector spawns a whole tab —
    /// so a single click is left free for looking at a node and its edges.
    /// </summary>
    public bool OpenOnDoubleClick { get; init; }

    /// <summary>
    /// A plain mouse wheel zooms the diagram rather than scrolling the page past it. For a pane whose
    /// whole content is the diagram; a diagram sitting in a flowing document leaves this off, or the
    /// page could never be scrolled past one.
    /// </summary>
    public bool ZoomOnWheel { get; init; }

    /// <summary>Height the diagram may take before it becomes a window onto itself. Zero uses the
    /// default cap.</summary>
    public double MaxHeight { get; init; }

    /// <summary>Where this diagram's expansion, selection and pan/zoom survive between renders.
    /// Null means they do not.</summary>
    public DiagramViewState? ViewState { get; init; }

    public static DiagramRenderOptions For(MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => new() { Palette = palette, OnNavigate = onNavigate };
}
