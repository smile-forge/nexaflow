using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

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
    /// <remarks>
    /// Only what the front matter asks for. Nothing declares a node: the nodes are the names the flows are written between, in
    /// the order they are first written, which is the order the lines are read in.
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new WithConfig<SankeyConfig>(SankeyConfig.Read(block.Config))];

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
}
