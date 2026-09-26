using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Xy;

/// <summary>
/// What an <c>xychart</c> block says beyond the lines every diagram shares: which way it runs after the keyword, a
/// <c>title</c>, its <c>x-axis</c> and <c>y-axis</c>, and its <c>bar</c> and <c>line</c> series.
///
/// <para>
/// The rules are Mermaid's. Text is a word, or words in quotes. An x-axis is its title where it has one, then its categories
/// in brackets — <c>[jan, "feb 2"]</c> — or a range, <c>0 --&gt; 100</c>; a y-axis is its title and a range, either left
/// out for the chart to work out. A series is its name where it has one, then its values in brackets, and a value of a line
/// may carry a label in quotes after it: <c>line [540 "PaLM", 65]</c>. A number may be signed, and start at its point.
/// </para>
/// <para>
/// Nothing a line says depends on another, so there are no stages: which category a value stands over is its place in the
/// order, which the model reads. What is written half way — a list not yet closed, a value still to come — is read as far as
/// it goes.
/// </para>
/// </summary>
public sealed class XyGrammar : IMermaidGrammar
{
    public const string XAxis = "x-axis";
    public const string YAxis = "y-axis";
    public const string Bar = "bar";
    public const string Line = "line";
    public const string Horizontal = "horizontal";
    public const string Vertical = "vertical";

    /// <summary>The arrow between a range's ends.</summary>
    public const string Arrow = "-->";

    /// <inheritdoc/>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments, comments: false);

