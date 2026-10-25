using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Layout.Prose;

/// <summary>
/// What a document is drawn in, said in a medium's own handles: its inks, its faces, its text size, and what the reader has
/// folded. Made once for a showing by whatever hosts it, as a theme is resolved once, so laying a document looks nothing up.
/// </summary>
public sealed class PortableStyle
{
    private readonly Func<string, int, bool, LayoutFace> _faces;
    private readonly Dictionary<(string Family, int Weight, bool Italic), LayoutFace> _known = [];

    /// <param name="faces">The face for a family at a weight — 400 regular, 700 bold — slanted or not: asked once for each, and kept.</param>
    public PortableStyle(Func<string, int, bool, LayoutFace> faces) => _faces = faces;

    public required LayoutInk Text { get; init; }
    public required LayoutInk TextMuted { get; init; }
    public required LayoutInk Accent { get; init; }
    public required LayoutInk Heading { get; init; }
    public required LayoutInk DefTerm { get; init; }
    public required LayoutInk Citation { get; init; }
    public required LayoutInk Marked { get; init; }
    public required LayoutInk Success { get; init; }
    public required LayoutInk Warning { get; init; }
    public required LayoutInk Danger { get; init; }
    public required LayoutInk Important { get; init; }
    public required LayoutInk CodeBg { get; init; }
    public required LayoutInk CodeBorder { get; init; }
    public required LayoutInk QuoteBg { get; init; }
    public required LayoutInk Hr { get; init; }
    public required LayoutInk TableBorder { get; init; }
    public required LayoutInk TableHeaderBg { get; init; }
    public required LayoutInk TableAltRowBg { get; init; }
    public required LayoutInk FigureBg { get; init; }
    public required LayoutInk FigureBorder { get; init; }
    public required LayoutInk FooterBg { get; init; }

    /// <summary>A scannable code's dark modules and the light field round them — dark on light whatever the theme, which a scanner needs.</summary>
    public required LayoutInk QrDark { get; init; }

    public required LayoutInk QrLight { get; init; }

    /// <summary>
    /// A barcode's bars and the stock behind them: a pair of their own rather than the QR code's, since a barcode is routinely printed in
    /// a brand colour and a QR code rarely is.
    /// </summary>
    public required LayoutInk BarcodeDark { get; init; }

    public required LayoutInk BarcodeLight { get; init; }

    /// <summary>Distinct colours a chart takes in turn for its series — round again past the last.</summary>
    public required IReadOnlyList<LayoutInk> Series { get; init; }

    /// <summary>
    /// The colours a chemical structure draws its elements in, by symbol. An element that is not here, carbon among them, is drawn in
    /// <see cref="Text"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, LayoutInk> Elements { get; init; }

    /// <summary>
    /// The colours a theme names for C4's levels — a person, a system, a container, a component, a deployment node, something somebody
    /// else's — and its boundaries, each null where the theme names none and C4 grades from the accent instead.
    /// </summary>
    public LayoutInk? C4Person { get; init; }

    public LayoutInk? C4System { get; init; }

    public LayoutInk? C4Container { get; init; }

    public LayoutInk? C4Component { get; init; }

    public LayoutInk? C4DeploymentNode { get; init; }

    public LayoutInk? C4External { get; init; }

    public LayoutInk? C4Boundary { get; init; }

    /// <summary>What a kind of code token is drawn in, or null where the theme says nothing about it — asked once a kind.</summary>
    public Func<string, LayoutInk?> Syntax { get; init; } = static _ => null;

    /// <summary>The family body text is set in.</summary>
    public required string TextFont { get; init; }

    /// <summary>The family code, and source shown as written, is set in.</summary>
    public required string MonoFont { get; init; }

    /// <summary>The face an icon content names is drawn in — the host's icon font, whose glyphs the names are read into.</summary>
    public required LayoutFace IconFace { get; init; }

    /// <summary>The size text is set at where nothing sets another — what a diagram's page is measured against.</summary>
    public const double DefaultTextSize = 13.5;

    public double TextSize { get; init; } = DefaultTextSize;

    /// <summary>What the reader has opened and folded, by the name each is kept by — null where nothing is kept.</summary>
    public IReadOnlyDictionary<string, bool>? Expansion { get; init; }

    /// <summary>What a key of the window's language says — the words on a tick's tip.</summary>
    public required Func<string, string> Says { get; init; }

