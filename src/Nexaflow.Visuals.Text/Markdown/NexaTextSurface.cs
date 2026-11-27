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
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Markdown.Editing;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;

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
public sealed partial class NexaTextSurface : UserControl, IContentEvents
{
    /// <summary>Where the page is drawn, with the bars that scroll the engine's view of it beside and under it.</summary>
    private readonly Grid _page;

    /// <summary>
    /// What the element stands in on the page, cutting off what it paints round the view — a screen above and below, slid under it as the view
    /// moves until it is painted again.
    /// </summary>
    private readonly Border _frame = new() { ClipToBounds = true };

    /// <summary>The bar that scrolls the view down the page.</summary>
    private readonly ScrollBar _down;

    /// <summary>The bar that slides the view along lines kept whole.</summary>
    private readonly ScrollBar _across;
    private readonly TextBlock _prompt;

    private MarkdownElement _shown;
    /// <summary>What the element now on the page is drawn in.</summary>
    private LayoutStyle? _drawnIn;

    /// <summary>
    /// What lays the document out, for as long as this shows one: what it read, the blocks that read as they did, and what the
    /// reader opened in each diagram outlive every element made to show it.
    /// </summary>
    private readonly ContentEngine _engine = new(WpfMedium.OnThisThread, WpfStyles.Dark) { PaperStyle = WpfStyles.Light };

    public NexaTextSurface()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;

        // What the engine says happened, said again to the page.
        _engine.SourceChanged += (_, change) => Written(change);

        _engine.PreRender += Laid;
        _engine.Reread += Landed;
        _engine.OutlineChanged += Outlined;

        // The engine keeps the view: where it moves — scrolled, brought to a place, held still while what stands above the reader comes to its
        // height — the bars, the margins and the page above are told. Content shown whole inside something that scrolls it has no view of its
        // own, and a place brought onto it is brought by whatever holds it, which is not held to where it was meanwhile.
        _engine.ViewMoved += (_, _) => Synced();
        _engine.Revealing += (_, _) =>
        {
            if (!_engine.Scrolls) PageMoving();
        };

        // What the view of the document asks of the page, done here.
        _engine.LineAsked += (_, asked) => ScrollToLine(asked.Line, asked.Place);
        Commanding();

        // And everything it asks of whatever shows the content, which is this: see IContentEvents.
        _engine.Events = this;

        // Over the document rather than in it, where the first thing written will go, and never in the way of a press.
        _prompt = new TextBlock
        {
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };
        _prompt.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

        // The bars stand for the engine's view: moved, they move it; and it moving sets them (Synced).
        _down = new ScrollBar { Orientation = Orientation.Vertical, Visibility = Visibility.Collapsed, Focusable = false };
        _across = new ScrollBar { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Focusable = false };
        Grid.SetColumn(_down, 1);
        Grid.SetRow(_across, 1);

