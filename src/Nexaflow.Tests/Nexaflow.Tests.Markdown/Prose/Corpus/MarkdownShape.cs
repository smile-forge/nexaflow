using System.Globalization;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Tests.Markdown.Prose.Corpus;

/// <summary>
/// What a reading of a document says, put so that two readings of different shapes can be compared — the document read whole
/// and read a stretch at a time: the set of constructs it found, each a kind, where its first and last characters stand, and
/// the facts it carries.
///
/// <para>
/// Nesting is in the ranges, so neither tree's shape has to agree with the other's — only what each says is there. The kinds
/// are the reading's own (<see cref="MarkdownKinds"/>) wherever one kind is one construct, and a name of their own where one
/// of ours is two: code is a block and a span, a formula sits in a sentence or on its own.
/// </para>
/// </summary>
internal static class MarkdownShape
{
    public const string CodeSpan = "code-span";
    public const string IndentedCode = "code-indented";
    public const string Fence = "fence";
    public const string HtmlBlock = "html-block";
    public const string HtmlInline = "html-inline";
    public const string Formula = "formula";
    public const string DisplayedFormula = "formula-display";
    public const string AbbreviationDefinition = "abbreviation-definition";

    /// <summary>A stretch the reading could not read and shows as it was typed.</summary>
    public const string ShownAsWritten = "shown-as-written";

    /// <summary>A block of a kind the reading has no reader for.</summary>
    public const string Unread = "unread";

    /// <summary>The kinds that are one construct, by the reading's own name.</summary>
    private static readonly HashSet<string> Same =
    [
        MarkdownKinds.Paragraph, MarkdownKinds.Heading, MarkdownKinds.Rule, MarkdownKinds.Quote, MarkdownKinds.Alert,
        MarkdownKinds.List, MarkdownKinds.Item, MarkdownKinds.Task, MarkdownKinds.Table, MarkdownKinds.Row, MarkdownKinds.Cell,
        MarkdownKinds.Definition, MarkdownKinds.Term, MarkdownKinds.FrontMatter, MarkdownKinds.Reference,
        MarkdownKinds.Emphasis, MarkdownKinds.Strong, MarkdownKinds.Strike, MarkdownKinds.Mark, MarkdownKinds.Insert,
        MarkdownKinds.Sub, MarkdownKinds.Sup, MarkdownKinds.Citation, MarkdownKinds.Image, MarkdownKinds.Entity,
        MarkdownKinds.Break, MarkdownKinds.Abbreviation, MarkdownKinds.Figure, MarkdownKinds.Caption, MarkdownKinds.Footer,
        MarkdownKinds.Typographic, MarkdownKinds.Shortcode, Kinds.Link,
    ];

    /// <summary>The kinds whose insides are not looked into: what is in them is no construct of markdown's.</summary>
    private static readonly HashSet<string> Closed =
    [
        MarkdownKinds.Rule, MarkdownKinds.Task, MarkdownKinds.FrontMatter, MarkdownKinds.Reference, MarkdownKinds.Entity,
        MarkdownKinds.Break, MarkdownKinds.Abbreviation, MarkdownKinds.Typographic, MarkdownKinds.Shortcode, CodeSpan, IndentedCode,
        Fence, HtmlBlock, HtmlInline, Formula,
        DisplayedFormula, AbbreviationDefinition, ShownAsWritten, Unread,
    ];

    /// <summary>What the reading of <paramref name="source"/> says is in it.</summary>
    public static List<MarkdownConstruct> Of(ContentNode reading, string source)
    {
        var shape = new List<MarkdownConstruct>();
        Walk(reading, reading.Offset ?? 0, words: false);
        return shape;

        void Walk(ContentNode node, int stands, bool words)
        {
            if (node.IsDerived) return;

            // A definition is trivia between blocks, and still a construct: it is what the document defines.
            if (node.Role == Roles.Trivia && node.Kind != MarkdownKinds.Reference) return;

            if (!ReferenceEquals(node, reading) && Kind(node, words) is { } kind)
            {
                var (start, end) = Extent(node, stands);
                shape.Add(new MarkdownConstruct(kind, Math.Clamp(start, 0, source.Length), Math.Clamp(end, start, source.Length), Facts(node, kind)));
                if (Closed.Contains(kind)) return;
            }

            var at = stands;
            var inside = words || node.Kind == Kinds.Words;

            foreach (var child in node.Children)
            {
                var begins = child.Offset ?? at;
                Walk(child, begins, inside);
                at = begins + child.Width;
            }
        }
    }

    /// <summary>
    /// What <paramref name="these"/> say is in a document that <paramref name="those"/> do not — counting a construct as often
    /// as it is said.
    /// </summary>
    public static IEnumerable<MarkdownConstruct> Unmatched(IEnumerable<MarkdownConstruct> these, IEnumerable<MarkdownConstruct> those)
    {
        var left = those.GroupBy(construct => construct).ToDictionary(group => group.Key, group => group.Count());

        foreach (var construct in these)
        {
            if (left.TryGetValue(construct, out var count) && count > 0) left[construct] = count - 1;
            else yield return construct;
        }
    }

