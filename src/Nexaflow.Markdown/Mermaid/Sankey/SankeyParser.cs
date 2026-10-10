using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Sankey;

/// <summary>
/// What a sankey diagram lets be written into it.
///
/// <para>
/// The block itself is read by the shared <see cref="MermaidParser"/>: a flow is one row, and a row of fields with
/// commas between them is a shape that reader already reads. What belongs to this diagram alone is how a name is
/// spelled, which is why it owns a parser at all.
/// </para>
/// <para>
/// A name is how every other row says which node it means, and a sankey writes a quote in one by writing it twice,
/// rather than as the entity code the rest of Mermaid uses. A name written bare holds neither a comma nor a quote:
/// being given either means the whole field has to be written again in quotes, which is more than the run of words
/// this is asked about, so it is refused — a keystroke that needs it is caught before this, where the field can be
/// written again. Everything else a sankey writes, Mermaid spells.
/// </para>
/// </summary>
public sealed class SankeyParser : ITranspile
{
    private SankeyParser()
    {
    }

    /// <inheritdoc/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>
    /// What a name says: what is between its quotes where it has them, with a quote written twice standing for one, and
    /// without the space either side of it, as Mermaid reads a field.
    ///
    /// <para>
    /// The other way round from <see cref="Named"/>, and the reason both are here: one field holds a name bare and the
    /// next holds the same name in quotes, so saying what one holds and writing it into the other are two different
    /// spellings of the one name. Asked of a whole field, quotes and all, or of the run of words inside one — a run holds
    /// no bare quote either way, so the answer is the same.
    /// </para>
    /// </summary>
    public static string Said(string written)
    {
        var said = written.Trim();

        if (said.Length >= 2 && said[0] == '"' && said[^1] == '"') said = said[1..^1];

        return said.Replace(SankeyGrammar.Quoted, "\"", StringComparison.Ordinal).Trim();
    }

    /// <summary>A change as a sankey diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        part.Role is SankeyRoles.Source or SankeyRoles.Target ? Named(part, text) : MermaidParser.Spelled(part, text);

    /// <summary>
    /// A name as the field holding it is written: between quotes, a quote written twice; written bare, nothing that
    /// would end the field.
    /// </summary>
    private static string? Named(ContentPart part, string text) =>
        Quoted(part)
            ? text.Replace("\"", SankeyGrammar.Quoted, StringComparison.Ordinal)
            : text.Any(character => character is ',' or '"') ? null : text;

    /// <summary>
    /// Whether the field being written into is in quotes. A sankey field opens with a quote of its own rather than
    /// holding a quoted run, so this is asked of what holds the words as well as of the words themselves.
    /// </summary>
    private static bool Quoted(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Children.Any(child => child.Role == Roles.Open && child.Text == "\"")) return true;

        return false;
    }
}
