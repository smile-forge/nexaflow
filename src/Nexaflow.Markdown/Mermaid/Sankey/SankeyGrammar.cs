using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Sankey;

/// <summary>
/// What a <c>sankey-beta</c> block says beyond the lines every diagram shares: a flow to a line, written as the three
/// columns of a CSV row — where it comes from, where it goes, and what it is worth.
///
/// <para>
/// The rules are Mermaid's, which are RFC 4180's cut to three fields. A field holds anything but a comma and a quote as it
/// is; one that needs either is written in quotes, and a quote inside those is written twice. Nothing declares a node: the
/// nodes are the names the flows are written between, in the order they are first written.
/// </para>
/// </summary>
public sealed class SankeyGrammar : IMermaidGrammar
{
    /// <summary>What a quote inside a quoted field is written as.</summary>
    public const string Quoted = "\"\"";

    private const string Shape = "A flow is where it comes from, where it goes and what it is worth: Bio-conversion,Losses,26.862.";

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        if (!Field(line, SankeyRoles.Source) || !line.Token(",")) return line.Shown(Shape);
        if (!Field(line, SankeyRoles.Target) || !line.Token(",")) return line.Shown(Shape);

        line.Room();
        line.Amount(SankeyRoles.Value, MermaidNumber.Where(worth => worth >= 0, "A flow is worth nought or more."));
        line.Space();

        return line.Done ? line.Read(SankeyKinds.Flow) : line.Shown(Shape);
    }

    /// <inheritdoc/>
    /// <remarks>Another flow, with all three of its columns to write.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => (",,", 0);

    /// <inheritdoc/>
    /// <remarks>
    /// A name holds anything but a comma and a quote as it is written; one given either is put in quotes, and a quote between
    /// those is written twice. A value is a number, so nothing else goes in one.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is { Kind: MermaidKinds.Amount }) return Only(caret, text, character => char.IsAsciiDigit(character) || character is '.' or '-');
        if (part.Parent is not { } holder || part.Kind is not (MermaidKinds.Words or Kinds.Hole)) return null;

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);

        // Inside quotes a quote is written twice and everything else goes in as it is.
        if (holder.Children.Any(child => child.Role == Roles.Open && child.Text == "\""))
            return text.Contains('"')
                ? new MermaidWriting(caret, caret, text.Replace("\"", Quoted, StringComparison.Ordinal),
                                     caret + text.Replace("\"", Quoted, StringComparison.Ordinal).Length)
                : null;

        if (!(before + after).Any(character => character is ',' or '"')) return null;

        // Written bare and no longer able to be: the whole name is written again, in quotes.
        var head = "\"" + before.Replace("\"", Quoted, StringComparison.Ordinal);
        return new MermaidWriting(part.Start, part.Start + said.Length,
                                  head + after.Replace("\"", Quoted, StringComparison.Ordinal) + "\"", part.Start + head.Length);
    }

    /// <inheritdoc/>
    /// <remarks>A node is declared where it is first written, and used everywhere it is written again.</remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Length: > 0 } words) continue;
            if (words.Role is not (SankeyRoles.Source or SankeyRoles.Target)) continue;

            if (!said.TryGetValue(words.Text, out var places)) said[words.Text] = places = [];
            places.Add(name);
        }

        return [.. said.Select(name => new MermaidName(name.Key, name.Value[0], [.. name.Value.Skip(1)]))];
    }

    /// <inheritdoc/>
    /// <remarks>A name given a comma or a quote is written in quotes, with a quote between them written twice.</remarks>
    public string Naming(string name) =>
        name.Any(character => character is ',' or '"') ? "\"" + name.Replace("\"", Quoted, StringComparison.Ordinal) + "\"" : name;

    /// <inheritdoc/>
    /// <remarks>Between a name's quotes, where an empty name is written, and where a value is still to be written.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind == MermaidKinds.Amount || (node.Kind == MermaidKinds.Name && node.Width <= 2);

    /// <summary>
    /// One field, quoted or not: the name as it was written, quotes and all, so what it says is read back from it rather than
    /// worked out here.
    /// </summary>
    private static bool Field(MermaidLine line, string role)
    {
        line.Space();
        line.Open();

        if (line.Next == '"')
        {
            if (!line.Quoted(role, kind: null, what: "name", doubled: true))
            {
                line.Close(MermaidKinds.Name, role);
                return false;
            }
        }
        else
        {
            line.Words(role, until: ",\"");
        }

        line.Close(MermaidKinds.Name, role);
        line.Space();
        return true;
    }

    /// <summary>Text written where only some characters may go, with the rest dropped — or null where it all may go.</summary>
    private static MermaidWriting? Only(int caret, string text, Func<char, bool> holds)
    {
        if (text.All(holds)) return null;

        var kept = new string([.. text.Where(holds)]);
        return new MermaidWriting(caret, caret, kept, caret + kept.Length);
    }
}