        _page = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            RowDefinitions = { new RowDefinition(), new RowDefinition { Height = GridLength.Auto } },
            Children = { _frame, _down, _across },
        };

        // The page sized again is laid in its width while it slides sideways; and, shown whole, it is told all of it is on screen.
        _page.SizeChanged += (_, args) =>
        {
            if (args.WidthChanged) Slid();
            Shows(_shown);
            Synced();
        };
        _page.PreviewMouseWheel += Wheeled;

        _shown = Made(string.Empty);

        _down.ValueChanged += (_, args) =>
        {
            if (!_syncing) _engine.ScrollTo(args.NewValue / _shown.Zoom);
        };
        _across.ValueChanged += (_, args) =>
        {
            if (!_syncing) _engine.SlideViewTo(args.NewValue / _shown.Zoom);
        };

        // The page between what stands beside it on either side, each stack in the order it stands outward from the page.
        Grid.SetColumn(_page, 1);
        Grid.SetColumn(_prompt, 1);
        Grid.SetColumn(_trailing, 2);
        LeadingMargins.CollectionChanged += (_, _) => Stood(_leading, LeadingMargins.Reverse());
        TrailingMargins.CollectionChanged += (_, _) => Stood(_trailing, TrailingMargins);

        base.Content = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { _leading, _page, _prompt, _trailing },
        };

        PreviewMouseLeftButtonDown += (_, _) =>
        {
        if (!IsKeyboardFocusWithin) Focus();
        };

    }

    /// <summary>Whether the bars are being set from the engine's view, so their moving is not taken as the reader moving them.</summary>
    private bool _syncing;

    /// <summary>Whether this scrolls itself, and when its bar shows.</summary>
    private ScrollBarVisibility _vertical = ScrollBarVisibility.Disabled;

    /// <summary>Whether this slides sideways, and when its bar shows.</summary>
    private ScrollBarVisibility _horizontal = ScrollBarVisibility.Disabled;

    /// <summary>
    /// The bars, the margins and the page above told where the engine's view now stands — after it moved, was sized again, or the content
    /// under it was laid again and it was kept inside it.
    /// </summary>
    private void Synced()
    {
        if (_engine.Scrolls && _engine.OnScreen is { } view)
        {
            var (zoom, extent) = (_shown.Zoom, _engine.Extent);

            _syncing = true;

            try
            {
                Barred(_down, _vertical, extent.Height * zoom, view.Height * zoom, _engine.ScrolledDown * zoom);
                Barred(_across, _horizontal, extent.Width * zoom, view.Width * zoom, _engine.ScrolledAcross * zoom);
            }
            finally
            {
                _syncing = false;
            }
        }

        Moved();
        Remargined();
        Reported();

        // A screen reader following where the view stands is told it moved, as it is told of any pane it scrolls.
        if (AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged) && UIElementAutomationPeer.FromElement(this) is NexaTextSurfaceAutomationPeer peer) peer.Scrolled();
    }

    /// <summary><paramref name="bar"/> set to stand for a view <paramref name="view"/> long at <paramref name="at"/> through <paramref name="extent"/>, shown as <paramref name="shown"/> says.</summary>
    private static void Barred(ScrollBar bar, ScrollBarVisibility shown, double extent, double view, double at)
    {
        var room = Math.Max(0, extent - view);

        bar.Maximum = room;
        bar.ViewportSize = view;
        bar.LargeChange = Math.Max(1, view);
        bar.SmallChange = Notch;
        bar.Value = Math.Clamp(at, 0, room);
        bar.Visibility = shown == ScrollBarVisibility.Visible || (shown == ScrollBarVisibility.Auto && room >= 1) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>How far a notch of the wheel scrolls, on the screen — as far as the system says, three lines of sixteen pixels unless it says otherwise.</summary>
    internal static double Notch => Math.Max(1, SystemParameters.WheelScrollLines) * 16;

    /// <summary>
    /// The wheel turned over the page: content that scrolls itself scrolls by it — sideways with Shift held, where it slides — until it can go
    /// no further that way, and then the wheel is whatever holds it's, as it is always for content shown whole.
    /// </summary>
    private void Wheeled(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || !_engine.Scrolls) return;

        var notches = -args.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        var by = (SystemParameters.WheelScrollLines < 0 ? _shown.ActualHeight : Notch) * notches / _shown.Zoom;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (_engine.ViewSlides && _engine.SlideViewBy(by)) args.Handled = true;
            return;
        }

        if (_engine.CanScroll(by)) args.Handled = _engine.ScrollBy(by);
    }

    /// <summary>
    /// Says a place brought onto content shown whole is on its way onto the screen of whatever holds it — which scrolls at its next layout pass —
    /// and lets go of that once that pass is done, whether or not it moved at all.
    /// </summary>
    private void PageMoving()
    {
        _engine.ScreenMoving = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _engine.ScreenMoving = false);
    }

    // ── Beside the page ─────────────────────────────────────────────────────

    /// <summary>
    /// What stands beside the page at its leading edge — the left, for text read left to right — in the order it stands outward from
    /// the page: line numbers, breakpoints, a fold's handles, each one of its own (<see cref="ContentMargin"/>).
    /// </summary>
    public ObservableCollection<ContentMargin> LeadingMargins { get; } = [];

    /// <summary>What stands beside the page at its trailing edge, in the order it stands outward from the page.</summary>
    public ObservableCollection<ContentMargin> TrailingMargins { get; } = [];

    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal };

    /// <summary><paramref name="margins"/>, left to right, standing in <paramref name="side"/> beside this — and whatever stood there before, beside nothing.</summary>
    private void Stood(StackPanel side, IEnumerable<ContentMargin> margins)
    {
        foreach (var gone in side.Children.OfType<ContentMargin>()) gone.Beside(null);
        side.Children.Clear();

        foreach (var margin in margins)
        {
            side.Children.Add(margin);
            margin.Beside(this);
        }
    }

    /// <summary>Tells everything standing beside the page that the lines on it may have moved.</summary>
    private void Remargined()
    {
        foreach (var margin in LeadingMargins) margin.LinesMoved();
        foreach (var margin in TrailingMargins) margin.LinesMoved();
    }

    /// <summary>
    /// The lines of the content standing on the visible page — each one's number, counting from one, and where its rows start and how tall
    /// they come to, in this surface's own frame — for what is drawn beside the page in step with them (<see cref="ContentMargin"/>). A line
    /// whose rows stand partly off the page is among them; content laid by a builder that lays no lines has none.
    /// </summary>
    public IReadOnlyList<ShownLine> LinesShown()
    {
        var height = _shown.ActualHeight;
        if (height <= 0) return [];

        var zoom = _shown.Zoom;
        var top = _engine.ViewOrigin.Y;

        // Where the page's top stands as it is laid out, not as it is slid while the view moves within what was painted: the lines are counted
        // from the view, which is where that top stands however far the drawing under it is slid.
        var at = _shown.TranslatePoint(_shown.RenderTransform.Inverse?.Transform(default) ?? default, this).Y;

        return [.. _engine.LinesBetween(top, top + (height / zoom))
                         .Select(line => line with { Top = at + ((line.Top - top) * zoom), Height = line.Height * zoom })];
    }

    /// <summary>
    /// The face and size the content is set in as typed, as it is drawn on the page, and the ink its text is set in — for what stands
    /// beside its lines.
    /// </summary>
    internal (Typeface Face, double Size, Brush Ink) TypedIn
    {
        get
        {
            var style = _drawnIn ?? Drawn;
            var mono = Nexaflow.Markdown.Layout.PlainText.TextSetting.Characters(style);

            return (((WpfFace)mono.Face).Typeface, mono.Size * _shown.Zoom, ((WpfInk)style.Text).Brush);
        }
    }

    // ── What it is ──────────────────────────────────────────────────────────

    /// <summary>
    /// The document shown and written in: a plain object the page holds (<see cref="EditedDocument"/>), which says itself what
    /// changed and whether that is what was saved, so nothing of the text is handed back to the page as it is written. A page that
    /// only shows text binds it through <see cref="AsEditedDocument"/>; one that is written in holds its document and reads the
    /// characters when it saves them.
    /// </summary>
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(EditedDocument), typeof(NexaTextSurface),
        new PropertyMetadata(null, (surface, args) => ((NexaTextSurface)surface).Holding((EditedDocument?)args.NewValue)));

    /// <summary>The document shown and written in — the page's, where it handed one over, and otherwise one of the surface's own.</summary>
    public EditedDocument Document
    {
        get => _engine.EditedDocument;
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>The document a page handed over, taken up as it stands; nothing handed over is a document of nothing written.</summary>
    private void Holding(EditedDocument? document) => _engine.EditedDocument = document ?? new EditedDocument();

    /// <summary>Whether it is only read. A read document can still be selected, searched and copied from.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(NexaTextSurface),
        new PropertyMetadata(true, (surface, _) => ((NexaTextSurface)surface).Remake()));

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
        nameof(WrittenIn), typeof(string), typeof(NexaTextSurface),
        new PropertyMetadata(null, (surface, _) => ((NexaTextSurface)surface).Rewritten()));

    public string? WrittenIn
    {
        get => (string?)GetValue(WrittenInProperty);
        set => SetValue(WrittenInProperty, value);
    }

    /// <summary>The language the content is written in, or null for a document.</summary>
    private string? Named => string.IsNullOrWhiteSpace(WrittenIn) ? null : WrittenIn.Trim();

    /// <summary>
    /// Shows the characters written rather than what they draw — for when the drawing itself is the trouble, a formula that
    /// will not set. Everything else about writing in it carries on as it was.
    /// </summary>
    public static readonly DependencyProperty EditAsSourceProperty = DependencyProperty.Register(
        nameof(EditAsSource), typeof(bool), typeof(NexaTextSurface),
        new PropertyMetadata(false, (surface, _) => ((NexaTextSurface)surface).HoldAsWritten()));

    public bool EditAsSource
    {
        get => (bool)GetValue(EditAsSourceProperty);
        set => SetValue(EditAsSourceProperty, value);
    }

    /// <summary>
    /// Whether a line too long for the room carries on in rows beneath — for content shown as typed (<see cref="ContentLanguages.AsTyped"/>),
    /// a page's source read as a page — rather than laid as its language lays it.
    /// </summary>
    public static readonly DependencyProperty WrapsLinesProperty = DependencyProperty.Register(
        nameof(WrapsLines), typeof(bool), typeof(NexaTextSurface),
        new PropertyMetadata(false, (surface, _) => ((NexaTextSurface)surface).Wrapped()));

    public bool WrapsLines
    {
        get => (bool)GetValue(WrapsLinesProperty);
        set => SetValue(WrapsLinesProperty, value);
    }

    // ── How it is drawn ─────────────────────────────────────────────────────

    /// <summary>
    /// The colours it is drawn in. Unset, it follows the theme (<see cref="StyleFormat.FromTheme"/>); a fixed light
    /// surface — a post-it — says <see cref="WpfStyles.Light"/>.
    /// </summary>
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(LayoutStyle), typeof(NexaTextSurface),
        new PropertyMetadata(null, (surface, _) => ((NexaTextSurface)surface).Remake()));

    public LayoutStyle? Palette
    {
        get => (LayoutStyle?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>
    /// The size body text is set at, with everything else in proportion. Unset (<c>NaN</c>) it is the shell's text
    /// size; a host with a zoom of its own binds the zoomed size here, and it applies at once even while being written in.
    /// </summary>
    public static readonly DependencyProperty BaseFontSizeProperty = DependencyProperty.Register(
        nameof(BaseFontSize), typeof(double), typeof(NexaTextSurface),
        new PropertyMetadata(double.NaN, (surface, _) => ((NexaTextSurface)surface).Remake()));

    public double BaseFontSize
    {
        get => (double)GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    /// <summary>What is shown where nothing is written yet, until something is.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(NexaTextSurface),
        new PropertyMetadata(string.Empty, (surface, _) => ((NexaTextSurface)surface).Prompted()));

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
        nameof(ContentPadding), typeof(Thickness), typeof(NexaTextSurface),
        new PropertyMetadata(default(Thickness), (surface, _) => ((NexaTextSurface)surface).Padded()));

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
        nameof(BaseDirectory), typeof(string), typeof(NexaTextSurface),
        new PropertyMetadata(null, (surface, _) => ((NexaTextSurface)surface).Hosted()));

    public string? BaseDirectory
    {
        get => (string?)GetValue(BaseDirectoryProperty);
        set => SetValue(BaseDirectoryProperty, value);
    }

    /// <summary>
    /// Whether the document, given no width at all, is laid out with none to keep to — see
    /// <see cref="ContentElement.UnlimitedWidth"/>. For a surface that is the whole of a pan and zoom viewport, which fits a
    /// diagram itself and wants it at its own size rather than drawn smaller to fit a page.
    /// </summary>
    public static readonly DependencyProperty UnlimitedWidthProperty = DependencyProperty.Register(
        nameof(UnlimitedWidth), typeof(bool), typeof(NexaTextSurface),
        new PropertyMetadata(false, (surface, _) => ((NexaTextSurface)surface).Remake()));

    public bool UnlimitedWidth
    {
        get => (bool)GetValue(UnlimitedWidthProperty);
        set => SetValue(UnlimitedWidthProperty, value);
    }

    /// <summary>
    /// Whether this scrolls itself, or lets whatever holds it do the scrolling — the second being what a reply in a
    /// conversation wants, where every reply scrolling separately would be unusable, and so the default. Scrolling itself, the engine keeps
    /// the view of the page, and the page is drawn from where it stands.
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => _vertical;
        set
        {
            if (_vertical == value) return;

            _vertical = value;
            _engine.Scrolls = value != ScrollBarVisibility.Disabled;

            Padded();
            _shown.InvalidateMeasure();
            _shown.InvalidateVisual();
            Shows(_shown);
            Synced();
        }
    }

    // ── What the host says ──────────────────────────────────────────────────

    /// <summary>What answers the verbs this document raises and does not answer itself — saving a picture, and anything a language offers.</summary>
    public ILayoutActions? Host
    {
        get => _host;
        set { _host = value; _engine.Answers(value); }
    }

    private ILayoutActions? _host;

    /// <summary>The host's say in where a picture comes from, asked before <see cref="BaseDirectory"/>.</summary>
    public Func<string, ImageSource?>? ImageResolver { get => _pictures; set { _pictures = value; Hosted(); } }

    private Func<string, ImageSource?>? _pictures;

    /// <summary>
    /// The host's say in how a link looks, asked for every link with the URL as written and the words it was written as —
    /// which is what lets the help pane mark a <c>locate:</c> link without disturbing those words.
    /// </summary>
    public Func<string, string, LinkLook?>? LinkDecorator { get => _links; set { _links = value; Hosted(); } }

    private Func<string, string, LinkLook?>? _links;

    /// <summary>What a <c>{{…}}</c> written in a diagram is read against. Null leaves one drawn as it was written.</summary>
    public Nexaflow.Markdown.Binding.IDataContext? DataSource { get => _data; set { _data = value; Hosted(); } }

    private Nexaflow.Markdown.Binding.IDataContext? _data;

    /// <summary>The element the document is drawn on — where the caret, what is picked out and the laid tree live.</summary>
    public MarkdownElement Shown => _shown;

    /// <summary>What lays the document out — for a test asking what only the engine is told, a pointer resting on the page or the rest of a long document read.</summary>
    internal ContentEngine Engine => _engine;

    /// <summary>Where the view's top stands, as its bar counts it, in this surface's pixels — for a test or a bench moving through the document as a reader does.</summary>
    internal double VerticalOffset => _engine.ScrolledDown * _shown.Zoom;

    /// <summary>How far down the view's top can go, as its bar counts it.</summary>
    internal double ScrollableHeight => Math.Max(0, (_engine.Extent.Height * _shown.Zoom) - ViewportHeight);

    /// <summary>How tall the view is, in this surface's pixels.</summary>
    internal double ViewportHeight => _shown.ActualHeight;

    /// <summary>How tall the page is, room round it and all, as the view's bar counts it.</summary>
    internal double ExtentHeight => _engine.Extent.Height * _shown.Zoom;

    /// <summary>How wide the page is, its widest line and the room round it, as the view's sideways bar counts it.</summary>
    internal double ExtentWidth => _engine.Extent.Width * _shown.Zoom;

    /// <summary>How wide the view is, in this surface's pixels.</summary>
    internal double ViewportWidth => _shown.ActualWidth;

    /// <summary>Scrolls the view so its top stands <paramref name="offset"/> down, as its bar counts it.</summary>
    internal void ScrollToVerticalOffset(double offset) => _engine.ScrollTo(offset / _shown.Zoom);

    /// <summary>Where the view's left edge stands, as its sideways bar counts it, in this surface's pixels.</summary>
    internal double HorizontalOffset => _engine.ScrolledAcross * _shown.Zoom;

    /// <summary>How far along the view's left edge can go, as its sideways bar counts it.</summary>
    internal double ScrollableWidth => Math.Max(0, ExtentWidth - ViewportWidth);

    /// <summary>Slides the view so its left edge stands <paramref name="offset"/> along, as its sideways bar counts it.</summary>
    internal void ScrollToHorizontalOffset(double offset) => _engine.SlideViewTo(offset / _shown.Zoom);

    /// <summary>Forgets what the reader had opened and chosen in every diagram here, and draws them as their sources say.</summary>
    public void ResetDiagramViews()
    {
        _engine.CloseDiagrams();
        _shown.Refresh();
    }

    // ── Showing it ──────────────────────────────────────────────────────────

    /// <summary>The colours and size it is drawn at, as they stand: the size the host gave, or the shell's where it gave none.</summary>
    private LayoutStyle Drawn =>
        (Palette ?? WpfStyles.FromTheme()) with
        {
            TextSize = double.IsNaN(BaseFontSize) || BaseFontSize <= 0 ? TextTypography.BaseFontSize : BaseFontSize,
            TabWidth = TabWidth,
            ShowsWhitespace = ShowsWhitespace,
        };

    /// <summary>What the host said about what the document holds, as the engine is told it.</summary>
    private ContentInputs Asked => new(WpfPicture.Finding(MarkdownPictures.Found(_pictures, BaseDirectory)), _links, _data);

    /// <summary>Tells the engine what the host now says, and lays the document out again by it.</summary>
    private void Hosted()
    {
        if (_shown is null) return;

        _engine.Inputs = Asked;
        _shown.Refresh();
    }

    /// <summary>After a different document is shown: nothing of the last one can be taken back, found or held open.</summary>
    private void Settled()
    {


        _engine.CaretWhereReadingStarts();

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
        _shown = Made(null);
        _shown.Restore(state);

        // Laid now rather than at the next layout pass, so what is asked of it in between — a selection, a search, the
        // formula under the caret — has a tree to ask.
        _shown.Refresh();
        if (caret) _shown.TakeCaret(state.Caret);
    }

    /// <param name="source">What to show, loaded in place of what the document holds — or null to show what it holds as it is.</param>
    private MarkdownElement Made(string? source)
    {
        var style = Drawn;

        _drawnIn = style;
        _engine.Inputs = Asked;

        var element = new MarkdownElement(source, style, Host, _engine, Named)
        {
            IsReadOnly = IsReadOnly,
            UnlimitedWidth = UnlimitedWidth,
            SlidingRoom = SlidingRoom(),
            HighlightsCaretLine = HighlightsCaretLine,
        };


        element.CaretMoved += (_, _) => Reveal();

        _frame.Child = element;

        _shown = element;
        Padded();
        Shows(element);

        return element;
    }

    /// <summary>
    /// Tells <paramref name="element"/>, shown whole inside something that scrolls it, that all of it may be on screen — what that scrolls is not
    /// this one's to know. Content that scrolls itself is told nothing: the engine keeps its view.
    /// </summary>
    private void Shows(MarkdownElement? element)
    {
        if (element is null || _engine.Scrolls || element.ActualHeight <= 0 || element.ActualWidth <= 0) return;

        element.OnScreen = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
    }

    /// <summary>
    /// Keeps the room asked for round the document: part of what scrolls, for content that scrolls itself — so the room above it scrolls away
    /// with the first line, as it would on a page — and room round the element for content shown whole.
    /// </summary>
    private void Padded()
    {
        var padding = ContentPadding;
        var zoom = _shown.Zoom;

        if (_engine.Scrolls)
        {
            _shown.Margin = default;
            _engine.Inset(padding.Left / zoom, padding.Top / zoom, padding.Right / zoom, padding.Bottom / zoom);
        }
        else
        {
            _shown.Margin = padding;
            _engine.Inset(0, 0, 0, 0);
        }

        // The prompt stands where the first word would, so it is inset as the document is.
        _prompt.Margin = padding;
    }

    /// <summary>Shows the prompt while nothing is written, and takes it away the moment something is.</summary>
    private void Prompted()
    {
        _prompt.Text = Placeholder ?? string.Empty;
        _prompt.FontSize = Drawn.TextSize;
        _prompt.Visibility = !string.IsNullOrEmpty(Placeholder) && Document.Current.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>Lays what is shown again with its lines wrapped, or kept whole, as <see cref="WrapsLines"/> now says.</summary>
    private void Wrapped()
    {
        _engine.WrapsLines = WrapsLines;
        Remake();
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new NexaTextSurfaceAutomationPeer(this);

    // ── Links ───────────────────────────────────────────────────────────────

    /// <summary>Scrolls the heading a <c>#anchor</c> link names into view; false where the document has no such heading.</summary>
    public bool ScrollToAnchor(string anchor) => GoTo(new ContentPath([new ContentStep(MarkdownKinds.Heading, anchor)]));

    /// <summary>
    /// Says that a heading of the document came, went, or now says or answers to something else — once the change is laid out, and once
    /// however many changes came together.
    /// </summary>
    public event EventHandler? OutlineChanged;

    /// <summary>Whether the outline has changed since it was last said to have.</summary>
    private bool _outlineChanging;

    /// <summary>Says the outline changed, once the laying out that changed it is done.</summary>
    private void Outlined(object? sender, EventArgs e)
    {
        if (_outlineChanging) return;

        _outlineChanging = true;
        Dispatcher.BeginInvoke(() =>
        {
            _outlineChanging = false;
            OutlineChanged?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Background);
    }

    // ── What a block offers ─────────────────────────────────────────────────

    /// <summary>
    /// A picture of one block as it is on the page, for a host keeping one of what a corner button was pressed on —
    /// painted from the page's own tree, cut to where the block came out, so nothing is read again to make it.
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? CapturePicture(ContentPart block, Brush? ground = null) =>
        Pictured(_engine.Where(block).ToWpf(), _shown.Zoom, ground);

    /// <summary>
    /// A picture of the whole document as it stands, no bigger than <paramref name="within"/> — for a host showing an
    /// overview of content too big to see at once, which is what a diagram on a viewport of its own needs. Never
    /// enlarged past the size it was laid out at.
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? CapturePicture(Size within, Brush? ground = null)
    {
        var size = _shown.Laid.Size;
        if (within.Width <= 0 || within.Height <= 0 || size.Width <= 0 || size.Height <= 0) return null;

        var fit = Math.Min(1, Math.Min(within.Width / size.Width, within.Height / size.Height));

        return Pictured(new Rect(0, 0, size.Width, size.Height), fit * _shown.Zoom, ground);
    }

    /// <summary>What a picture of <paramref name="box"/> of the page comes to, drawn at <paramref name="scale"/>.</summary>
    private System.Windows.Media.Imaging.BitmapSource? Pictured(Rect box, double scale, Brush? ground)
    {
        var dpi = VisualTreeHelper.GetDpi(this);

        return WpfPainter.Painted(_shown.Laid.Root, box, scale, (_drawnIn ?? Drawn).Text.Brush(), ground, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
    }
}
