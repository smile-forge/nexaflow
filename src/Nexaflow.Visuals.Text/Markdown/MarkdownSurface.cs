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

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A whole markdown document on one page: the scroller, the element that draws it, what a search turned up,
/// and the buttons a block offers in its own corner.
///
/// <para>
/// <strong>One element, not one per block.</strong> Everything in the document is pieces of a single laid
/// tree — the prose, the diagrams, the tunes — so a drag runs from a word into a chart without anything
/// forwarding gestures between controls, and a search looks in one place.
/// </para>
/// <para>
/// <strong>It says what things mean and the host does them.</strong> Copying, saving a picture and
/// following a link out of the document are all raised as verbs (<see cref="ILayoutActions"/>) rather than
/// done here: a clipboard, a file and a browser are the application's, shared with everything else in the
/// window. What is this surface's is what only it can know — where in the document something is, and what
/// is on the page.
/// </para>
/// </summary>
public sealed class MarkdownSurface : UserControl
{
    private readonly ScrollViewer _scroller;
    private readonly Border _corner;
    private readonly StackPanel _buttons;

    private MarkdownElement _shown;
    private ContentPart? _over;

    /// <summary>What the element now on the page was built with, so it is only made again when that changes.</summary>
    private StyleFormat _drawn = StyleFormat.Dark;
    private bool _writable;

    /// <summary>
    /// The document as it reads, kept beside the drawing. One read per document rather than one per
    /// pointer move — and it is what says what the document is <em>made of</em>, which the drawing does
    /// not: a paragraph that needed no piece of its own is still a paragraph.
    /// </summary>
    private ContentPart _read = ContentPart.Of(ContentNode.Branch(MarkdownKinds.Document, []));

    public MarkdownSurface()
    {
        Focusable = true;
        _shown = Made(string.Empty);
        _shown.SourceChanged += (_, _) => Reread();

        _buttons = new StackPanel { Orientation = Orientation.Horizontal };

        // Semi-transparent, in the corner, out of the way of the words until the pointer comes near.
        _corner = new Border
        {
            Child = _buttons,
            Opacity = 0.75,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 12, 0),
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(3),
        };

        _scroller = new ScrollViewer
        {
            Content = _shown,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        base.Content = new Grid { Children = { _scroller, _corner } };

        MouseMove += (_, args) => Over(args.GetPosition(_shown));
        MouseLeave += (_, _) => Over(null);
    }

