using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Music.Abc.Stages;

/// <summary>
/// Gathers a tuplet marker and the events it covers into one node.
///
/// <para>
/// <c>(3</c> means the next three notes go in the time of two, and <c>(p:q:r</c> spells all three numbers
/// out. The marker is written before the notes and says nothing about where they stop, so what a tuplet
/// <em>is</em> can only be worked out by counting forward — which is exactly the kind of thing a stage
/// exists to do once rather than everywhere.
/// </para>
/// <para>
/// Runs before the beams, because a tuplet beams as one group and a group that is not there yet cannot be
/// beamed as one. What the numbers mean is worked out here too, and hung on the group: how many notes, in
/// the time of how many. The defaults for <c>q</c> follow the ABC standard, where the odd-numbered
/// tuplets mean one thing in a simple meter and another in a compound one — which is why this needs the
/// meter, and so runs after the context.
/// </para>
/// </summary>
public sealed class GroupTuplets : IAstStage
{
    public string Name => "abc:tuplets";

    private static bool IsEvent(ContentNode node) =>
        node.Kind is AbcKinds.Note or AbcKinds.Chord or AbcKinds.Rest;

    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Regrouping(tree, (node, children) =>
            node.Kind == AbcKinds.Line ? Grouped(children, ResolveContext.Of(node)) : null);

    private static IReadOnlyList<ContentNode>? Grouped(IReadOnlyList<ContentNode> children, AbcContext context)
    {
        var rebuilt = new List<ContentNode>(children.Count);
        var moved = false;
        var at = 0;

        while (at < children.Count)
        {
            if (children[at].Kind != AbcKinds.Tuplet)
            {
                rebuilt.Add(children[at]);
                at++;
                continue;
            }

            var (notes, time, count) = Read(children[at].Text, context);

            // How far forward the marker reaches: the r events after it, and whatever is written among
            // them. A marker that runs off the end of the line covers what there is, which is what
            // somebody halfway through typing it has written.
            var end = at + 1;
            var found = 0;
            while (end < children.Count && found < count)
            {
                if (children[end].Kind is AbcKinds.Barline or Kinds.Comment) break;
                if (IsEvent(children[end])) found++;
                end++;
            }

            if (found == 0)
            {
                rebuilt.Add(children[at]);
                at++;
                continue;
            }

            rebuilt.Add(ContentNode
                .Branch(AbcKinds.TupletGroup, [.. children.Skip(at).Take(end - at)])
                .Saying(
                    (AbcKinds.Text, AbcRoles.Value, $"{notes}"),
                    (AbcKinds.Text, AbcRoles.Duration, $"{time}")));

            at = end;
            moved = true;
        }

        return moved ? rebuilt : null;
    }

    /// <summary>
    /// What <c>(p</c>, <c>(p:q</c> or <c>(p:q:r</c> means: p notes, in the time of q, over the next r.
    /// <para>
    /// The defaults are the standard's and they are not uniform, because the odd-numbered tuplets are
    /// read against the meter: a <c>(3</c> is three in the time of two in a simple meter and three in the
    /// time of two in a compound one too, but a <c>(2</c> is two in the time of three, and a bare <c>(5</c>
    /// takes whichever the meter suggests.
    /// </para>
    /// </summary>
    private static (int Notes, int Time, int Count) Read(string marker, AbcContext context)
    {
        var at = 1;                              // past the '('
        var notes = (int)Number(marker, ref at);
        if (notes <= 0) return (0, 0, 0);

        var time = 0;
        var count = 0;

        if (at < marker.Length && marker[at] == ':')
        {
            at++;
            time = (int)Number(marker, ref at);
            if (at < marker.Length && marker[at] == ':')
            {
                at++;
                count = (int)Number(marker, ref at);
            }
        }

        var compound = context.BeatUnit == 8 && context.Beats % 3 == 0;
        if (time <= 0) time = notes switch { 2 => 3, 3 => 2, 4 => 3, 6 => 2, 8 => 3, _ => compound ? 3 : 2 };
        if (count <= 0) count = notes;

        return (notes, time, count);
    }

    private static long Number(string text, ref int at)
    {
        long value = 0;
        while (at < text.Length && char.IsAsciiDigit(text[at])) { value = (value * 10) + (text[at] - '0'); at++; }
        return value;
    }

    // ── Reading the answer back ─────────────────────────────────────────────

    /// <summary>How many notes this group holds, and how many notes' worth of time it takes.</summary>
    public static (int Notes, int Time) Of(ContentNode group) =>
        (int.TryParse(group.Said(AbcRoles.Value), out var notes) ? notes : 0,
         int.TryParse(group.Said(AbcRoles.Duration), out var time) ? time : 0);
}