        return (line.Word(Horizontal, XyKinds.Orientation) || line.Word(Vertical, XyKinds.Orientation)) && line.Done
            ? line.Read(XyKinds.Options, MermaidRoles.Arguments)
            : ContentNode.Shown(arguments, "An xychart runs horizontal or vertical, and nothing else follows it: xychart horizontal.", MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord, XAxis, YAxis, Bar, Line) switch
        {
            MermaidLine.TitleWord => line.Title(quotes: true),
            XAxis => Axis(line, XAxis),
            YAxis => Axis(line, YAxis),
            Bar => Series(line, Bar),
            Line => Series(line, Line),
            _ => line.Shown("An xychart line is a title, an axis or a series: x-axis [jan, feb], y-axis \"Revenue\" 0 --> 100, bar [20, 35]."),
        };
    }

    /// <inheritdoc/>
    /// <remarks>Only what the front matter asks for: the axes and series are the lines in the order they are written.</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new WithConfig<XyConfig>(XyConfig.Read(block.Config))];

    /// <inheritdoc/>
    /// <remarks>Under a series, another of its kind; anywhere else, a bar — each with its values still to write, the caret in its brackets.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) =>
        above?.Kind == XyKinds.Series && above.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key) is { } word
            ? ($"{word.Text} []", word.Text.Length + 2)
            : ($"{Bar} []", Bar.Length + 2);

    /// <inheritdoc/>
    /// <remarks>A bare word holds no space, comma, bracket, quote or arrow, and is put in quotes to hold one; see <see cref="MermaidWriting.Escape"/>.</remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        MermaidWriting.Escape(part, caret, text, (_, said) => said.Length > 0 && said.All(Letter));

    /// <inheritdoc/>
    /// <remarks>Where a category, a title or a name is still to write, and between the quotes of a title, a name or a label.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind is MermaidKinds.Name or MermaidKinds.Quoted;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>
    /// An axis: <c>x-axis "Month" [jan, feb]</c>, <c>x-axis 0 --&gt; 10</c>, <c>y-axis "Revenue" 0 --&gt; 100</c> — its title and
    /// its range each left out where the chart is to work them out.
    /// </summary>
    private static ContentNode Axis(MermaidLine line, string word)
    {
        var shape = word == XAxis
            ? "An x-axis is its title where it has one, then its categories in brackets or a range: x-axis \"Month\" [jan, feb], or x-axis 0 --> 10."
            : "A y-axis is its title where it has one, then its range where it has one: y-axis \"Revenue\" 0 --> 100.";

        line.Word(word);
        if (!Spaced(line)) return line.Done ? line.Read(XyKinds.Axis) : line.Shown(shape);
        if (line.Done) return line.Read(XyKinds.Axis);

        // A title comes first — unless what is written first is where a range starts.
        if (line.Next != '[' && !Ranged(line.Rest))
        {
            if (!Titled(line)) return line.Shown(shape);
            line.Space();
        }

        if (line.Done) return line.Read(XyKinds.Axis);

        if (line.Next == '[')
        {
            if (word == YAxis) return line.Shown("A y-axis is a range of numbers, not categories: y-axis 0 --> 100.");
            if (!Categories(line)) return line.Shown(shape);
        }
        else if (!Range(line))
        {
            return line.Shown(shape);
        }

        return line.Done ? line.Read(XyKinds.Axis) : line.Shown(shape);
    }

    /// <summary>A series: <c>bar "Revenue" [20, 35]</c>, or <c>line [540 "PaLM", 65]</c>.</summary>
    private static ContentNode Series(MermaidLine line, string word)
    {
        const string Shape = "A series is its name where it has one, then its values in brackets: bar \"Revenue\" [20, 35, 30].";

        line.Word(word);
        if (!Spaced(line) || line.Done) return line.Shown(Shape);

        if (line.Next != '[')
        {
            if (!Titled(line)) return line.Shown(Shape);
            line.Space();
        }

        if (line.Next != '[') return line.Shown(Shape);

        Values(line);
        return line.Done ? line.Read(XyKinds.Series) : line.Shown(Shape);
    }

    /// <summary>An axis's categories in their brackets. A comma with nothing after it yet is followed by a category still to write.</summary>
    private static bool Categories(MermaidLine line)
    {
        line.Open();
        line.Token("[", Roles.Open);
        line.Space();

        if (line.Next != ']' && !line.Names(Category, Roles.Element, XyRoles.Category, ends: ']')) return false;

        line.Space();
        var closed = line.Token("]", Roles.Close);
        line.Close(XyKinds.Categories, trouble: closed ? null : "These categories are never closed with ].");
        return true;
    }

    /// <summary>A range: where it starts, the arrow, and where it ends — the end still to come where nothing is written after the arrow.</summary>
    private static bool Range(MermaidLine line)
    {
        line.Open();
        line.Amount(XyRoles.Min, Number, stop: Arrow);
        line.Space();
        if (!line.Token(Arrow)) return false;

        line.Room();
        line.Amount(XyRoles.Max, Number);
        line.Close(XyKinds.Range);
        return true;
    }

    /// <summary>A series' values in their brackets. Brackets never closed are still read, with the reason on them.</summary>
    private static void Values(MermaidLine line)
    {
        line.Open();
        line.Token("[", Roles.Open);
        line.Space();

        if (!line.Done && line.Next != ']')
        {
            while (true)
            {
                Point(line);
                line.Space();
                if (!line.Token(",")) break;
                line.Room();
            }
        }

        var closed = line.Token("]", Roles.Close);
        line.Close(XyKinds.Values, trouble: closed ? null : "These values are never closed with ].");
    }

    /// <summary>One value, and the label in quotes after it where one is written.</summary>
    private static void Point(MermaidLine line)
    {
        line.Open();
        line.Amount(XyRoles.Value, Number, until: ",]\"");

        if (line.Past == '"')
        {
            line.Space();
            line.Quoted(XyRoles.Label, what: "label");
        }

        line.Close(XyKinds.Point);
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>A title or a name: in quotes, or a word.</summary>
    private static bool Titled(MermaidLine line) =>
        line.Next == '"' ? line.Quoted(XyRoles.Title, what: "title") : line.Name(XyRoles.Title, Letter);

    private static bool Category(MermaidLine line) => line.Name(XyRoles.Category, Letter);

    /// <summary>What a bare word is made of: anything but space, a comma, a bracket, a quote or the arrow's head.</summary>
    private static bool Letter(char character) => character > ' ' && character is not (',' or '[' or ']' or '"' or '>');

    /// <summary>Whether what is written starts with where a range starts: a single word, then the arrow.</summary>
    private static bool Ranged(string rest)
    {
        var arrow = rest.IndexOf(Arrow, StringComparison.Ordinal);
        return arrow >= 0 && rest[0] != '"' && !rest[..arrow].TrimEnd().Contains(' ');
    }

    /// <summary>Any number: Mermaid's may be signed, and start at its point.</summary>
    private static readonly Func<string, string?> Number = MermaidNumber.Where(_ => true, string.Empty);

    /// <summary>Takes the space after a line's word — and, where nothing more is written, the space left for what follows — or says there is none.</summary>
    private static bool Spaced(MermaidLine line)
    {
        var at = line.At;
        line.Room();
        return line.At > at;
    }
}
