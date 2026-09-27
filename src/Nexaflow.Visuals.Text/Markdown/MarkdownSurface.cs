using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Prose;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Stages;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Markdown, shown and written in: the one control a document is put on, wherever it is put.
///
/// <para>
/// <strong>Two things decide what it is.</strong> <see cref="IsReadOnly"/> — a reply from the assistant is only read,
/// a document open in its own tab is written in — and <see cref="WrittenIn"/>, which makes it content in one language
/// rather than a document: the Solver's formula field is this control holding nothing but LaTeX. Everything
/// else is how it is drawn and what the host is told.
/// </para>
/// <para>
/// <strong>One element, not one per block.</strong> Everything in the document is pieces of a single laid tree — the
/// prose, the diagrams, the tunes — so a drag runs from a word into a chart without anything forwarding gestures between
/// controls, a search looks in one place, and the caret goes from a sentence into a formula by the same arrow key it
/// moves along the sentence with.
/// </para>
/// <para>
/// <strong>It says what things mean and the host does them.</strong> Copying, pasting, saving a picture and following a
/// link out of the document are raised — copying and pasting as routed events the application handles once
/// (<see cref="CopyingEvent"/>, <see cref="PastingEvent"/>), the rest as verbs (<see cref="ILayoutActions"/>) — rather than
/// done here: a clipboard, a file and a browser are the application's, shared with everything else in the window. What
/// is this control's is what only it can know — where in the document something is, what is on the page, and what was
/// written, so that it can be taken back.
/// </para>
/// </summary>
public sealed partial class MarkdownSurface : UserControl, ILayoutActions
{
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _prompt;

    private MarkdownElement _shown;
    /// <summary>What the element now on the page is drawn in.</summary>
    private StyleFormat? _drawnIn;

    /// <summary>
    /// What lays the document out, for as long as this shows one: what it read, the blocks that read as they did, and what the
    /// reader opened in each diagram outlive every element made to show it.
    /// </summary>
    private readonly ContentEngine _engine = new();

    /// <summary>The document as it was read — the tree the laid layout was drawn from, as the engine last read it.</summary>
    private ContentPart Read => _engine.ReadRoot;

    /// <summary>Set while this control is telling a binding what was written, so the value coming back is not taken for a new document.</summary>
    private bool _telling;

    public MarkdownSurface()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;

        // What the engine says happened, said again to the page.
        _engine.SourceChanged += (_, change) => Written(change);
        _engine.SelectionChanged += Picked;
        _engine.PreRender += Laid;

        // Over the document rather than in it, where the first thing written will go, and never in the way of a press.
        _prompt = new TextBlock
        {
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };
        _prompt.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