    /// <summary>What is being shown.</summary>
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownSurface),
        new FrameworkPropertyMetadata(string.Empty, (surface, args) =>
            ((MarkdownSurface)surface).Shows((string?)args.NewValue ?? string.Empty)));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>What answers the verbs this document raises and cannot answer itself.</summary>
    public ILayoutActions? Host { get; set; }

    /// <summary>What it is drawn in, and what the host said about the things inside it.</summary>
    public StyleFormat Palette { get; set; } = StyleFormat.Dark;

    /// <summary>What the host said about the content written inside the document.</summary>
    public DiagramRenderOptions? Options { get; set; }

    /// <summary>Whether a reader may write in it.</summary>
    public bool IsReadOnly { get; set; } = true;

    /// <summary>The element the document is drawn on — where a caret, a selection and the tree live.</summary>
    public MarkdownElement Shown => _shown;

    // ── Looking for something ───────────────────────────────────────────────

    /// <summary>Everywhere the document says <paramref name="term"/>, in the order a reader comes to them.</summary>
    public IReadOnlyList<(int Start, int Length)> Found { get; private set; } = [];

    /// <summary>Which of them is being looked at, or -1 for none.</summary>
    public int At { get; private set; } = -1;

    /// <summary>
    /// Looks for <paramref name="term"/> and goes to the first place it is — both where it is written and
    /// where it is only drawn, which is what a reader means by what the page says.
    /// </summary>
    public int Find(string? term)
    {
        Found = MarkdownFind.In(_shown.Laid, _shown.Markdown, term);
        At = -1;

        _shown.Showing = (Found, At);

        if (Found.Count > 0) Next();

        return Found.Count;
    }

    /// <summary>The next place, coming back round to the first.</summary>
    public bool Next() => Goes(At + 1);

    /// <summary>The one before, coming back round to the last.</summary>
    public bool Previous() => Goes(At - 1);

    /// <summary>Stops looking, which is a repaint and nothing else.</summary>
    public void Stop()
    {
        Found = [];
        At = -1;

        _shown.Showing = ([], -1);
    }

    private bool Goes(int to)
    {
        if (Found.Count == 0) return false;

        At = (to + Found.Count) % Found.Count;

        _shown.Showing = (Found, At);
        _shown.Show(Found[At]);

        return true;
    }

    // ── Going to a place ────────────────────────────────────────────────────

    /// <summary>Goes to a line, counting from one — and to the whole of whatever drew it, where a line is inside a picture.</summary>
    public bool GoTo(int line) => _shown.Show(MarkdownFind.Line(_shown.Markdown, line), choose: false);

    /// <summary>Goes where a saved reference leads, or as far as it still does.</summary>
    public bool GoTo(ContentPath path) => MarkdownFind.Followed(_read, path) is { } place && _shown.Show(place);

    /// <summary>How to find what is picked out again later, after the document has been written in.</summary>
    public ContentPath Reference() =>
        ContentPath.Of(_shown.Laid.Root.PieceAt(Middle()).Part as ContentPart);

    // ── What a block offers ─────────────────────────────────────────────────

    /// <summary>
    /// Shows the buttons for whatever block the pointer is over, and takes them away when it leaves.
    ///
    /// <para>
    /// What they are is the language's to say — a picture of a diagram is worth keeping and a picture of a
    /// code fence is a worse copy of the code — so this asks rather than deciding.
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
    /// The block a point is in: not the word or the run under it, but the thing the document is made of that
    /// holds them — a paragraph, a table, a fence.
    ///
    /// <para>
    /// Found by the offset rather than by walking up from the piece. A piece knows the characters it was drawn
    /// from, but not always as a part of this document's tree: a code fence's runs carry plain spans, because
    /// the thing that drew them was reading code and not markdown. An offset is an offset whatever drew it,
    /// and every language is laid at the offset its body starts at — so this is the one question that works
    /// the same everywhere.
    /// </para>
    /// </summary>
    private ContentPart? Blocked(Point at)
    {
        var piece = _shown.Laid.Root.PieceAt(at);
        if (!piece.Exists) return null;

        var offset = piece.Sits().Start;

        foreach (var block in _read.Children)
            if (!block.Derived && block.Role != Roles.Trivia && offset >= block.Start && offset < block.End)
                return block;

        return null;
    }

    /// <summary>Where a block came out on the page, which is every rect the characters it holds were drawn at.</summary>
    private Rect Where(ContentPart block)
    {
        var rects = _shown.Laid.Root.RangeRects(block.Start, Math.Max(block.Length, 1));
        if (rects.Count == 0) return Rect.Empty;

        var box = rects[0];

        foreach (var rect in rects) box = Rect.Union(box, rect);

        return box;
    }

    /// <summary>
    /// What the block at a point offers in its corner — whichever of the usual buttons it allows, and
    /// whatever it adds of its own.
    /// </summary>
    public IReadOnlyList<LayoutIntent> Corner(Point at) =>
        Blocked(at) is { } block ? [.. Offered(block)] : [];

    /// <summary>What a block's corner offers: whichever of the usual ones it allows, and whatever it adds.</summary>
    private IEnumerable<LayoutIntent> Offered(ContentPart block)
    {
        var corner = Asked(block);

        if (corner.Copies) yield return new LayoutIntent(LayoutVerbs.Copy, null, "Copy");
        if (corner.Saves) yield return new LayoutIntent(LayoutVerbs.Save, null, "Save as a picture");

        foreach (var added in corner.Adds) yield return added;
    }

    /// <summary>What the language drawing a block says about its corner, or the usual where no language is.</summary>
    private BlockCorner Asked(ContentPart block)
    {
        for (var at = block; at is not null; at = at.Parent)
        {
            if (ContentNesting.Of(at) is not { } nesting) continue;
            if (at.Part(Roles.Body) is not { } body) continue;

            return nesting.Language.Corner(new ContentAsk(nesting.Named, body.Text) { Part = block, IsReadOnly = IsReadOnly });
        }

        // Prose is not a picture of anything, so there is nothing to keep a picture of.
        return new BlockCorner(Saves: false);
    }

    private FrameworkElement Button(LayoutIntent offer, Point at) =>
        new Button
        {
            Content = DiagramRibbon.Names(offer),
            Margin = new Thickness(1),
            Padding = new Thickness(6, 1, 6, 1),
            Command = new Does(() => Raise(offer, at)),
        };

    /// <summary>
    /// Hands a verb to the host, with what it would need — the characters of the block, and what copying
    /// them would amount to, so the host has only to put it somewhere.
    /// </summary>
    public void Raise(LayoutIntent offer, Point at)
    {
        if (Host is null || Blocked(at) is not { } block) return;

        var said = offer.Verb == LayoutVerbs.Copy
            ? offer with { Target = MarkdownClipboard.Copied(_shown.Markdown, (block.Start, block.Length)).Markdown }
            : offer;

        var piece = _shown.Laid.Root.PieceAt(at);

        Host.Invoke(new LayoutAct(LayoutGesture.Click, said, piece, block, block, [piece], at));
    }

    private void Raise(LayoutIntent offer, Piece block)
    {
        if (Host is null || block.Part is not ContentPart part) return;

        var said = offer.Verb == LayoutVerbs.Copy
            ? offer with { Target = MarkdownClipboard.Copied(_shown.Markdown, (part.Start, part.Length)).Markdown }
            : offer;

        Host.Invoke(new LayoutAct(LayoutGesture.Click, said, block, part, part, [block], block.Bounds.TopLeft));
    }

    // ── Showing it ──────────────────────────────────────────────────────────

    private void Shows(string markdown)
    {
        if (ReferenceEquals(_shown.Markdown, markdown)) return;

        // A palette or a host set after the document is what a XAML host does, so the element is made
        // again rather than asked to change what it was built with.
        if (_writable == IsReadOnly || !ReferenceEquals(_drawn, Palette))
        {
            _shown = Made(markdown);
            _shown.SourceChanged += (_, _) => Reread();
            _scroller.Content = _shown;
        }
        else
        {
            _shown.Markdown = markdown;
        }

        Reread();
        Stop();
    }

    private MarkdownElement Made(string markdown)
    {
        (_drawn, _writable) = (Palette, !IsReadOnly);

        return new MarkdownElement(markdown, Palette, Host) { IsReadOnly = IsReadOnly };
    }

    /// <summary>Reads the document again, which is what a changed source means.</summary>
    private void Reread() =>
        _read = ContentPart.Of(MarkdownParser.Reader.Run(MarkdownParser.Read(_shown.Markdown)));

    private Point Middle() =>
        new(_scroller.HorizontalOffset + (_scroller.ViewportWidth / 2),
            _scroller.VerticalOffset + (_scroller.ViewportHeight / 2));

    /// <summary>A command that is one thing done, which is all a button in a corner needs.</summary>
    private sealed class Does(Action what) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => what();
    }
}
