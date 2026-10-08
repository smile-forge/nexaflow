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
    /// <param name="language">The symbology the block was called by — every 2D code, and a barcode, is read the same way.</param>
    public static ContentNode Parse(string? source, string language)
    {
        source ??= string.Empty;
        var lines = new List<ContentNode>();

        for (var at = 0; at < source.Length;)
        {
            var newline = source.IndexOf('\n', at);
            var stop = newline < 0 ? source.Length : newline + 1;

            var end = newline < 0 ? source.Length : newline;
            if (end > at && source[end - 1] == '\r') end--;

            lines.Add(Line(source[at..end], source[end..stop], at));
            at = stop;
        }

        return new BlockNode(language, lines, MatrixKinds.Block);
    }

    /// <summary>
    /// One line and the characters that ended it. The space either side of what it says is trivia of the line,
    /// so a field is only its key, its colon and its value.
    /// </summary>
    private static ContentNode Line(string body, string terminator, int at)
    {
        var pieces = new List<ContentNode>();

        var lead = Leading(body);
        var trail = Trailing(body, lead);
        var text = body[lead..(body.Length - trail)];

        if (lead > 0) pieces.Add(Space(body[..lead], at));

        if (text.Length == 0) { }
        else if (text[0] == '#') pieces.Add(ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: at + lead));
        else if (text.IndexOf(':') is var colon and > 0) pieces.Add(Field(text, colon, at + lead));
        else pieces.Add(ContentNode.Shown(text, $"'{text}' is not a `key: value` line.", offset: at + lead));

        if (trail > 0) pieces.Add(Space(body[(body.Length - trail)..], at + body.Length - trail));
        if (terminator.Length > 0) pieces.Add(Space(terminator, at + body.Length));

        return ContentNode.Branch(MatrixKinds.Line, pieces, offset: at);
    }

    /// <summary>A key, its colon, and the value after it — absent when nothing follows the colon.</summary>
    private static ContentNode Field(string text, int colon, int at)
    {
        var key = text[..colon];
        var keyEnd = key.Length - Trailing(key, 0);

        var pieces = new List<ContentNode> { ContentNode.Leaf(MatrixKinds.Key, key[..keyEnd], Roles.Name, offset: at) };
        if (keyEnd < key.Length) pieces.Add(Space(key[keyEnd..], at + keyEnd));

        pieces.Add(ContentNode.Leaf(Kinds.Token, ":", Roles.Separator, offset: at + colon));

        var rest = text[(colon + 1)..];
        var gap = Leading(rest);
        if (gap > 0) pieces.Add(Space(rest[..gap], at + colon + 1));
        if (gap < rest.Length)
            pieces.Add(ContentNode.Leaf(MatrixKinds.Value, rest[gap..], MatrixRoles.Value, offset: at + colon + 1 + gap));

        return ContentNode.Branch(MatrixKinds.Field, pieces, offset: at);
    }

    private static ContentNode Space(string text, int at) => ContentNode.Leaf(Kinds.Space, text, Roles.Trivia, offset: at);

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
