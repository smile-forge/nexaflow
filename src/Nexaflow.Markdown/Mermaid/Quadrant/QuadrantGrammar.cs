using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Quadrant.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Quadrant;

/// <summary>
/// What a <c>quadrantChart</c> block says beyond the lines every diagram shares: a <c>title</c>, the <c>x-axis</c> and
/// <c>y-axis</c> ends, the <c>quadrant-1</c>…<c>quadrant-4</c> captions, the points, and the <c>classDef</c>s that style them.
///
/// <para>
/// The rules are Mermaid's. An axis is what its low end says, then <c>--&gt;</c> and what its high end says: <c>x-axis Low
/// Reach --&gt; High Reach</c>. A point is its name, a colon, and where it stands from 0 to 1 across and up —
/// <c>Campaign A: [0.3, 0.6]</c> — its class after <c>:::</c> before the colon, and its style after its position:
/// <c>radius</c>, <c>color</c>, <c>stroke-color</c> and <c>stroke-width</c>. Text runs bare to where it ends, or is written
/// in quotes.
/// </para>
/// <para>
/// Whether a point's class is written anywhere is a fact about the block, not the point's line, so it is the stage's
/// (<see cref="ResolveClasses"/>).
/// </para>
/// </summary>
public sealed class QuadrantGrammar : IMermaidGrammar
{
    public const string XAxis = "x-axis";
    public const string YAxis = "y-axis";
    public const string ClassDef = "classDef";
    public const string Arrow = "-->";
    public const string ClassMark = ":::";

    /// <summary>The words starting a quadrant's line, the first quadrant's first.</summary>
    public static readonly IReadOnlyList<string> Regions = ["quadrant-1", "quadrant-2", "quadrant-3", "quadrant-4"];

