namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Per-render options for <see cref="BlockRenderer"/>: the colour
/// <see cref="Palette"/> plus an optional link-navigation hook.
///
/// A <see cref="MarkdownPalette"/> converts implicitly to a context (with no
/// navigation hook), so existing palette-only callers are unaffected.
/// </summary>
public sealed class MarkdownRenderContext
{
    public required MarkdownPalette Palette { get; init; }

    /// <summary>
    /// Invoked when a link is clicked. Return <c>true</c> to indicate the link was
    /// handled (e.g. opened in an in-app tab); the renderer then skips its default
    /// behaviour of launching the OS browser. When null, links open externally.
    /// </summary>
    public Func<string, bool>? OnNavigate { get; init; }

    /// <summary>
    /// Invoked when a diagram node's expand chip is clicked. Return <c>true</c> to claim it — a host
    /// that generated the diagram answers by re-emitting it with more of the tree walked. Return
    /// false, or leave this null, and the diagram opens the node itself from what it already holds.
    /// </summary>
    public Func<DiagramExpandRequest, bool>? OnDiagramExpand { get; init; }

    /// <summary>Called when a diagram's selected node changes, for a host showing detail beside it.</summary>
    public Action<DiagramSelection>? OnDiagramSelect { get; init; }

    /// <summary>In a diagram, a single click on a node selects it and a double-click opens it. For a
    /// pane where opening a node costs something (a whole new tab). See
    /// <see cref="DiagramRenderOptions.OpenOnDoubleClick"/>.</summary>
    public bool DiagramOpenOnDoubleClick { get; init; }

    /// <summary>A plain wheel over a diagram zooms it instead of scrolling this surface. Only for a
    /// pane whose whole content is the diagram. See <see cref="DiagramRenderOptions.ZoomOnWheel"/>.</summary>
    public bool DiagramZoomOnWheel { get; init; }

    /// <summary>Height a diagram may take before it becomes a window onto itself. Zero uses the
    /// default; a pane that is entirely one diagram passes its own height so the diagram fills it
    /// rather than overflowing past the bottom of the panel.</summary>
    public double MaxDiagramHeight { get; init; }

    /// <summary>
    /// Where a rendered sub-block — a formula, a score, a diagram — sits across the column.
    /// <para>
    /// Centred is right for a document, where set-piece content is something the prose steps around.
    /// It is wrong for a pane whose whole content <em>is</em> that sub-block: then it is an input
    /// field, and what is in a field starts where every other field's content starts. One setting for
    /// all of them rather than one per kind, because it is a fact about the surface, not about what
    /// happens to be on it — a formula and a score in the same pane centring differently would be a
    /// bug however each was reached.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Null leaves each kind where it normally sits — a formula centred, a diagram filling the column —
    /// which is what a document wants. Setting it overrides the lot.
    /// </remarks>
    public System.Windows.HorizontalAlignment? SubblockAlignment { get; init; }

    /// <summary>
    /// Where each diagram's expansion, selection and pan/zoom are kept between renders. Null means
    /// they are not kept — fine for a surface that renders once, wrong for one whose host re-emits
    /// the markdown to answer an expand.
    /// </summary>
    public DiagramViewStates? DiagramStates { get; init; }

    /// <summary>
    /// Optional base directory used to resolve relative <c>![](file.png)</c> image paths to a
    /// local file (e.g. a post-it's attachment folder). Absolute paths and <c>file:</c> URIs
    /// resolve without it; remote <c>http(s)</c> images are never loaded and render as text.
    /// </summary>
    public string? BaseDirectory { get; init; }

    /// <summary>
    /// When true, a diagram wider/taller than the available width is scaled down (uniformly) to fit
    /// rather than getting its own scrollbars. Set by the inline editor: scrollbars inside an editable
    /// surface fight text selection (you can't grab the thumb), so the diagram fits the column instead.
    /// </summary>
    public bool FitContentToWidth { get; init; }

    /// <summary>
    /// When true (with <see cref="FitContentToWidth"/>), a too-wide diagram keeps its natural size and gets a
    /// horizontal scrollbar instead of being scaled down — readable at full size. Only sensible on a read-only
    /// selectable surface (the "As Code" panel) where the scrollbar thumb can actually be grabbed; an editable
    /// surface leaves this off and scales instead. Vertical overflow still flows to the host (full height).
    /// </summary>
    public bool ScrollWideDiagrams { get; init; }

    public static readonly MarkdownRenderContext Dark = MarkdownPalette.Dark;

    public static implicit operator MarkdownRenderContext(MarkdownPalette palette)
        => new() { Palette = palette };
}