        _scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };

        // The document is told which part of it is on screen, so only what is near that is painted.
        _scroller.ScrollChanged += (_, _) => Shows(_shown);

        _shown = Made(string.Empty);

        base.Content = new Grid { Children = { _scroller, _prompt } };

        PreviewMouseLeftButtonDown += (_, _) =>
        {
        if (!IsKeyboardFocusWithin) Focus();
        };

        // A scroller that is not to scroll still takes the wheel, and a document in a conversation would then stop the
        // conversation scrolling wherever the pointer rested on it. So the wheel goes on to whatever holds this.
        _scroller.PreviewMouseWheel += (_, args) =>
        {
            if (VerticalScrollBarVisibility != ScrollBarVisibility.Disabled || args.Handled) return;

            args.Handled = true;
            (Parent as UIElement ?? VisualTreeHelper.GetParent(this) as UIElement)?.RaiseEvent(
                new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta) { RoutedEvent = MouseWheelEvent, Source = this });
        };
    }

    // ── What it is ──────────────────────────────────────────────────────────

    /// <summary>
    /// The markdown shown — and, where it is written in, what was written. Binds both ways: a host hands a document in and
    /// is told every edit, but the text it is told is never taken back for a new document.
    /// </summary>
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownSurface),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (surface, args) => ((MarkdownSurface)surface).Shows((string?)args.NewValue ?? string.Empty)));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Whether it is only read. A read document can still be selected, searched and copied from.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(MarkdownSurface),
        new PropertyMetadata(true, (surface, _) => ((MarkdownSurface)surface).Remake()));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// What the content is written in, where it is one language rather than a markdown document — <c>latex</c>, <c>abc</c>,
    /// any language a fence can name — with <see cref="Markdown"/> carrying only that language's own text, laid out by that
    /// language alone. Empty, the default, for a document.
    /// </summary>
    public static readonly DependencyProperty WrittenInProperty = DependencyProperty.Register(
        nameof(WrittenIn), typeof(string), typeof(MarkdownSurface),
        new PropertyMetadata(null, (surface, _) => ((MarkdownSurface)surface).Rewritten()));

    public string? WrittenIn
    {
        get => (string?)GetValue(WrittenInProperty);
        set => SetValue(WrittenInProperty, value);
    }

    /// <summary>The language the content is written in, or null for a document.</summary>
    private string? Named => string.IsNullOrWhiteSpace(WrittenIn) ? null : WrittenIn.Trim();

    /// <summary>Whether the content is a formula, which a palette key types into wherever the caret is.</summary>
    private bool Maths => Named?.ToLowerInvariant() is "latex" or "math" or "tex";

    /// <summary>
    /// Shows the characters written rather than what they draw — for when the drawing itself is the trouble, a formula that
    /// will not set. Everything else about writing in it carries on as it was.
    /// </summary>
    public static readonly DependencyProperty EditAsSourceProperty = DependencyProperty.Register(
        nameof(EditAsSource), typeof(bool), typeof(MarkdownSurface),
        new PropertyMetadata(false, (surface, _) => ((MarkdownSurface)surface).HoldAsWritten()));

    public bool EditAsSource
    {
        get => (bool)GetValue(EditAsSourceProperty);
        set => SetValue(EditAsSourceProperty, value);
    }

    // ── How it is drawn ─────────────────────────────────────────────────────

    /// <summary>
    /// The colours it is drawn in. Unset, it follows the theme (<see cref="StyleFormat.FromTheme"/>); a fixed light
    /// surface — a post-it — says <see cref="StyleFormat.Light"/>.
    /// </summary>
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(StyleFormat), typeof(MarkdownSurface),
        new PropertyMetadata(null, (surface, _) => ((MarkdownSurface)surface).Remake()));

    public StyleFormat? Palette
    {
        get => (StyleFormat?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>
    /// The size body text is set at, with everything else in proportion. Unset (<c>NaN</c>) it is the shell's text
    /// size; a host with a zoom of its own binds the zoomed size here, and it applies at once even while being written in.
    /// </summary>
    public static readonly DependencyProperty BaseFontSizeProperty = DependencyProperty.Register(
        nameof(BaseFontSize), typeof(double), typeof(MarkdownSurface),
        new PropertyMetadata(double.NaN, (surface, _) => ((MarkdownSurface)surface).Remake()));

    public double BaseFontSize
    {
        get => (double)GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    /// <summary>What is shown where nothing is written yet, until something is.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(MarkdownSurface),
        new PropertyMetadata(string.Empty, (surface, _) => ((MarkdownSurface)surface).Prompted()));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Room left round the document inside the scroller, rather than a margin outside it — a margin would push the
    /// scrollbar in too, leaving a gap before anything beside it.
    /// </summary>
    public static readonly DependencyProperty ContentPaddingProperty = DependencyProperty.Register(
        nameof(ContentPadding), typeof(Thickness), typeof(MarkdownSurface),
        new PropertyMetadata(default(Thickness), (surface, _) => ((MarkdownSurface)surface).Padded()));

    public Thickness ContentPadding
    {
        get => (Thickness)GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }

    /// <summary>
    /// Where a relative <c>![](file.png)</c> is looked for. Null leaves only absolute and <c>file:</c> pictures, and
    /// whatever <see cref="ImageResolver"/> finds.
    /// </summary>
    public static readonly DependencyProperty BaseDirectoryProperty = DependencyProperty.Register(
        nameof(BaseDirectory), typeof(string), typeof(MarkdownSurface),
        new PropertyMetadata(null, (surface, _) => ((MarkdownSurface)surface).Hosted()));

    public string? BaseDirectory
    {
        get => (string?)GetValue(BaseDirectoryProperty);
        set => SetValue(BaseDirectoryProperty, value);
    }

    /// <summary>
    /// Whether this scrolls itself, or lets whatever holds it do the scrolling — the second being what a reply in a
    /// conversation wants, where every reply scrolling separately would be unusable, and so the default.
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => _scroller.VerticalScrollBarVisibility;
        set => _scroller.VerticalScrollBarVisibility = value;
    }

    // ── What the host says ──────────────────────────────────────────────────

    /// <summary>What answers the verbs this document raises and does not answer itself — saving a picture, and anything a language offers.</summary>
    public ILayoutActions? Host { get; set; }

    /// <summary>The host's say in where a picture comes from, asked before <see cref="BaseDirectory"/>.</summary>
    public Func<string, ImageSource?>? ImageResolver { get => _pictures; set { _pictures = value; Hosted(); } }

    private Func<string, ImageSource?>? _pictures;

    /// <summary>
    /// The host's say in how a link looks, asked for every link with the URL as written and the words it was written as —
    /// which is what lets the help pane mark a <c>locate:</c> link without disturbing those words.
    /// </summary>
    public Func<string, string, LinkLook?>? LinkDecorator { get => _links; set { _links = value; Hosted(); } }

    private Func<string, string, LinkLook?>? _links;

    /// <summary>
    /// A diagram node's expand chip was pressed. True claims it — a host that generated the diagram writes
    /// <see cref="Markdown"/> again with more of the tree walked. Null lets the diagram open the node from what its source
    /// already says.
    /// </summary>
    public Func<DiagramExpandRequest, bool>? DiagramExpand { get; set; }

    /// <summary>What a <c>{{…}}</c> written in a diagram is read against. Null leaves one drawn as it was written.</summary>
    public Nexaflow.Markdown.Binding.IDataContext? DiagramData { get => _data; set { _data = value; Hosted(); } }

    private Nexaflow.Markdown.Binding.IDataContext? _data;

    /// <summary>
    /// The host's say in something dropped here — a picture, a file, a link. True where it took it (usually through
    /// <see cref="InsertMarkdownAt"/>); false leaves it to be written in as text, so a host can say "not mine".
    /// </summary>
    public Func<IDataObject, Point, bool>? ContentDropped { get; set; }

    /// <summary>The same for something pasted: true where the host took it, false leaves it to be written in as text.</summary>
    public Func<IDataObject, bool>? ContentPasted { get; set; }

    /// <summary>The element the document is drawn on — where the caret, what is picked out and the laid tree live.</summary>
    public MarkdownElement Shown => _shown;

    /// <summary>Lays everything out again — what a host calls once what <see cref="DiagramData"/> holds has changed.</summary>
    public void RefreshDiagrams() => _shown.Refresh();

    /// <summary>Forgets what the reader had opened and chosen in every diagram here, and draws them as their sources say.</summary>
    public void ResetDiagramViews()
    {
        _engine.CloseDiagrams();
        _shown.Refresh();
    }

    // ── Showing it ──────────────────────────────────────────────────────────

    /// <summary>The colours and size it is drawn at, as they stand: the size the host gave, or the shell's where it gave none.</summary>
    private StyleFormat Drawn =>
        (Palette ?? StyleFormat.FromTheme()) with
        {
            TextSize = double.IsNaN(BaseFontSize) || BaseFontSize <= 0 ? TextTypography.BaseFontSize : BaseFontSize,
        };

    /// <summary>What the host said about what the document holds, as the engine is told it.</summary>
    private ContentInputs Asked => new(MarkdownPictures.Found(_pictures, BaseDirectory), _links, _data);

    /// <summary>Tells the engine what the host now says, and lays the document out again by it.</summary>
    private void Hosted()
    {
        if (_shown is null) return;

        _engine.Inputs = Asked;
        _shown.Refresh();
    }

    /// <summary>A new document from outside — not something written here, which never comes back this way.</summary>
    private void Shows(string markdown)
    {
        if (_telling || string.Equals(_shown.Markdown, markdown, StringComparison.Ordinal)) return;

        _shown.Markdown = markdown;

        Settled();
    }

    /// <summary>After a different document is shown: nothing of the last one can be taken back, found or held open.</summary>
    private void Settled()
    {
        

        // A field holding one language is written at the end of what it holds; a document is read from its top.
        _shown.Restore(_shown.Current.MoveCaretTo(Named is null ? 0 : _shown.Markdown.Length));

        HoldAsWritten();
        _engine.Begin();

        Stop();
        Prompted();
    }

    /// <summary>
    /// Makes the element again, keeping what is being written and where — for whatever the element is built with and
    /// cannot change: its colours, its size, whether it may be written in, and what the host said about what it holds.
    /// </summary>
    private void Remake()
    {
        if (_shown is null) return;

        var state = _shown.Current;
        var caret = _shown.HasCaret;

        // What was laid in the old colours, at the old size or for the old reader is not set down again as it was.
        _engine.Forget();

        _shown.Release();
        _shown = Made(state.Source);
        _shown.Restore(state);

        // Laid now rather than at the next layout pass, so what is asked of it in between — a selection, a search, the
        // formula under the caret — has a tree to ask.
        _shown.Refresh();
        if (caret) _shown.TakeCaret(state.Caret);
    }

    private MarkdownElement Made(string source)
    {
        var style = Drawn;

        _drawnIn = style;
        _engine.Inputs = Asked;

        var element = new MarkdownElement(source, style, this, _engine, Named)
        {
            IsReadOnly = IsReadOnly,
            Margin = ContentPadding,
        };

        
        element.CaretMoved += (_, _) => Reveal();

        _scroller.Content = element;
        Shows(element);

        return element;
    }

    /// <summary>
    /// Tells <paramref name="element"/> which part of it the scroller is showing. A scroller that does not scroll shows all of
    /// it, so nothing is left out; one not yet laid out says nothing, and the next scroll or resize says it.
    /// </summary>
    private void Shows(MarkdownElement? element)
    {
        if (element is null || _scroller.ViewportHeight <= 0 || _scroller.ViewportWidth <= 0) return;

        element.OnScreen = new Rect(_scroller.HorizontalOffset, _scroller.VerticalOffset, _scroller.ViewportWidth, _scroller.ViewportHeight);
    }

    private void Padded()
    {
        _shown.Margin = ContentPadding;

        // The prompt stands where the first word would, so it is inset as the document is.
        _prompt.Margin = ContentPadding;
    }

    /// <summary>Shows the prompt while nothing is written, and takes it away the moment something is.</summary>
    private void Prompted()
    {
        _prompt.Text = Placeholder ?? string.Empty;
        _prompt.FontSize = Drawn.TextSize;
        _prompt.Visibility = !string.IsNullOrEmpty(Placeholder) && Markdown.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── What it is written in ───────────────────────────────────────────────

    /// <summary>Reads the same text again as what it now is — a document, or content in one language.</summary>
    private void Rewritten()
    {
        if (_shown is null) return;

        Remake();
        Settled();
    }

    /// <summary>Keeps the whole text shown as it was written while <see cref="EditAsSource"/> says so.</summary>
    private void HoldAsWritten()
    {
        if (_shown is null) return;

        _engine.HoldsWritten = EditAsSource;
    }

    // ── Links ───────────────────────────────────────────────────────────────

    /// <summary>Scrolls the heading a <c>#anchor</c> link names into view; false where the document has no such heading.</summary>
    public bool ScrollToAnchor(string anchor) => GoTo(ContentPath.Read($"{MarkdownKinds.Heading}:{anchor}"));

    private static bool Leads(string url) => Uri.TryCreate(url, UriKind.Absolute, out _);

    /// <inheritdoc/>
    bool ILayoutActions.Invoke(LayoutAct act)
    {
        // A choice made from the menu closes it, whatever it turned out to be.
        if (act.Gesture == LayoutGesture.ContextMenu && _ribbon is { IsOpen: true }) _ribbon.IsOpen = false;

        switch (act.Intent.Verb)
        {
            case LayoutVerbs.Navigate when act.Intent.Target is { Length: > 0 } where:
                return Leads(where) && (OpenLink(where) || (Host?.Invoke(act) ?? false));

            case LayoutVerbs.Copy when act.Intent.Target is { } what:
            return Copy(MarkdownClipboard.Copied(what, null));

            // A corner's copy, pressed on a block: the block, as the document writes it.
            case LayoutVerbs.Copy when act.Gesture == LayoutGesture.Click && act.Node is { } block:
            return Copy(MarkdownClipboard.Copied(_shown.Markdown, (block.Start, block.Length)));

            default:
                return Diagrammed(act) ?? (Chose(act.Intent.Verb) || (Host?.Invoke(act) ?? false));
        }
    }

    /// <summary>
    /// A verb a diagram answers for itself — opening a node, folding it, choosing it — answered for the diagram it was raised
    /// in: found up the layout from the piece pressed, and told that diagram's own state and what the host said about it.
    /// Null where the verb is not one of those, or was raised in no diagram.
    /// </summary>
    private bool? Diagrammed(LayoutAct act)
    {
        if (act.Intent.Verb is not (LayoutVerbs.Expand or LayoutVerbs.Collapse)) return null;

        for (var piece = act.Piece; piece.Exists; piece = piece.Parent)
        {
            if (piece.Part is not ContentPart part || ContentLanguages.Held(part) is null) continue;

            return new DiagramActions(DiagramExpand, _engine.Opened(part)) { Shown = _shown }.Invoke(act);
        }

        // Content in one language is one diagram, where it is one.
        return Named is null ? null : new DiagramActions(DiagramExpand, _engine.Opened(Read)) { Shown = _shown }.Invoke(act);
    }

    /// <inheritdoc/>
    IReadOnlyList<LayoutIntent> ILayoutActions.Menu(LayoutAct act) => [.. Offered(), .. Host?.Menu(act) ?? []];

    // ── What a block offers ─────────────────────────────────────────────────

    /// <summary>The block of the document an offset is in — the whole of it, where it is content in one language.</summary>
    private ContentPart? Blocked(int offset) => _engine.Blocked(offset);

    /// <summary>What the block at a point offers in its corner — whichever of the usual buttons it allows, and whatever it adds.</summary>
    public IReadOnlyList<LayoutIntent> Corner(Point at) => _engine.Offers(at);

    /// <summary>
    /// Shows the block at <paramref name="at"/> as it was written, with the caret where it was pressed — what two presses on a
    /// block do. It is drawn again once the caret leaves it.
    /// </summary>
    /// <returns>Whether it did: not where the document is only read, and not in a block already shown as written.</returns>
    public bool OpenAsWritten(Point at) => _engine.OpenAsWritten(at);

    /// <summary>
    /// What a corner button means for the block at <paramref name="at"/>: copying it is asked of whoever holds the clipboard, and
    /// anything else goes to the host with the block it was pressed on.
    /// </summary>
    public void Raise(LayoutIntent offer, Point at) => _engine.Raise(offer, at);

    /// <summary>
    /// A picture of one block as it is on the page, for a host keeping one of what a corner button was pressed on —
    /// painted from the page's own tree, cut to where the block came out, so nothing is read again to make it.
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? Picture(ContentPart block, Brush? ground = null)
    {
        var box = _engine.Where(block);
        if (box.IsEmpty || box.Width <= 0 || box.Height <= 0) return null;

        var scale = _shown.Zoom;
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            if (ground is not null) dc.DrawRectangle(ground, null, new Rect(0, 0, box.Width * scale, box.Height * scale));

            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-box.X, -box.Y));
            dc.PushClip(new RectangleGeometry(box));
            LayoutPainter.Paint(dc, _shown.Laid.Root, (_drawnIn ?? Drawn).Text);
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(box.Width * scale * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(box.Height * scale * dpi.DpiScaleY)),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }
    private Point Middle() =>
        new(_scroller.HorizontalOffset + (_scroller.ViewportWidth / 2),
            _scroller.VerticalOffset + (_scroller.ViewportHeight / 2));
}
