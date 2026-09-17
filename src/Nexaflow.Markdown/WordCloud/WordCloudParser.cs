using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.WordCloud;

/// <summary>
/// Reads the body of a <c>wordcloud</c> fence into a tree: a <c>key: value</c> line at a time, the key before
/// the first colon and everything after it the value.
///
/// <para>
/// The same grammar as a 2D code's block, doing one more job. Most of a cloud's lines are its words — the key
/// is the word and the value is what it counts for — and a few of them are settings. Which a line is, is
/// decided here rather than downstream, because it is decided by the key alone: a key
/// <see cref="WordCloudSetting.Is">named as a setting</see> is one, and anything else is a word. A word that
/// happens to be a setting's name is written in quotes, which is the one thing this grammar has that a 2D
/// code's has not.
/// </para>
/// <para>
/// A line that is neither is held as it was written, with the reason, rather than ending the read — so the
/// tree prints back as exactly what was typed however little of it makes sense, and a cloud keeps drawing
/// while somebody types into it.
/// </para>
/// </summary>
public static class WordCloudParser
{
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;
        var lines = new List<ContentNode>();

        // Whether the settings are still being read. They are the lines above the words, and the first word
        // closes them — see Pair.
        var settings = true;

        for (var at = 0; at < source.Length;)
        {
            var newline = source.IndexOf('\n', at);
            var stop = newline < 0 ? source.Length : newline + 1;

            var end = newline < 0 ? source.Length : newline;
            if (end > at && source[end - 1] == '\r') end--;

            lines.Add(Line(source[at..end], source[end..stop], ref settings));
            at = stop;
        }

        return ContentNode.Branch(WordCloudKinds.Block, lines);
    }

    /// <summary>
    /// One line and the characters that ended it. The space either side of what it says is trivia of the
    /// line, so a word or a setting is only its key, its colon and its value.
    /// </summary>
    private static ContentNode Line(string body, string terminator, ref bool settings)
    {
        var pieces = new List<ContentNode>();

        var lead = Leading(body);
        var trail = Trailing(body, lead);
        var text = body[lead..(body.Length - trail)];

        if (lead > 0) pieces.Add(Space(body[..lead]));

        if (text.Length == 0) { }
        else if (text[0] == '#') pieces.Add(ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia));
        else if (Colon(text) is var colon and > 0) pieces.Add(Pair(text, colon, ref settings));
        else
            pieces.Add(ContentNode.Shown(
                text, $"'{text}' is not a `word: weight` line."));

        if (trail > 0) pieces.Add(Space(body[(body.Length - trail)..]));
        if (terminator.Length > 0) pieces.Add(Space(terminator));

        return ContentNode.Branch(WordCloudKinds.Line, pieces);
    }

    /// <summary>
    /// The colon that divides the line, which is the first one outside quotes: a word written
    /// <c>"9:15": 4</c> is the time, not a word called <c>"9</c>.
    /// </summary>
    private static int Colon(string text)
    {
        var quoted = false;

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] == '"') quoted = !quoted;
            else if (text[at] == ':' && !quoted) return at;
        }

        return -1;
    }

    /// <summary>
    /// A word and its weight, or a setting and its value.
    ///
    /// <para>
    /// <strong>The settings are the lines above the words</strong>, and the first word closes them. A key that
    /// names a setting is one only while none has been written yet; after that it is a word, because a list of
    /// words is full of ordinary English and several of the settings are ordinary English too — <c>shape</c>,
    /// <c>scale</c>, <c>colour</c>, <c>gap</c>. Without the rule a cloud of design terms stopped being a cloud
    /// at all, which is a poor answer to a block that says nothing wrong.
    /// </para>
    /// <para>
    /// A word written in quotes is a word whatever it is called and wherever it stands, which is the escape
    /// hatch for the one case the rule cannot reach: a cloud whose <em>heaviest</em> word is named like a
    /// setting.
    /// </para>
    /// </summary>
    private static ContentNode Pair(string text, int colon, ref bool settings)
    {
        var key = text[..colon];
        var keyEnd = key.Length - Trailing(key, 0);
        var written = key[..keyEnd];

        var setting = settings && written.Length > 0 && written[0] != '"' && WordCloudSetting.Is(written);
        if (!setting) settings = false;

        var pieces = new List<ContentNode>
        {
            setting
                ? ContentNode.Leaf(WordCloudKinds.Key, written, Roles.Name)
                : Word(written),
        };

        if (keyEnd < key.Length) pieces.Add(Space(key[keyEnd..]));

        pieces.Add(ContentNode.Leaf(Kinds.Token, ":", Roles.Separator));

        var rest = text[(colon + 1)..];
        var gap = Leading(rest);
        if (gap > 0) pieces.Add(Space(rest[..gap]));

        if (gap < rest.Length)
            pieces.Add(setting
                ? ContentNode.Leaf(WordCloudKinds.Value, rest[gap..], WordCloudRoles.Value)
                : ContentNode.Leaf(WordCloudKinds.Weight, rest[gap..], WordCloudRoles.Weight));

        return ContentNode.Branch(setting ? WordCloudKinds.Setting : WordCloudKinds.Entry, pieces);
    }

    /// <summary>
    /// The word an entry is for: its characters, and the quotes around them where it was written in any.
    ///
    /// <para>
    /// The quotes are the writer's way of saying "this is a word, whatever it is called", so they are held
    /// beside it rather than in it — which is what lets the word be drawn, pressed and typed into without
    /// them, while the line still prints back as it was written.
    /// </para>
    /// </summary>
    private static ContentNode Word(string written)
    {
        if (written.Length < 2 || written[0] != '"' || written[^1] != '"')
            return ContentNode.Leaf(WordCloudKinds.Word, written, WordCloudRoles.Word);

        return ContentNode.Branch(WordCloudKinds.Word,
        [
            ContentNode.Leaf(Kinds.Token, "\"", Roles.Open),
            ContentNode.Leaf(WordCloudKinds.Word, written[1..^1], WordCloudRoles.Word),
            ContentNode.Leaf(Kinds.Token, "\"", Roles.Close),
        ], WordCloudRoles.Word);
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
