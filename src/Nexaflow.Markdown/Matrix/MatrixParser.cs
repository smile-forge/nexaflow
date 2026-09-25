using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Matrix;

/// <summary>
/// Reads the body of a code block — <c>qr</c>, <c>aztec</c>, <c>pdf417</c>, <c>datamatrix</c>, and the fields of a
/// <c>barcode</c> (<see cref="Barcode.BarcodeParser"/>) — into a tree.
///
/// <para>
/// One parser for the four, because the grammar is one: a field per line, the key before the first colon and
/// everything after it the value, which is what lets a URL sit on the right of a <c>url:</c> without quoting.
/// Which keys a symbology takes, and what they come to, is its builder's business; this says only where each
/// key and each value is.
/// </para>
/// <para>
/// A line that is not a field is held as it was written, with the reason, rather than ending the read — so
/// the tree prints back as exactly what was typed however little of it makes sense.
/// </para>
/// </summary>
public static class MatrixParser
{
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;
        var lines = new List<ContentNode>();

        for (var at = 0; at < source.Length;)
        {
            var newline = source.IndexOf('\n', at);
            var stop = newline < 0 ? source.Length : newline + 1;

            var end = newline < 0 ? source.Length : newline;
            if (end > at && source[end - 1] == '\r') end--;

            lines.Add(Line(source[at..end], source[end..stop]));
            at = stop;
        }

        return ContentNode.Branch(MatrixKinds.Block, lines);
    }

    /// <summary>
    /// One line and the characters that ended it. The space either side of what it says is trivia of the line,
    /// so a field is only its key, its colon and its value.
    /// </summary>
    private static ContentNode Line(string body, string terminator)
    {
        var pieces = new List<ContentNode>();

        var lead = Leading(body);
        var trail = Trailing(body, lead);
        var text = body[lead..(body.Length - trail)];

        if (lead > 0) pieces.Add(Space(body[..lead]));

        if (text.Length == 0) { }
        else if (text[0] == '#') pieces.Add(ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia));
        else if (text.IndexOf(':') is var colon and > 0) pieces.Add(Field(text, colon));
        else pieces.Add(ContentNode.Shown(text, $"'{text}' is not a `key: value` line."));

        if (trail > 0) pieces.Add(Space(body[(body.Length - trail)..]));
        if (terminator.Length > 0) pieces.Add(Space(terminator));

        return ContentNode.Branch(MatrixKinds.Line, pieces);
    }

    /// <summary>A key, its colon, and the value after it — absent when nothing follows the colon.</summary>
    private static ContentNode Field(string text, int colon)
    {
        var key = text[..colon];
        var keyEnd = key.Length - Trailing(key, 0);

        var pieces = new List<ContentNode> { ContentNode.Leaf(MatrixKinds.Key, key[..keyEnd], Roles.Name) };
        if (keyEnd < key.Length) pieces.Add(Space(key[keyEnd..]));

        pieces.Add(ContentNode.Leaf(Kinds.Token, ":", Roles.Separator));

        var rest = text[(colon + 1)..];
        var gap = Leading(rest);
        if (gap > 0) pieces.Add(Space(rest[..gap]));
        if (gap < rest.Length) pieces.Add(ContentNode.Leaf(MatrixKinds.Value, rest[gap..], MatrixRoles.Value));

        return ContentNode.Branch(MatrixKinds.Field, pieces);
    }

    private static ContentNode Space(string text) => ContentNode.Leaf(Kinds.Space, text, Roles.Trivia);

    private static int Leading(string text)
    {
        var n = 0;
        while (n < text.Length && char.IsWhiteSpace(text[n])) n++;
        return n;
    }

    /// <summary>How much space ends <paramref name="text"/>, never reaching back past <paramref name="floor"/>.</summary>
    private static int Trailing(string text, int floor)
    {
        var n = 0;
        while (text.Length - n > floor && char.IsWhiteSpace(text[text.Length - n - 1])) n++;
        return n;
    }
}
