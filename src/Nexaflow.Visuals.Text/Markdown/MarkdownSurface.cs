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
/// a document open in its own tab is written in — and <see cref="SingleBlock"/>, which makes it one block of one
/// language rather than a document: the Solver's formula field is this control holding nothing but LaTeX. Everything
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
    private readonly Border _corner;
    private readonly StackPanel _buttons;
    private readonly TextBlock _prompt;

    private MarkdownElement _shown;
    private ContentPart? _over;

    /// <summary>What the element now on the page was built with, so it is only made again when that changes.</summary>
    private (StyleFormat Style, bool Writable, DiagramRenderOptions Options)? _built;

    /// <summary>
    /// The document as it was read — the tree the laid layout was drawn from, reached through any part it drew, so the
    /// document is read once and every question about what it is made of is asked of that one reading. A paragraph that
    /// needed no piece of its own is still a paragraph in it.
    /// </summary>
    private ContentPart Read =>
        _shown.Laid.Root.Part is ContentPart drawn ? drawn.Ancestors().LastOrDefault() ?? drawn : Unread;

    /// <summary>A document with nothing in it, for before anything has been laid.</summary>
    private static readonly ContentPart Unread = ContentPart.Of(ContentNode.Branch(MarkdownKinds.Document, []));

    /// <summary>Set while this control is telling a binding what was written, so the value coming back is not taken for a new document.</summary>
    private bool _telling;

    public MarkdownSurface()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;

        _buttons = new StackPanel { Orientation = Orientation.Horizontal };

        // Faint, in the corner, out of the way of the words — and whole once the pointer is on it.
        _corner = new Border
        {
            Child = _buttons,
            Opacity = Faint,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 12, 0),
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(3),
        };

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

        base.Content = new Grid { Children = { _scroller, _prompt, _corner } };

        _corner.MouseEnter += (_, _) => _corner.Opacity = 1;
        _corner.MouseLeave += (_, _) => _corner.Opacity = Faint;

        // Over the corner's own buttons the block they belong to is still the one pointed at.
        MouseMove += (_, args) => { if (!_corner.IsMouseOver) Over(args.GetPosition(_shown)); };
        MouseLeave += (_, _) => Over(null);
        PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (!IsKeyboardFocusWithin) Focus();

            // Two presses on a block show it as it was written; taken here, before the element picks out the word pressed.
            if (args.ClickCount == 2 && OpenAsWritten(args.GetPosition(_shown))) args.Handled = true;
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
    /// Makes this one block of one language rather than a document — <c>latex</c>, <c>abc</c>, any fenced language —
    /// with <see cref="Markdown"/> carrying only that language's own text. What says "this is maths" (<c>$$</c>) or "this
    /// is a tune" (a fence) is put round it to be drawn and taken off again on the way out, so the host never sees or has
    /// to keep it. Empty, the default, for a document.
    /// </summary>
    public static readonly DependencyProperty SingleBlockProperty = DependencyProperty.Register(
        nameof(SingleBlock), typeof(string), typeof(MarkdownSurface),
        new PropertyMetadata(null, (surface, _) => ((MarkdownSurface)surface).Reframe()));

    public string? SingleBlock
    {
        get => (string?)GetValue(SingleBlockProperty);
        set => SetValue(SingleBlockProperty, value);
    }

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
    /// Height a diagram may take, for a pane that is entirely one diagram: bind it to the pane and the diagram fills it
    /// instead of running past the bottom. Zero, the default, uses the built-in cap.
    /// </summary>
    public static readonly DependencyProperty MaxDiagramHeightProperty = DependencyProperty.Register(
        nameof(MaxDiagramHeight), typeof(double), typeof(MarkdownSurface),
        new PropertyMetadata(0.0, (surface, args) =>
        {
            // A pane resize walks this through every pixel on the way; a diagram does not care about a few of them.
            if (Math.Abs((double)args.NewValue - (double)args.OldValue) >= 24) ((MarkdownSurface)surface).Remake();
        }));

    public double MaxDiagramHeight
    {
        get => (double)GetValue(MaxDiagramHeightProperty);
        set => SetValue(MaxDiagramHeightProperty, value);
    }

    /// <summary>
    /// Where a relative <c>![](file.png)</c> is looked for. Null leaves only absolute and <c>file:</c> pictures, and
    /// whatever <see cref="ImageResolver"/> finds.
    /// </summary>
    public static readonly DependencyProperty BaseDirectoryProperty = DependencyProperty.Register(
        nameof(BaseDirectory), typeof(string), typeof(MarkdownSurface),
        new PropertyMetadata(null, (surface, _) => ((MarkdownSurface)surface).Remake()));

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

    /// <summary>A link out of the document. True where the host took it; false leaves it to open as links do.</summary>
    public Func<string, bool>? LinkNavigate { get; set; }

    /// <summary>The host's say in where a picture comes from, asked before <see cref="BaseDirectory"/>.</summary>
    public Func<string, ImageSource?>? ImageResolver { get => _pictures; set { _pictures = value; Remake(); } }

    private Func<string, ImageSource?>? _pictures;

    /// <summary>
    /// The host's say in how a link looks, asked for every link with the URL as written and the words it was written as —
    /// which is what lets the help pane mark a <c>locate:</c> link without disturbing those words.
    /// </summary>
    public Func<string, string, LinkLook?>? LinkDecorator { get => _links; set { _links = value; Remake(); } }

    private Func<string, string, LinkLook?>? _links;

    /// <summary>
    /// A diagram node's expand chip was pressed. True claims it — a host that generated the diagram writes
    /// <see cref="Markdown"/> again with more of the tree walked. Null lets the diagram open the node from what its source
    /// already says.
    /// </summary>
    public Func<DiagramExpandRequest, bool>? DiagramExpand { get => _expand; set { _expand = value; Remake(); } }

    private Func<DiagramExpandRequest, bool>? _expand;

    /// <summary>A diagram's chosen node changed — for a host showing detail beside the diagram. The key is null when nothing is chosen.</summary>
    public Action<DiagramSelection>? DiagramSelect { get => _select; set { _select = value; Remake(); } }

    private Action<DiagramSelection>? _select;

    /// <summary>What a <c>{{…}}</c> written in a diagram is read against. Null leaves one drawn as it was written.</summary>
    public Nexaflow.Markdown.Binding.IDataContext? DiagramData { get => _data; set { _data = value; Remake(); } }

    private Nexaflow.Markdown.Binding.IDataContext? _data;

    /// <summary>In a diagram, a single press chooses a node and two open it — for a pane where opening one costs something.</summary>
    public bool DiagramOpenOnDoubleClick { get => _double; set { _double = value; Remake(); } }

    private bool _double;

    /// <summary>When true, a diagram scales down to the width it is given rather than being cut off.</summary>
    public bool FitContentToWidth { get => _fit; set { _fit = value; Remake(); } }

    private bool _fit;

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

    /// <summary>Where each diagram's opened nodes and zoom live between renders — on this control rather than on the element.</summary>
    private readonly DiagramViewStates _diagramStates = new();

    /// <summary>Forgets what the reader had opened and chosen in every diagram here, and draws them as their sources say.</summary>
    public void ResetDiagramViews()
    {
        _diagramStates.Clear();
        _shown.Refresh();
    }

    // ── Showing it ──────────────────────────────────────────────────────────

    /// <summary>The colours and size it is drawn at, as they stand: the size the host gave, or the shell's where it gave none.</summary>
    private StyleFormat Drawn =>
        (Palette ?? StyleFormat.FromTheme()) with
        {
            TextSize = double.IsNaN(BaseFontSize) || BaseFontSize <= 0 ? TextTypography.BaseFontSize : BaseFontSize,
        };

    /// <summary>What the host said about the content written inside the document, gathered once per element.</summary>
    private DiagramRenderOptions Asked(StyleFormat style) => new()
    {
        Palette = style,
        ReadOnly = IsReadOnly,
        OnNavigate = OpenLink,
        OnExpand = _expand,
        OnSelect = _select,
        DataContext = _data,
        Pictures = MarkdownPictures.Found(_pictures, BaseDirectory),
        Links = _links,
        FitToWidth = _fit || !IsReadOnly,
        OpenOnDoubleClick = _double,
        MaxHeight = MaxDiagramHeight,
        Views = _diagramStates,
    };

    /// <summary>A new document from outside — not something written here, which never comes back this way.</summary>
    private void Shows(string markdown)
    {
        if (_telling || string.Equals(Unframed(_shown.Markdown), markdown, StringComparison.Ordinal)) return;

        _shown.Markdown = Framed(markdown);

        Settled();
    }

    /// <summary>After a different document is shown: nothing of the last one can be taken back, found or held open.</summary>
    private void Settled()
    {
        _history.Clear();

        // A field holding one block is written at the end of what it holds; a document is read from its top.
        var (start, length) = Inner;
        _shown.Restore(_shown.Current.MoveCaretTo(string.IsNullOrEmpty(SingleBlock) ? 0 : start + length));

        HoldAsWritten();
        _last = _shown.Current;

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

        _shown = Made(state.Source);
        _shown.Restore(state);

        // Laid now rather than at the next layout pass, so what is asked of it in between — a selection, a search, the
        // formula under the caret — has a tree to ask.
        _shown.Refresh();
        if (caret) _shown.TakeCaret(state.Caret);

        _last = _shown.Current;
    }

    private MarkdownElement Made(string source)
    {
        var style = Drawn;
        var options = Asked(style);

        _built = (style, !IsReadOnly, options);

        var element = new MarkdownElement(source, style, this, options)
        {
            IsReadOnly = IsReadOnly,
            Margin = ContentPadding,
        };

        element.SourceChanged += (_, _) => Written();
        element.CaretMoved += (_, _) => { Confined(); Moved(); Reveal(); };
        element.SelectionChanged += (_, _) => Moved();

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

    /// <summary>
    /// In one block of a language, keeps the caret in the host's own text: past either end are the delimiters that were
    /// put round it to draw it, which are nobody's to write in.
    /// </summary>
    private void Confined()
    {
        if (string.IsNullOrEmpty(SingleBlock)) return;

        var (start, length) = Inner;
        var caret = _shown.Caret;

        if (caret < start || caret > start + length) _shown.Restore(_shown.Current.MoveCaretTo(Math.Clamp(caret, start, start + length)));
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

    // ── One block of one language ───────────────────────────────────────────

    /// <summary>What goes either side of the text to make it the block it is. Maths is delimited, not fenced — a <c>```latex</c> block would be a listing of LaTeX, not a formula.</summary>
    private (string Open, string Close) Fence =>
        SingleBlock?.Trim().ToLowerInvariant() switch
        {
            null or "" => (string.Empty, string.Empty),
            "latex" or "math" or "tex" => ("$$\n", "\n$$"),
            var language => ("```" + language + "\n", "\n```"),
        };

    /// <summary>What the element is given for what the host said: the text inside whatever makes it the block it is.</summary>
    private string Framed(string markdown) => Fence.Open + markdown + Fence.Close;

    /// <summary>What the host is told for what the element holds: the text without what was put round it to draw it.</summary>
    private string Unframed(string source)
    {
        var (open, close) = Fence;
        if (open.Length == 0) return source;

        return source.Length >= open.Length + close.Length
               && source.StartsWith(open, StringComparison.Ordinal)
               && source.EndsWith(close, StringComparison.Ordinal)
            ? source[open.Length..^close.Length]
            : source;
    }

    /// <summary>Where the host's own text lies in what the element holds.</summary>
    private (int Start, int Length) Inner
    {
        get
        {
            var source = _shown.Markdown;
            var (open, close) = Fence;
            var framed = open.Length > 0 && !ReferenceEquals(Unframed(source), source);

            return framed ? (open.Length, source.Length - open.Length - close.Length) : (0, source.Length);
        }
    }

    /// <summary>Re-reads the same text as the block it now is — a document, or one block of a language.</summary>
    private void Reframe()
    {
        if (_shown is null) return;

        _shown.Markdown = Framed(Markdown ?? string.Empty);
        Settled();
    }

    /// <summary>
    /// Keeps the text shown as it was written while <see cref="EditAsSource"/> says so — the whole of it, or the whole of
    /// the block's own text where it is one block, so the delimiters that were never the host's are not shown either.
    /// </summary>
    private void HoldAsWritten()
    {
        if (_shown is null) return;

        var state = _shown.Current;
        var (start, length) = Inner;
        var whole = new RawZone(start, start + length);

        // Let go of only what this held open: a command being spelled is the writer's, and stays shown as they spell it.
        RawZone? wanted = EditAsSource ? whole : state.Raw == whole ? null : state.Raw;
        if (state.Raw == wanted) return;

        _shown.Restore(state with { Raw = wanted });
        _last = _shown.Current;
    }

    // ── Links ───────────────────────────────────────────────────────────────

    /// <summary>Scrolls the heading a <c>#anchor</c> link names into view; false where the document has no such heading.</summary>
    public bool ScrollToAnchor(string anchor) => GoTo(ContentPath.Read($"{MarkdownKinds.Heading}:{anchor}"));

    // A link into this document is answered by the element and never reaches here. Anything else that says where it goes is
    // the host's; a relative link that is not an anchor says nowhere, so nothing is handed on for it.
    private bool OpenLink(string url) => LinkNavigate?.Invoke(url) ?? false;

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
        if (act.Intent.Verb is not (LayoutVerbs.Expand or LayoutVerbs.Collapse or LayoutVerbs.Select)) return null;

        for (var piece = act.Piece; piece.Exists; piece = piece.Parent)
        {
            if (piece.Part is not ContentPart part || ContentLanguages.Held(part) is null) continue;

            return new DiagramActions(Asked(Drawn), _shown.Engine.Opened(part)) { Shown = _shown }.Invoke(act);
        }

        return null;
    }

    /// <inheritdoc/>
    IReadOnlyList<LayoutIntent> ILayoutActions.Menu(LayoutAct act) => [.. Offered(), .. Host?.Menu(act) ?? []];

    // ── What a block offers ─────────────────────────────────────────────────

    /// <summary>
    /// Shows the buttons for whatever block the pointer is over, and takes them away when it leaves.
    ///
    /// <para>
    /// What they are is the language's to say — a picture of a diagram is worth keeping and a picture of a code fence is
    /// a worse copy of the code — so this asks rather than deciding.
    /// </para>
    /// </summary>
    private void Over(Point? at)
    {
        var block = at is { } where ? Blocked(where) : null;

        if (ReferenceEquals(block, _over)) return;

        _over = block;
        _buttons.Children.Clear();

        if (block is null || at is not { } point)
        {
            _corner.Visibility = Visibility.Collapsed;

            return;
        }

        foreach (var offer in Offered(block)) _buttons.Children.Add(Button(offer, point));

        var box = Where(block);

        _corner.Visibility = _buttons.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _corner.Margin = new Thickness(0, Math.Max((box.IsEmpty ? 0 : box.Y) * _shown.Zoom - _scroller.VerticalOffset + 2, 2), 12, 0);
    }

    /// <summary>
    /// The block a point is in: not the word or the run under it, but the thing the document is made of that holds them
    /// — a paragraph, a table, a fence — and nothing where the point is on none of them.
    ///
    /// <para>
    /// Found by the offset rather than by walking up from the piece. A piece knows the characters it was drawn from, but
    /// not always as a part of this document's tree: a code fence's runs carry plain spans, because the thing that drew
    /// them was reading code and not markdown. An offset is an offset whatever drew it, and every language is laid at
    /// the offset its body starts at — so this is the one question that works the same everywhere.
    /// </para>
    /// </summary>
    private ContentPart? Blocked(Point at)
    {
        var piece = _shown.Laid.Root.PieceAt(at);
        if (!piece.Exists) return null;

        // The nearest piece answers a point that is on nothing, which is a question about what is near and not about what
        // the point is on.
        return Blocked(piece.Sits().Start) is { } block && Where(block) is { IsEmpty: false } box && box.Contains(at) ? block : null;
    }

    /// <summary>The block of the document an offset is in.</summary>
    private ContentPart? Blocked(int offset)
    {
        foreach (var block in Read.Children)
            if (!block.Derived && block.Role != Roles.Trivia && offset >= block.Start && offset < block.End)
                return block;

        return null;
    }

    /// <summary>
    /// Where a block came out on the page: the whole of what it drew, from its top to its bottom and across the page — a
    /// short line is still a block the width of the page, and its corner stands at the page's edge, so the way from the
    /// words to the corner never leaves the block.
    ///
    /// <para>
    /// Every block of the document is a piece of its own, holding all it drew — a barcode's bars and a chart's wedges too,
    /// which stand for no characters and so lie in no stretch of them. So the block's own piece is what answers, found by the
    /// first thing in it that was written; the stretch of its characters answers only where there is no such piece.
    /// </para>
    /// </summary>
    private Rect Where(ContentPart block)
    {
        Rect Across(Rect box) => new(0, box.Y, Math.Max(box.Right, _shown.Laid.Size.Width), box.Height);

        foreach (var whole in _shown.Laid.Root.Children)
        {
            if (whole.Kind != MarkdownPieces.Whole) continue;
            if (whole.SelfAndDescendants().Select(piece => piece.Part).FirstOrDefault(part => part is { Length: > 0 }) is not { } named) continue;

            if (named.Start >= block.Start && named.Start < block.End) return Across(whole.Bounds);
        }

        var rects = _shown.Laid.Root.RangeRects(block.Start, Math.Max(block.Length, 1));
        if (rects.Count == 0) return Rect.Empty;

        var box = rects[0];

        foreach (var rect in rects) box = Rect.Union(box, rect);

        return Across(box);
    }

    /// <summary>What the block at a point offers in its corner — whichever of the usual buttons it allows, and whatever it adds of its own.</summary>
    public IReadOnlyList<LayoutIntent> Corner(Point at) =>
        Blocked(at) is { } block ? [.. Offered(block)] : [];

    /// <summary>
    /// Shows the block at <paramref name="at"/> as it was written, with the caret where it was pressed — the whole block,
    /// whatever it holds, which is the default a language may one day say otherwise to for its own. It is drawn again once
    /// the caret leaves it.
    /// </summary>
    /// <returns>
    /// Whether it did: not where the document is only read, and not in a block already shown as written, where two presses
    /// pick out a word as they do in any text.
    /// </returns>
    public bool OpenAsWritten(Point at)
    {
        if (IsReadOnly || Blocked(at) is not { } block) return false;

        // As much of it as is drawn when it is shown: the line ending that closes it is not somewhere to write.
        var zone = new RawZone(block.Start, block.Start + block.Print().TrimEnd('\n', '\r').Length);
        var state = _shown.Current;

        if (zone.Length == 0 || (state.Raw is { } shown && shown.Start < zone.End && zone.Start < shown.End)) return false;

        var caret = Math.Clamp(_shown.Laid.OffsetAt(at), zone.Start, zone.End);

        _shown.Restore(state.MoveCaretTo(caret) with { Raw = zone });
        _shown.Refresh();
        _shown.TakeCaret(caret);
        _last = _shown.Current;

        return true;
    }

    /// <summary>What a block's corner offers: whichever of the usual ones it allows, and whatever it adds.</summary>
    private IEnumerable<LayoutIntent> Offered(ContentPart block)
    {
        var corner = CornerOf(block);

        if (corner.Copies) yield return new LayoutIntent(LayoutVerbs.Copy, null, "Copy");
        if (corner.Saves) yield return new LayoutIntent(LayoutVerbs.Save, null, "Save as a picture");

        foreach (var added in corner.Adds) yield return added;
    }

    /// <summary>What the language drawing a block says about its corner, or the usual where no language is.</summary>
    private BlockCorner CornerOf(ContentPart block)
    {
        for (var at = block; at is not null; at = at.Parent)
        {
            if (ContentLanguages.Held(at) is not { } language) continue;

            var ask = new ContentAsk(ContentNested.Language(at)!, at.Part(Roles.Body)!.Text) { Part = block, IsReadOnly = IsReadOnly };
            return language.Editing.Corner(ask);
        }

        // Prose is read rather than handled: it is no picture to keep, and copying it is what selecting it is for.
        return BlockCorner.None;
    }

    /// <summary>
    /// One of a corner's buttons: the app's own icon button, drawing the mark for what it does where there is one and its
    /// name where there is not, and saying its name while pointed at.
    /// </summary>
    private FrameworkElement Button(LayoutIntent offer, Point at)
    {
        var named = DiagramRibbon.Names(offer);
        var button = new Button
        {
            Content = DiagramRibbon.Icon(offer) is { } icon
                ? new TextBlock { Text = icon, FontFamily = DiagramRibbon.IconFont, FontSize = 13 }
                : new TextBlock { Text = named, Margin = new Thickness(6, 0, 6, 0) },
            ToolTip = named,
            Margin = new Thickness(1),
            Command = new Does(() => Raise(offer, at)),
        };

        button.SetResourceReference(StyleProperty, "IconButton");
        if (DiagramRibbon.Icon(offer) is null) button.Width = double.NaN;

        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "Markdown_Corner_" + offer.Verb);

        return button;
    }

    /// <summary>How faint a block's corner is until the pointer is on it.</summary>
    private const double Faint = 0.55;

    /// <summary>
    /// What a corner button does. Copying is asked of whoever holds the clipboard, with what copying the block would put
    /// there already worked out; anything else goes to the host with the block it was pressed on.
    /// </summary>
    public void Raise(LayoutIntent offer, Point at)
    {
        if (Blocked(at) is not { } block) return;

        if (offer.Verb == LayoutVerbs.Copy)
        {
            Copy(MarkdownClipboard.Copied(_shown.Markdown, (block.Start, block.Length)));

            return;
        }

        var piece = _shown.Laid.Root.PieceAt(at);

        Host?.Invoke(new LayoutAct(LayoutGesture.Click, offer, piece, block, block, [piece], at));
    }

    /// <summary>
    /// A picture of one block as it is on the page, for a host keeping one of what a corner button was pressed on —
    /// painted from the page's own tree, cut to where the block came out, so nothing is read again to make it.
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? Picture(ContentPart block, Brush? ground = null)
    {
        var box = Where(block);
        if (box.IsEmpty || box.Width <= 0 || box.Height <= 0) return null;

        var scale = _shown.Zoom;
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            if (ground is not null) dc.DrawRectangle(ground, null, new Rect(0, 0, box.Width * scale, box.Height * scale));

            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-box.X, -box.Y));
            dc.PushClip(new RectangleGeometry(box));
            LayoutPainter.Paint(dc, _shown.Laid.Root, (_built?.Style ?? Drawn).Text);
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

    /// <summary>A command that is one thing done, which is all a button in a corner needs.</summary>
    private sealed class Does(Action what) : ICommand
    {
        event EventHandler? ICommand.CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => what();
    }
}