    /// <summary>What a point's or a class's style can set.</summary>
    public static readonly IReadOnlyList<string> Styles = ["radius", "color", "stroke-color", "stroke-width"];

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, [MermaidLine.TitleWord, XAxis, YAxis, ClassDef, .. Regions]) switch
        {
            MermaidLine.TitleWord => line.Title(),
            XAxis => Axis(line, XAxis),
            YAxis => Axis(line, YAxis),
            ClassDef => Class(line),
            { } region => Region(line, region),
            null => Point(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>Under a quadrant's caption, the next quadrant's; anywhere else, a point with its name still to write, in the middle.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above)
    {
        var word = above?.Kind == QuadrantKinds.Region ? above.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key)?.Text : null;
        var next = word is null ? -1 : Regions.ToList().FindIndex(region => region.Equals(word, StringComparison.OrdinalIgnoreCase)) + 1;

        return next is > 0 and < 4 ? ($"{Regions[next]} ", Regions[next].Length + 1) : (": [0.5, 0.5]", 0);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// In quotes, a quote is written as its entity code; bare text is put in quotes to hold a colon, a quote or a bracket, which
    /// would end it; a bare class name is put in quotes to hold anything but a word.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text, (_, said) => Bare(said)) is { } escaped) return escaped;
        if (part.Parent is not { Kind: QuadrantKinds.Text } holder || holder.Children.Any(child => child.Role == Roles.Open)) return null;

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);

        return (before + after).Any(character => character is ':' or '"' or '[' or ']')
            ? MermaidWriting.Quoting(part.Start, part.Start + said.Length, before, after)
            : null;
    }

    /// <inheritdoc/>
    /// <remarks>A class is declared where a <c>classDef</c> names it, and used by every point that takes it after <c>:::</c>.</remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var uses = block.SelfAndDescendants()
            .Where(part => part.Kind == QuadrantKinds.Point)
            .Select(point => point.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name))
            .OfType<ContentPart>()
            .ToList();

        return
        [
            .. block.SelfAndDescendants()
                .Where(part => part.Kind == QuadrantKinds.Class)
                .Select(line => line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name))
                .OfType<ContentPart>()
                .Select(name => (Name: name, Said: Said(name)))
                .Where(declared => declared.Said.Length > 0)
                .Select(declared => new MermaidName(declared.Said, declared.Name, [.. uses.Where(use => Said(use) == declared.Said)])),
        ];

        static string Said(ContentPart name) => name.Words()?.Text ?? string.Empty;
    }

    /// <inheritdoc/>
    public string Naming(string name) => Bare(name) ? name : "\"" + name + "\"";

    /// <inheritdoc/>
    /// <remarks>Whether each point's class is written is worked out over the whole block (<see cref="ResolveClasses"/>).</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new ResolveClasses()];

    /// <inheritdoc/>
    /// <remarks>Where text or a class's name is still to write — an axis end, a caption, a point's name, between quotes.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind is QuadrantKinds.Text or MermaidKinds.Name;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>An axis: <c>x-axis Low Reach --&gt; High Reach</c> — its high end left out where only its low end is named.</summary>
    private static ContentNode Axis(MermaidLine line, string word)
    {
        line.Word(word);
        if (!Spaced(line)) return line.Done ? line.Read(QuadrantKinds.Axis) : line.Shown("An axis names its low end, then --> and its high end: x-axis Low Reach --> High Reach.");

        if (!Text(line, QuadrantRoles.Low, stop: Arrow)) return line.Shown("This text is never closed with a quote.");

        line.Space();
        if (line.Token(Arrow))
        {
            line.Room();
            if (!Text(line, QuadrantRoles.High)) return line.Shown("This text is never closed with a quote.");
        }

        return line.Done ? line.Read(QuadrantKinds.Axis) : line.Shown("An axis names its low end, then --> and its high end: x-axis Low Reach --> High Reach.");
    }

    /// <summary>A quadrant's caption: <c>quadrant-1 We should expand</c>.</summary>
    private static ContentNode Region(MermaidLine line, string word)
    {
        line.Word(word);
        if (!Spaced(line)) return line.Shown($"A quadrant's line is its word and what it says: {word} We should expand.");

        return Text(line, QuadrantRoles.Caption) && line.Done
            ? line.Read(QuadrantKinds.Region)
            : line.Shown($"A quadrant's line is its word and what it says: {word} We should expand.");
    }

    /// <summary>A class: <c>classDef important color: #ff3300, radius: 10</c> — its style still to write where none is yet.</summary>
    private static ContentNode Class(MermaidLine line)
    {
        const string Shape = "A classDef names its class, then its style: classDef important color: #ff3300, radius: 10.";

        line.Word(ClassDef);
        if (!Spaced(line) || line.Done || !Id(line, QuadrantRoles.Class)) return line.Shown(Shape);

        if (!line.Done)
        {
            var at = line.At;
            line.Space();
            if (line.At == at || !line.Properties(Styles)) return line.Shown(Shape);
        }

        return line.Read(QuadrantKinds.Class);
    }

    /// <summary>
    /// A point: <c>Campaign A: [0.3, 0.6]</c>, <c>Point A:::important: [0.9, 0.1] radius: 12</c>. Its position still to come
    /// stands after the space left for it.
    /// </summary>
    private static ContentNode Point(MermaidLine line)
    {
        const string Shape = "A point is its name, a colon and where it stands, from 0 to 1 across and up: Campaign A: [0.3, 0.6].";

        if (!Text(line, QuadrantRoles.Name, until: ":")) return line.Shown(Shape);

        line.Space();
        if (line.Token(ClassMark) && !Id(line, QuadrantRoles.Class)) return line.Shown(Shape);

        line.Space();
        if (!line.Token(":")) return line.Shown(Shape);

        line.Room();
        if (line.Done) return line.Read(QuadrantKinds.Point);
        if (line.Next != '[') return line.Shown(Shape);

        Position(line);

        if (!line.Done)
        {
            var at = line.At;
            line.Space();
            if (line.At == at || !line.Properties(Styles)) return line.Shown(Shape);
        }

        return line.Read(QuadrantKinds.Point);
    }

    /// <summary>Where a point stands: <c>[0.3, 0.6]</c> — read as far as it goes, with the reason where it is not two numbers, or never closed.</summary>
    private static void Position(MermaidLine line)
    {
        line.Open();
        line.Token("[", Roles.Open);
        line.Space();
        line.Amount(QuadrantRoles.X, Coordinate, until: ",]");
        line.Space();

        var pair = line.Token(",");
        if (pair)
        {
            line.Room();
            line.Amount(QuadrantRoles.Y, Coordinate, until: "]");
            line.Space();
        }

        var closed = line.Token("]", Roles.Close);
        line.Close(QuadrantKinds.Position, trouble: !closed ? "This position is never closed with ]."
                                                   : !pair ? "A point stands at two numbers, across and up: [0.3, 0.6]."
                                                   : null);
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>Text as a piece of its own: in quotes, or bare up to <paramref name="until"/> or <paramref name="stop"/> — false where its quote is never closed.</summary>
    private static bool Text(MermaidLine line, string role, string? until = null, string? stop = null)
    {
        line.Open();

        if (line.Next == '"')
        {
            if (!line.Quoted(role, kind: null)) return false;
        }
        else
        {
            line.Words(role, until: until, stop: stop);
        }

        line.Close(QuadrantKinds.Text, role);
        return true;
    }

    /// <summary>A class's name, bare or in quotes.</summary>
    private static bool Id(MermaidLine line, string role) => line.Name(role, Letter);

    private static bool Letter(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '-';

    private static bool Bare(string name) => name.Length > 0 && name.All(Letter);

    /// <summary>A position: a number from 0 to 1.</summary>
    private static readonly Func<string, string?> Coordinate = MermaidNumber.Where(number => number is >= 0 and <= 1, "A point stands from 0 to 1 across and up.");

    /// <summary>Takes the space after a line's word — and, where nothing more is written, the space left for what follows — or says there is none.</summary>
    private static bool Spaced(MermaidLine line)
    {
        var at = line.At;
        line.Room();
        return line.At > at;
    }
}
