using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Latex.Stages;

/// <summary>
/// Gathers a sign that was written as several things into the one node it means.
///
/// <para>
/// The mirror of macro expansion, and it is worth seeing them as a pair. Expansion hangs structure
/// underneath that stands for <em>no</em> source — zero width, never printed. This re-nests structure
/// that stands for <em>all</em> of its source: every token stays, in the order it was written, and only
/// the shape over them changes. Both leave the tree printing as what it came from, which is the one
/// rule here.
/// </para>
/// <para>
/// <c>\not</c> is the case that needs it. It draws a slash over whatever follows, so <c>\not=</c> is
/// already one node meaning one sign — but physics writes <c>\not\!p</c>, pulling the slash onto the
/// letter with a kern, and a kern is not something to draw over. Read strictly, the <c>\!</c> becomes
/// the argument and the <c>p</c> is left outside as a neighbour, which is neither what was meant nor
/// something the builder can act on without reaching sideways out of its own node.
/// </para>
/// <para>
/// TeX agrees with the gathering rather than with the strict reading: <c>\not</c> overlays the next
/// <em>atom</em>, and a kern is not an atom. So this is the faithful shape and the parser's is the
/// literal one — which is the division of labour, the parser reading what is written and a stage
/// saying what it amounts to.
/// </para>
/// </summary>
public sealed class GatherSigns : IAstStage
{
    public string Name => "latex:signs";

    public ContentNode Run(ContentNode tree) => Gather(tree);

    /// <summary>Whether this piece takes up room without drawing anything - a kern, or written space.</summary>
    private static bool IsRoom(ContentNode node) =>
        node.Kind is Kinds.Space or Kinds.Comment
        || (node.Kind == TexKinds.Command
            && node.Part(Roles.Name)?.Text is { } name
            && TexCommands.IsSpacing(name));

    /// <summary>A <c>\not</c> whose argument turned out to be a kern, so what it slashes is further on.</summary>
    private static bool IsReaching(ContentNode node) =>
        node.Kind == TexKinds.Command
        && node.Part(Roles.Name)?.Text == @"\not"
        && node.Part(TexRole.Base) is { } written
        && IsRoom(written);

    /// <summary>Whether this says how the operator before it should wear its scripts.</summary>
    private static bool IsLimitWord(ContentNode node) =>
        node.Kind == TexKinds.Command
        && node.Part(Roles.Name)?.Text is @"\limits" or @"\nolimits";

    /// <summary>
    /// A script written after <c>\limits</c> belongs to the operator before it, not to the word.
    ///
    /// <para>
    /// <c>\sum\limits_{i}^{n}</c> reads as three things in a row, and the script attaches to whatever it
    /// was written after — which is <c>\limits</c>. So the operator sits outside the script wearing
    /// nothing, and the word wears the limits, which is the wrong way round in every sense. Gathered, the
    /// operator takes the word inside itself and becomes what the script is on.
    /// </para>
    /// <para>
    /// The word goes in as trivia. It is not a part of the operator in the sense that a numerator is part
    /// of a fraction — it says how the operator behaves and draws nothing — so <c>Parts</c> should not
    /// offer it to anything building the operator, while <c>Print</c> still puts it back where it was.
    /// </para>
    /// </summary>
    private static ContentNode? Limited(ContentNode operatorNode, ContentNode script)
    {
        if (script.Kind != TexKinds.Script) return null;
        if (script.Part(TexRole.Base) is not { } written || !IsLimitWord(written)) return null;
        if (operatorNode.Kind != TexKinds.Command || operatorNode.Part(Roles.Name) is null) return null;

        var carried = operatorNode.With([.. operatorNode.Children, written.As(Roles.Trivia)]);

        return script.With([.. script.Children.Select(
            child => ReferenceEquals(child, written) ? carried.As(TexRole.Base) : child)]);
    }

    private static ContentNode Gather(ContentNode node)
    {
        if (node.IsLeaf) return node;

        // Every child gathered once, up front. Looking ahead at the next child and gathering it there as
        // well cost the whole subtree twice at every level — which is exponential in depth, and turned a
        // five minute sweep over the corpus into one still running at twelve.
        var seen = new ContentNode[node.Children.Count];
        var moved = false;
        for (var at = 0; at < seen.Length; at++)
        {
            seen[at] = Gather(node.Children[at]);
            moved |= !ReferenceEquals(seen[at], node.Children[at]);
        }

        var rebuilt = new List<ContentNode>(seen.Length);

        for (var at = 0; at < seen.Length; at++)
        {
            var child = seen[at];

            // An operator and the word saying how it wears its scripts.
            if (at + 1 < seen.Length && Limited(child, seen[at + 1]) is { } limited)
            {
                rebuilt.Add(limited);
                at++;
                moved = true;
                continue;
            }

            if (IsReaching(child))
            {
                // Everything between the slash and what it is drawn over comes with it: the kern is what
                // puts the one on the other, so it belongs inside the sign rather than beside it.
                var next = at + 1;
                var between = new List<ContentNode>();
                while (next < seen.Length && IsRoom(seen[next])) between.Add(seen[next++]);

                if (next < seen.Length)
                {
                    rebuilt.Add(Slashing(child, between, seen[next]));
                    at = next;
                    moved = true;
                    continue;
                }
            }

            rebuilt.Add(child);
        }

        return moved ? node.With(rebuilt) : node;
    }

    /// <summary>
    /// One <c>\not</c> node holding the whole sign: its name, the room written after it, and the thing the
    /// slash goes over — in the order they were written, which is what keeps this printable.
    /// </summary>
    private static ContentNode Slashing(ContentNode reaching, List<ContentNode> between, ContentNode over)
    {
        var children = new List<ContentNode>(reaching.Children.Count + between.Count + 1);

        // The old argument was the first kern. It keeps its place and stops being the argument.
        foreach (var child in reaching.Children)
            children.Add(child.Role == TexRole.Base ? child.As(Roles.Element) : child);

        children.AddRange(between);
        children.Add(over.As(TexRole.Base));

        return reaching.With(children);
    }
}
