using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Settings;

/// <summary>
/// The characters of a line, for the blocks written as <c>key: value</c> lines with rows beneath them —
/// a word cloud's, a correlation plot's.
///
/// <para>
/// Small enough to be written again each time, and that is the trouble: written again, the two readings
/// of "where does this line divide" drift apart, and a value that reads one way in one block reads
/// another way in the next. One place, so the answer is the same answer.
/// </para>
/// </summary>
public static class SettingLines
{
    /// <summary>
    /// The colon that divides a line, which is the first one outside quotes — so a value may hold as many
    /// more as it likes, and a key written in quotes keeps its own.
    /// </summary>
    /// <returns>Where it is, or -1 where the line has none.</returns>
    public static int Colon(string text)
    {
        var quoted = false;

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] == '"') quoted = !quoted;
            else if (text[at] == ':' && !quoted) return at;
        }

        return -1;
    }

    /// <summary>How much space starts <paramref name="text"/>.</summary>
    public static int Leading(string text)
    {
        var n = 0;
        while (n < text.Length && char.IsWhiteSpace(text[n])) n++;
        return n;
    }

    /// <summary>How much space ends <paramref name="text"/>, never reaching back past <paramref name="floor"/>.</summary>
    public static int Trailing(string text, int floor)
    {
        var n = 0;
        while (text.Length - n > floor && char.IsWhiteSpace(text[text.Length - n - 1])) n++;
        return n;
    }

    /// <summary>Space held where the writer put it, which is what keeps a block printing back as it was written.</summary>
    public static ContentNode Space(string text) => ContentNode.Leaf(Kinds.Space, text, Roles.Trivia);
}