    /// <summary>
    /// Where a piece's characters start and end: from its first to its last. Not its own offset and width — a grid table's
    /// cell is its slices of lines that hold other cells too, so it is no one stretch.
    /// </summary>
    private static (int Start, int End) Extent(ContentNode node, int stands)
    {
        var pieces = new List<(int Start, int End)>();
        var at = stands;

        Reach(node);

        // Nothing in it takes up any characters — a break between two slices of a cell's lines: it stands where it says.
        var stood = node.Offset ?? stands;
        if (pieces.Count == 0) return (stood, stood + node.Width);

        return (pieces.Min(piece => piece.Start), pieces.Max(piece => piece.End));

        void Reach(ContentNode piece)
        {
            if (piece.IsDerived) return;
            if (piece.Offset is { } offset) at = offset;

            if (piece.IsLeaf)
            {
                if (piece.Width > 0) pieces.Add((at, at + piece.Width));

                at += piece.Width;
                return;
            }

            foreach (var child in piece.Children) Reach(child);
        }
    }

    private static string? Kind(ContentNode node, bool words) => node switch
    {
        BlockNode { Language: MarkdownParser.Maths } => words ? Formula : DisplayedFormula,
        BlockNode => Fence,
        { Kind: Kinds.Verbatim, IsLeaf: true, Role: Roles.Element } => ShownAsWritten,
        { Kind: Kinds.Verbatim, IsLeaf: false, Role: not Roles.Body } => Unread,
        { Kind: MarkdownKinds.Code } => words ? CodeSpan : node.Part(Roles.Open) is null ? IndentedCode : Fence,
        { Kind: MarkdownKinds.Html } => words ? HtmlInline : HtmlBlock,
        { Kind: MarkdownKinds.Formula } => Formula,
        { Kind: MarkdownKinds.Reference } when Said(node, MarkdownKinds.Abbreviation) is not null => AbbreviationDefinition,
        _ => Same.Contains(node.Kind) ? node.Kind : null,
    };

    private static string Facts(ContentNode node, string kind) => kind switch
    {
        MarkdownKinds.Heading => Facts(("rank", Said(node, MarkdownKinds.Rank)), ("id", Said(node, MarkdownKinds.Anchor))),
        MarkdownKinds.List => Facts(("counts", (Said(node, MarkdownKinds.Numbering) ?? (node.Part(Roles.Body) is { } items ? Said(items, MarkdownKinds.Numbering) : null))
                                         is MarkdownNumbering n ? $"{n.Bullet}{n.Start.ToString(CultureInfo.InvariantCulture)}" : null),
                                    ("loose", (Said(node, MarkdownKinds.Loose) ?? (node.Part(Roles.Body) is { } held ? Said(held, MarkdownKinds.Loose) : null)) is true)),
        MarkdownKinds.Task => Facts(("done", node.Children.Any(child => child.Role == MarkdownRoles.Done))),
        MarkdownKinds.Row => Facts(("head", node.Role == MarkdownRoles.Head)),
        MarkdownKinds.Cell => Facts(("align", Said(node, MarkdownKinds.Aligned)),
                                    ("spans", Said(node, MarkdownKinds.Spans) is MarkdownSpans s ? $"{s.Across}x{s.Down}" : null)),
        MarkdownKinds.Alert => Facts(("name", node.SelfAndDescendants().FirstOrDefault(part => part.Role == Roles.Name && part.IsLeaf)?.Text)),
        Kinds.Link or MarkdownKinds.Image => Facts(("to", MarkdownLinks.Goes(node) ?? string.Empty)),
        MarkdownKinds.Break => Facts(("hard", node.Role == MarkdownRoles.Hard)),
        MarkdownKinds.Typographic => Facts(("faces", node.Role)),
        MarkdownKinds.Abbreviation => Facts(("means", Said(node, MarkdownKinds.Abbreviation))),
        MarkdownKinds.Reference => Facts(("to", Said(node, MarkdownKinds.Reference))),
        AbbreviationDefinition => Facts(("means", Said(node, MarkdownKinds.Abbreviation))),
        Fence => Facts(("language", node is BlockNode block ? block.Language : string.Empty)),
        _ => string.Empty,
    };

    /// <summary>What a stage or the reader hung on a piece as <paramref name="kind"/>, where anything was.</summary>
    private static object? Said(ContentNode node, string kind)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsDerived || child.Kind != kind) continue;
            if (child.Held is { } held) return held;

            foreach (var inner in child.Children) return inner.Held ?? inner.Text;
        }

        return null;
    }

    private static string Facts(params (string Name, object? Value)[] facts)
    {
        var said = new StringBuilder();

        foreach (var (name, value) in facts)
        {
            if (value is null) continue;
            if (said.Length > 0) said.Append(';');

            said.Append(name).Append('=').Append(value is bool yes ? (yes ? "yes" : "no") : Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        return said.ToString();
    }
}

/// <summary>One thing a reading says is in a document: what it is, where its first and last characters stand, and what it carries.</summary>
internal readonly record struct MarkdownConstruct(string Kind, int Start, int End, string Facts)
{
    private const int Longest = 40;

    /// <summary>
    /// The construct by what it is and the characters it stands for — which, unlike where it stands, survives the document it
    /// is in being written to somewhere else.
    /// </summary>
    public string Named(string source)
    {
        var text = source[this.Start..this.End];
        var shown = (text.Length > Longest ? text[..Longest] + "…" : text).Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");

        return this.Facts.Length > 0 ? $"{this.Kind}[{this.Facts}] \"{shown}\"" : $"{this.Kind} \"{shown}\"";
    }

    /// <summary>Where it is, as lines and columns counted from one.</summary>
    public string Where(string source) => $"{Place(source, this.Start)}-{Place(source, this.End)}";

    private static string Place(string source, int at)
    {
        var line = 1;
        var starts = 0;

        for (var next = source.IndexOf('\n'); next >= 0 && next < at; next = source.IndexOf('\n', next + 1))
        {
            line++;
            starts = next + 1;
        }

        return $"{line}:{at - starts + 1}";
    }
}
