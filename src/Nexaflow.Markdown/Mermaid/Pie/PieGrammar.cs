using System.Globalization;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>
/// What a <c>pie</c> block says beyond the lines every diagram shares: <c>showData</c> and a title after the keyword,
/// a <c>title</c> of its own, and a slice per line — a label in quotes, a colon, and a number.
///
/// <para>
/// The rules are Mermaid's: a value is a number greater than nought, and slices are drawn clockwise in the order they
/// are written. A value that is neither is still read as a slice, with the reason on the number — the label is what
/// the reader is looking at, and hiding the whole line because its value is wrong would leave nothing to fix. A value
/// not yet written is no complaint at all: it is a slice still being written.
/// </para>
/// </summary>
public sealed class PieGrammar : IMermaidGrammar
{
    /// <summary>The word after <c>pie</c> that puts each slice's value in the legend.</summary>
    public const string ShowData = "showData";

    /// <summary>The word that gives the chart its title, after the keyword or on a line of its own.</summary>
    public const string Title = "title";

    /// <inheritdoc/>
    public ContentNode? Header(string arguments)
    {
        var pieces = new List<ContentNode>();
        var at = 0;

        if (Word(arguments, at, ShowData) is var shown and > 0)
        {
            pieces.Add(ContentNode.Leaf(PieKinds.ShowData, arguments[at..shown], Roles.Name));
            at = Space(arguments, shown, pieces);
        }

        if (at < arguments.Length)
            pieces.Add(Titled(arguments[at..])
                       ?? ContentNode.Shown(arguments[at..],
                                            "A pie takes showData and a title after it: pie showData title Key elements."));

        return pieces.Count == 0 ? null : ContentNode.Branch(PieKinds.Options, pieces, MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text) =>
        Word(text, 0, Title) > 0 ? Titled(text.TrimEnd()) : Slice(text);

    /// <inheritdoc/>
    /// <remarks>A slice with neither its label nor its value written, the caret between its quotes — whatever it follows.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => ("\"\" : ", 1);

    /// <inheritdoc/>
    /// <remarks>A label holds anything but a quote, which is written <c>#quot;</c>.</remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        part.Parent?.Kind == PieKinds.Label ? MermaidWriting.InQuotes(caret, text) : null;

    /// <summary>A <c>title …</c>, wherever it is written. Null where the word is there with nothing after it.</summary>
    private static ContentNode? Titled(string text)
    {
        var end = Word(text, 0, Title);
        if (end <= 0) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Key, text[..end], Roles.Name) };
        var at = Space(text, end, pieces);

        if (at < text.Length) pieces.Add(ContentNode.Leaf(PieKinds.Name, text[at..], PieRoles.Label));

        return ContentNode.Branch(PieKinds.Title, pieces);
    }

    /// <summary>
    /// One slice: <c>"Calcium" : 42.96</c> — or <c>"Calcium" : </c>, still to be given its value.
    ///
    /// <para>
    /// The line comes with the space after it, because past the colon that space is where the value goes: one not yet
    /// written is read as empty and standing after it, which is where typing it will put it. Anywhere else the space is the
    /// line's, and is left to it.
    /// </para>
    /// </summary>
    private static ContentNode Slice(string text)
    {
        var written = text.TrimEnd();

        if (written[0] != '"')
            return ContentNode.Shown(written, "A pie slice is a label in quotes, a colon and a number: \"Calcium\" : 42.96.");

        var close = written.IndexOf('"', 1);
        if (close < 0) return ContentNode.Shown(written, "This slice's label is never closed with a quote.");

        var pieces = new List<ContentNode> { Label(written[..(close + 1)]) };
        var at = Space(written, close + 1, pieces);

        if (at >= written.Length || written[at] != ':')
            return ContentNode.Shown(written, "A pie slice needs a colon between its label and its value.");

        pieces.Add(ContentNode.Leaf(Kinds.Token, ":", Roles.Separator));

        // Nothing after the colon: the value is still to come, and goes after whatever space was left for it.
        at = at + 1 < written.Length ? Space(written, at + 1, pieces) : Space(text, at + 1, pieces);
        pieces.Add(Worth(written.Length > at ? written[at..] : string.Empty));

        return ContentNode.Branch(PieKinds.Slice, pieces);
    }

    /// <summary>A label with its quotes — machinery either side of what it says.</summary>
    private static ContentNode Label(string quoted) =>
        ContentNode.Branch(PieKinds.Label,
        [
            ContentNode.Leaf(Kinds.Token, "\"", Roles.Open),
            ContentNode.Leaf(PieKinds.Name, quoted[1..^1], PieRoles.Label),
            ContentNode.Leaf(Kinds.Token, "\"", Roles.Close),
        ]);

    /// <summary>A slice's value, in the place it is written — an empty one included, so there is somewhere to write it.</summary>
    private static ContentNode Worth(string number) =>
        ContentNode.Branch(PieKinds.Worth,
                           [ContentNode.Leaf(PieKinds.Value, number, PieRoles.Value, number.Length == 0 ? null : Trouble(number))]);

    /// <summary>What is wrong with a slice's value, where anything is.</summary>
    private static string? Trouble(string text) =>
        !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? $"'{text}' is not a number."
            : value > 0 ? null : "A pie slice is worth more than nought, or it is not a slice.";

    /// <summary>
    /// How far <paramref name="word"/> reaches from <paramref name="at"/>, or nought where the text does not say it.
    /// A word ends where the next one cannot have started, so <c>titled</c> is not <c>title</c>.
    /// </summary>
    private static int Word(string text, int at, string word)
    {
        if (at + word.Length > text.Length) return 0;
        if (string.Compare(text, at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return 0;

        var end = at + word.Length;
        return end == text.Length || char.IsWhiteSpace(text[end]) ? end : 0;
    }

    /// <summary>Takes the space at <paramref name="at"/> as trivia, and says where what follows it starts.</summary>
    private static int Space(string text, int at, List<ContentNode> pieces)
    {
        var past = at;
        while (past < text.Length && char.IsWhiteSpace(text[past])) past++;

        if (past > at) pieces.Add(ContentNode.Leaf(Kinds.Space, text[at..past], Roles.Trivia));
        return past;
    }
}