    /// <summary>The face for <paramref name="family"/>, found once and kept.</summary>
    public LayoutFace Face(string family, bool bold = false, bool italic = false) => Face(family, bold ? 700 : 400, italic);

    /// <summary>The face for <paramref name="family"/> at <paramref name="weight"/> — what a diagram's semibold heading is set in — found once and kept.</summary>
    public LayoutFace Face(string family, int weight, bool italic = false)
    {
        if (_known.TryGetValue((family, weight, italic), out var known)) return known;

        return _known[(family, weight, italic)] = _faces(family, weight, italic);
    }
}

/// <summary>Where content stands in what holds it.</summary>
public enum PortableStanding
{
    Alone,
    Block,
    Inline,
}

/// <summary>What a host wants a link to look like — said in a medium's inks.</summary>
public sealed record PortableLinkLook(LayoutInk? Ink = null, bool Underline = true, string? Before = null, string? After = null,
                                      string? Says = null)
{
    /// <summary>The family the marks are set in, where the reading face lacks them.</summary>
    public string? MarkFont { get; init; }
}

/// <summary>A stretch of source that could not be made sense of, and why.</summary>
public sealed record PortableDiagnostic(int Start, int Length, string Message)
{
    public static PortableDiagnostic Of(ISourcePart part, string message, PortableSeverity severity = PortableSeverity.Error) =>
        new(part.Start, Math.Max(part.Length, 1), message) { Part = part, Severity = severity };

    public ISourcePart? Part { get; init; }

    public PortableSeverity Severity { get; init; }
}

/// <summary>How much is wrong: nothing could be made of it, or what was drawn is not quite what was asked.</summary>
public enum PortableSeverity
{
    Error,
    Warning,
}

/// <summary>What a builder makes: a tree, how much room it wants, and whatever could not be read.</summary>
public sealed record PortableLaid(PortableTree Tree, double Width, double Height, IReadOnlyList<PortableDiagnostic> Trouble)
{
    public PortablePiece Root => Tree.Root;

    /// <summary>Whether anything reached the page: a run of words, or a mark of any kind.</summary>
    public bool Draws
    {
        get
        {
            for (var at = 0; at < Tree.Count; at++)
                if (Tree.WordsOf(at) is not null || Tree.MarksOf(at).Length > 0) return true;

            return false;
        }
    }

    /// <summary>Whether this is the source shown as it is written, rather than anything drawn from it.</summary>
    public bool ShowsSource => Root.Kind is PortableText.SourceKind or PortableSourceShown.Block;
}

/// <summary>Another language's content, laid out and ready to be set down inside this one's.</summary>
public sealed record PortableInset(PortableLaid Laid)
{
    public double Width => Laid.Width;

    public double Height => Laid.Height;

    public bool Draws => Laid.Draws;

    /// <summary>Sets it down at <paramref name="at"/>, as a piece of <paramref name="kind"/> holding the whole of it.</summary>
    public void Set(PortableBuilder build, LayoutPoint at, string kind)
    {
        build.Open(kind, part: null, stops: PortableStops.None);
        build.Graft(Laid.Tree, at);
        build.Close();
    }
}

/// <summary>
/// What <paramref name="holder"/> holds in another language, laid out at <paramref name="room"/> standing as
/// <paramref name="standing"/> says — or null where nothing read it. The one thing a builder asks of the engine.
/// </summary>
public delegate PortableInset? PortableNesting(ContentPart holder, double room, PortableStanding standing);

/// <summary>What a piece of a laid-out markdown document is.</summary>
public static class PortablePieces
{
    public const string Document = "MarkdownDocument";
    public const string Whole = "MarkdownWhole";
    public const string Block = "MarkdownBlock";
    public const string Words = "MarkdownWords";
    public const string Marker = "MarkdownMarker";
    public const string Tick = "MarkdownTick";
    public const string Rule = "MarkdownRule";
    public const string Fold = "MarkdownFold";
    public const string Page = "MarkdownPage";
    public const string Row = "MarkdownRow";
    public const string Cell = "MarkdownCell";
    public const string Verbatim = "MarkdownVerbatim";
    public const string Picture = "MarkdownPicture";

    /// <summary>Tick an item off, or take the tick back.</summary>
    public const string TickVerb = "tick";
}