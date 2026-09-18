using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// Reads the body of a <c>scatter</c>, <c>bubble</c>, <c>heatmap</c> or <c>density2d</c> fence into a
/// tree: <strong>the settings, and then the table</strong>.
///
/// <para>
/// One grammar for the four fences, because they differ in what is drawn rather than in what is
/// written — which is the decomposition ggplot2 makes, and the reason a bubble plot is a scatter plot
/// with one more column rather than a language of its own.
/// </para>
/// <para>
/// <strong>Only the shapes of a line are decided here.</strong> Whether the first row names the columns,
/// whether the table is a long list of points or a matrix, what a cell reads as and which channel it
/// feeds are all facts about the block as a whole, and the pipeline works them out from everything that
/// was written. A parser that guessed at them would have to guess again at every keystroke.
/// </para>
/// <para>
/// A line is a setting when its key names one <em>and</em> the settings are still open: they are the
/// lines above the table and the first row closes them, so a column headed <c>size</c> is a column.
/// Blank lines and comments leave them open, and a bare <c>data</c> line closes them on purpose, which
/// is the escape hatch for a table whose first row would otherwise read as a setting.
/// </para>
/// <para>
/// A line that says nothing this can read is held as it was written rather than ending the read, so the
/// tree prints back as exactly what was typed however little of it makes sense, and a plot keeps
/// drawing while somebody types into it.
/// </para>
/// </summary>
public static class PlotParser
{
    /// <summary>The word that opens the table where a reader wants to say so rather than let the first row do it.</summary>
    public const string DataWord = "data";

    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;
        var lines = new List<ContentNode>();

        // Whether the settings are still being read. They are the lines above the table, and the first
        // row closes them — see Line.
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

        return ContentNode.Branch(PlotKinds.Block, lines);
    }

    /// <summary>
    /// One line and the characters that ended it. The space either side of what it says is trivia of the
    /// line, so a setting is only its key, its colon and its value, and a row only its cells and what
    /// stands between them.
    /// </summary>
    private static ContentNode Line(string body, string terminator, ref bool settings)
    {
        var pieces = new List<ContentNode>();

        var lead = SettingLines.Leading(body);
        var trail = SettingLines.Trailing(body, lead);
        var text = body[lead..(body.Length - trail)];

        if (lead > 0) pieces.Add(SettingLines.Space(body[..lead]));

        if (text.Length == 0)
        {
            // A blank line leaves the settings open: it is how a reader separates them from the table.
        }
        else if (text[0] == '#')
        {
            pieces.Add(ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia));
        }
        else if (settings && text.Equals(DataWord, StringComparison.OrdinalIgnoreCase))
        {
            settings = false;
            pieces.Add(ContentNode.Leaf(PlotKinds.Data, text, Roles.Name));
        }
        else if (settings && SettingLines.Colon(text) is var colon and > 0 && PlotSetting.Is(Named(text, colon)))
        {
            pieces.Add(Setting(text, colon));
        }
        else
        {
            settings = false;
            pieces.Add(Row(text));
        }

        if (trail > 0) pieces.Add(SettingLines.Space(body[(body.Length - trail)..]));
        if (terminator.Length > 0) pieces.Add(SettingLines.Space(terminator));

        return ContentNode.Branch(PlotKinds.Line, pieces);
    }

    /// <summary>What a line names, without the space between it and its colon.</summary>
    private static string Named(string text, int colon)
    {
        var key = text[..colon];
        return key[..(key.Length - SettingLines.Trailing(key, 0))];
    }

    /// <summary>
    /// A setting and what it is set to: the key, the colon, and every character after it held whole.
    /// What those characters amount to depends on the key, so it belongs to the reader rather than here.
    /// </summary>
    private static ContentNode Setting(string text, int colon)
    {
        var key = text[..colon];
        var keyEnd = key.Length - SettingLines.Trailing(key, 0);

        var pieces = new List<ContentNode>
        {
            ContentNode.Leaf(PlotKinds.Key, key[..keyEnd], Roles.Name),
        };

        if (keyEnd < key.Length) pieces.Add(SettingLines.Space(key[keyEnd..]));

        pieces.Add(ContentNode.Leaf(Kinds.Token, text[colon..(colon + 1)], Roles.Separator));

        var rest = text[(colon + 1)..];
        var gap = SettingLines.Leading(rest);
        if (gap > 0) pieces.Add(SettingLines.Space(rest[..gap]));

        if (gap < rest.Length)
            pieces.Add(ContentNode.Leaf(PlotKinds.Value, rest[gap..], PlotRoles.Value));

        return ContentNode.Branch(PlotKinds.Setting, pieces);
    }

    /// <summary>
    /// One line of the table: its cells, and the space and commas between them.
    ///
    /// <para>
    /// Space and commas separate alike, so a table lined up in columns and a table written with commas
    /// are the same table. Which of them it is stays as written — the space a reader lined their numbers
    /// up with is theirs, and this is where it lives.
    /// </para>
    /// </summary>
    private static ContentNode Row(string text)
    {
        var pieces = new List<ContentNode>();

        for (var at = 0; at < text.Length;)
        {
            if (char.IsWhiteSpace(text[at]))
            {
                var space = at;
                while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
                pieces.Add(SettingLines.Space(text[space..at]));
            }
            else if (text[at] == ',')
            {
                pieces.Add(ContentNode.Leaf(Kinds.Token, text[at..(at + 1)], Roles.Separator));
                at++;
            }
            else
            {
                var start = at;
                var quoted = false;

                while (at < text.Length)
                {
                    if (text[at] == '\"') quoted = !quoted;
                    else if (!quoted && (text[at] == ',' || char.IsWhiteSpace(text[at]))) break;
                    at++;
                }

                pieces.Add(Cell(text[start..at]));
            }
        }

        return ContentNode.Branch(PlotKinds.Row, pieces, Roles.Row);
    }

    /// <summary>
    /// One value of a row: its characters, and the quotes around them where it was written in any.
    ///
    /// <para>
    /// The quotes say "this is one value, spaces and commas and all", so they are held beside it rather
    /// than in it — which is what lets the value be drawn, pressed and typed into without them, while
    /// the line still prints back as it was written.
    /// </para>
    /// </summary>
    private static ContentNode Cell(string written)
    {
        if (written.Length < 2 || written[0] != '\"' || written[^1] != '\"')
            return ContentNode.Leaf(PlotKinds.Cell, written, Roles.Cell);

        return ContentNode.Branch(PlotKinds.Cell,
        [
            ContentNode.Leaf(Kinds.Token, written[..1], Roles.Open),
            ContentNode.Leaf(PlotKinds.Cell, written[1..^1], Roles.Cell),
            ContentNode.Leaf(Kinds.Token, written[^1..], Roles.Close),
        ], Roles.Cell);
    }
}
