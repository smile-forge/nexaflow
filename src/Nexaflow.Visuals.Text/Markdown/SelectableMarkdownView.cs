using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Common.Search;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Read-only, drag-selectable markdown: a whole document on one <see cref="MarkdownSurface"/>.
///
/// <para>
/// Everything in it — the prose, the diagrams, the tunes, the formulas — is pieces of a single laid tree,
/// so a drag runs from a word into a chart without anything forwarding gestures between controls, and a
/// search looks in one place.
/// </para>
/// <para>
/// <strong>It says what things mean and the host does them.</strong> Copying and following a link out of
/// the document go out as verbs, because a clipboard and a browser are the application's. A link *into* the
/// document is answered here, since nobody else can: the heading is on this page.
/// </para>
/// </summary>
public class SelectableMarkdownView : UserControl, ILayoutActions
{
    private readonly MarkdownSurface _surface;

    public SelectableMarkdownView()
    {
        _surface = new MarkdownSurface { Host = this };

        Background = Brushes.Transparent;
        Content = _surface;
    }

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(SelectableMarkdownView),
            new PropertyMetadata(string.Empty, (view, _) => ((SelectableMarkdownView)view).Rebuild()));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Colour scheme for rendering. When unset, follows the active theme via
    /// <see cref="StyleFormat.FromTheme"/> (light themes get dark text, etc.); set
    /// <see cref="StyleFormat.Light"/> for fixed light surfaces (e.g. scratchpad post-its).</summary>
    public static readonly DependencyProperty PaletteProperty =
        DependencyProperty.Register(nameof(Palette), typeof(StyleFormat), typeof(SelectableMarkdownView),
            new PropertyMetadata(null, (view, _) => ((SelectableMarkdownView)view).Rebuild()));

    public StyleFormat? Palette
    {
        get => (StyleFormat?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>In-app link handler. Return true to mark the link handled (the
    /// renderer then skips opening the OS browser). When null, links open externally.</summary>
    public Func<string, bool>? LinkNavigate { get; set; }

    /// <summary>
    /// Asked to put something where things are copied to — the markdown of whatever was chosen.
    ///
    /// <para>
    /// A clipboard is the application's, shared with every other thing in the window and subject to
    /// whatever the host has to say about what may leave it. This says what would go on one; it does not
    /// reach out and put it there.
    /// </para>
    /// </summary>
    public Action<string>? CopyRequested { get; set; }

    /// <summary>
    /// A diagram node's expand chip was clicked. Return true to claim it — a host that generated the
    /// diagram re-emits <see cref="Markdown"/> with more of the tree walked. Left null, the diagram
    /// opens the node itself from what its source already describes.
    /// </summary>
    public Func<DiagramExpandRequest, bool>? DiagramExpand { get; set; }

    /// <summary>A diagram's selected node changed — for a host showing detail beside the diagram.
    /// The key is null when the selection was dropped.</summary>
    public Action<DiagramSelection>? DiagramSelect { get; set; }

    /// <summary>
    /// What a <c>{{…}}</c> written in a diagram is read against. Null leaves one drawn as it was written.
    /// </summary>
    public Nexaflow.Markdown.Binding.IDataContext? DiagramData { get; set; }

    /// <summary>
    /// Lays everything out again — what a host calls once what <see cref="DiagramData"/> holds has changed.
    /// The document itself is untouched, so nothing is re-parsed and nothing scrolls.
    /// </summary>
    public void RefreshDiagrams() => _surface.Shown.Refresh();

    /// <summary>In a diagram, a single click selects a node and a double-click opens it. Set it on a
    /// pane where opening a node costs something the user may not have meant.</summary>
    public bool DiagramOpenOnDoubleClick { get; set; }

    /// <summary>
    /// A plain wheel over a diagram zooms it rather than scrolling this surface.
    ///
    /// <para>
    /// <strong>No longer has an effect.</strong> It meant a diagram with a scroller of its own inside the
    /// document, and there is no longer one: a document is a single element and the wheel scrolls the
    /// surface it is on. A diagram is zoomed from its own chrome instead.
    /// </para>
    /// </summary>
    public bool DiagramZoomOnWheel { get; set; }

    /// <summary>
    /// Height a diagram may take, for a pane that is entirely one diagram: bind it to the pane and
    /// the diagram fills it instead of running past the bottom. Zero (the default) uses the built-in cap.
    /// </summary>
    public static readonly DependencyProperty MaxDiagramHeightProperty =
        DependencyProperty.Register(nameof(MaxDiagramHeight), typeof(double), typeof(SelectableMarkdownView),
            new PropertyMetadata(0.0, OnMaxDiagramHeightChanged));

    public double MaxDiagramHeight
    {
        get => (double)GetValue(MaxDiagramHeightProperty);
        set => SetValue(MaxDiagramHeightProperty, value);
    }

    // A pane resize walks this through every intermediate pixel; rebuilding on each would be absurd, and a
    // diagram does not care about a few pixels either way.
    private static void OnMaxDiagramHeightChanged(DependencyObject view, DependencyPropertyChangedEventArgs args)
    {
        if (Math.Abs((double)args.NewValue - (double)args.OldValue) >= 24) ((SelectableMarkdownView)view).Rebuild();
    }

    /// <summary>Where each diagram's expansion, selection and pan/zoom live between renders — on the
    /// view rather than on the rendered element, which a host-driven re-emit replaces.</summary>
    private readonly DiagramViewStates _diagramStates = new();

    /// <summary>
    /// Forgets what the reader had opened, selected and zoomed to in every diagram here, so the next
    /// render starts fitted.
    /// </summary>
    public void ResetDiagramViews() => _diagramStates.Clear();

    /// <summary>Base directory for resolving relative <c>![](file.png)</c> image paths to a local
    /// file. When null, only absolute/<c>file:</c> images render (remote images stay text).</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>Host hook for image sources, asked before <see cref="BaseDirectory"/>.</summary>
    public Func<string, ImageSource?>? ImageResolver { get; set; }

    /// <summary>
    /// The host's say in how a link looks, asked for every link with the URL as written and the words it
    /// was written as — which is what lets the help pane mark a <c>locate:</c> link without disturbing
    /// those words. Read at render time, so set it before <see cref="Markdown"/>.
    /// </summary>
    public Func<string, string, LinkLook?>? LinkDecorator { get; set; }

    /// <summary>When true, a diagram scales down to the control width rather than being cut off.</summary>
    public bool FitContentToWidth { get; set; }

    /// <summary>
    /// A too-wide diagram keeps its natural size and gets a scrollbar of its own.
    ///
    /// <para>
    /// <strong>No longer has an effect.</strong> A document is a single element with one scroller, which
    /// is the one that moves; a diagram inside it is fitted to the room it is given.
    /// </para>
    /// </summary>
    public bool ScrollWideDiagrams { get; set; }

    /// <summary>What the host said about the content written inside the document, gathered once per render.</summary>
    private DiagramRenderOptions Options() => new()
    {
        Palette = Palette ?? StyleFormat.FromTheme(),
        ReadOnly = true,
        OnNavigate = OpenLink,
        OnExpand = DiagramExpand,
        OnSelect = DiagramSelect,
        DataContext = DiagramData,
        Pictures = MarkdownPictures.Found(ImageResolver, BaseDirectory),
        Links = LinkDecorator,
        FitToWidth = FitContentToWidth,
        OpenOnDoubleClick = DiagramOpenOnDoubleClick,
        MaxHeight = MaxDiagramHeight,
    };

    private void Rebuild()
    {
        _diagramStates.Rewind();

        _surface.Palette = Palette ?? StyleFormat.FromTheme();
        _surface.Options = Options();
        _surface.Markdown = Markdown ?? string.Empty;
    }

    /// <summary>Scrolls the heading with in-page anchor <paramref name="anchor"/> — a <c>#anchor</c> link's target,
    /// without the hash — into view; false when the document has no such heading.</summary>
    public bool ScrollToAnchor(string anchor) =>
        _surface.GoTo(Nexaflow.Markdown.Ast.ContentPath.Read($"{Nexaflow.Markdown.Prose.MarkdownKinds.Heading}:{anchor}"));

    // A link into this document is answered by the element itself and never reaches here. Anything else is
    // the host's (LinkNavigate), then the browser's.
    private bool OpenLink(string url) => LinkNavigate?.Invoke(url) ?? false;

    // ── What the document asks of its host ──────────────────────────────────

    /// <inheritdoc/>
    bool ILayoutActions.Invoke(LayoutAct act)
    {
        switch (act.Intent.Verb)
        {
            case LayoutVerbs.Navigate when act.Intent.Target is { Length: > 0 } where:
                return OpenLink(where);

            case LayoutVerbs.Copy when act.Intent.Target is { } what:
                CopyRequested?.Invoke(what);

                return CopyRequested is not null;

            default:
                return false;
        }
    }

    /// <inheritdoc/>
    IReadOnlyList<LayoutIntent> ILayoutActions.Menu(LayoutAct act) => [];

    // ── Search (rendered text) ──────────────────────────────────────────────

    /// <summary>Highlights every match of <paramref name="matcher"/> and focuses the first. Returns the
    /// matches so the caller can report a count or hand ids to the model.</summary>
    public IReadOnlyList<RenderedMatch> FindInRendered(TextSearchMatcher matcher)
    {
        _surface.Find(matcher.Occurrences);

        return [.. Enumerable.Range(0, _surface.Found.Count).Select(at => new RenderedMatch(at, _surface.Preview(at)))];
    }

    /// <summary>Removes the search highlights (no rebuild — scroll is preserved).</summary>
    public void ClearSearch() => _surface.Stop();

    /// <summary>Steps to the next (<paramref name="delta"/> = +1) or previous match.</summary>
    public void StepSearch(int delta)
    {
        if (delta >= 0) _surface.Next();
        else _surface.Previous();
    }

    /// <summary>Narrows the painted matches to the given ordinals; returns how many survived.</summary>
    public int RestrictSearch(IReadOnlySet<int> keep) => _surface.Restrict(keep);

    /// <summary>
    /// Whether this surface scrolls itself. Default <see cref="ScrollBarVisibility.Disabled"/> so the
    /// control auto-sizes to content (e.g. chat bubbles inside an outer scroller).
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => _surface.VerticalScrollBarVisibility;
        set => _surface.VerticalScrollBarVisibility = value;
    }
}
