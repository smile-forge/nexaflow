using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Packet.Stages;

/// <summary>
/// What each escape and line ending written in a label says, hung under it (<see cref="ContentWords.Value"/>) — so what sets
/// a label's words sets that in its place, and never looks at the characters to find it.
///
/// <para>
/// A backslash keeps the character after it, as Mermaid's grammars read one: <c>\"</c> says a quote and <c>\\</c> a backslash.
/// A letter naming a control character names one — <c>\n</c>, <c>\r</c> and <c>\t</c> a line ending or a tab, which a label
/// drawn on one line shows as a space, as Mermaid's own does; <c>\b</c>, <c>\f</c>, <c>\v</c> and <c>\0</c> characters that
/// draw nothing. A line ending the label runs across is a space for the same reason.
/// </para>
/// </summary>
public sealed class SayEscapes : IAstStage
{
    /// <summary>What a line ending drawn on one line is.</summary>
    private const string Spaced = " ";

    public string Name => "packet:escapes";

    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, node => node.Kind switch
    {
        PacketKinds.Escape => node.Saying(PacketKinds.Escape, ContentWords.Value, Kept(node.Print())),
        PacketKinds.Break => node.Saying(PacketKinds.Break, ContentWords.Value, Spaced),
        _ => node,
    });

    /// <summary>What a backslash and the character after it say.</summary>
    private static string Kept(string escape) => escape[1] switch
    {
        'n' or 'r' or 't' or '\r' or '\n' => Spaced,
        'b' or 'f' or 'v' or '0' => string.Empty,
        var kept => kept.ToString(),
    };
}
