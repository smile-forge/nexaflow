using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Packet.Stages;

/// <summary>
/// Which bits each field takes and how they fall across the rows, said on the field (<see cref="PacketFieldNode"/>) — and what is
/// wrong with a field that only the fields before it show, said on the number at fault.
///
/// <para>
/// The rules are Mermaid's. The fields are contiguous: the first starts at bit 0, and each after it at the bit after the one
/// before it ended — which is where one counting its bits starts, and where one whose start is still to write is put. A field
/// ends at or after the bit it starts at, and takes a bit or more. A packet is drawn to 10,000 rows, as Mermaid's is. Where a
/// field breaks a rule it still has bits — the ones it was written with, or the one it starts at where how many it takes
/// cannot be known — so it is drawn where the reader can see it, and the number that broke the rule is marked where it is
/// drawn; the next field goes on from the bit after it.
/// </para>
/// <para>
/// A row holds <see cref="PacketConfig.BitsPerRow"/> bits, and a field running past the end of one goes on into the next as a
/// run of its own: how many bits a row holds is the front matter's, so it is a fact of this reading rather than of drawing it.
/// </para>
/// </summary>
public sealed class PlaceFields(PacketConfig config) : IAstStage
{
    /// <summary>How many rows a packet is drawn to, as Mermaid's is.</summary>
    public const int MostRows = 10_000;

    public string Name => "packet:fields";

    public ContentNode Run(ContentNode tree)
    {
        // Depth first, so the fields are met in the order they were written — which is the order their bits run in.
        var next = 0L;
        return AstRewrite.Each(tree, node => node.Kind == PacketKinds.Field ? Placed(node, ref next) : node);
    }

    private PacketFieldNode Placed(ContentNode field, ref long next)
    {
        var start = Number(field, PacketRoles.Start);
        var end = Number(field, PacketRoles.End);
        var count = Number(field, PacketRoles.Bits);
        var wrong = new Dictionary<ContentNode, string>(ReferenceEqualityComparer.Instance);

        long first, last;
        string? unsized = null;

        if (count is not null)
        {
            first = next;
            last = first;

            if (Read(count) is not { } bits) unsized = PacketRoles.Bits;
            else if (bits == 0)
            {
                wrong[count] = "A field takes a bit or more: +0 takes none.";
                unsized = PacketRoles.Bits;
            }
            else last = first + bits - 1;
        }
        else
        {
            // A start still to write, or written wrong — which says so itself — is where the field would start.
            first = start is not null && Read(start) is { } written ? written : next;
            if (first != next) wrong[start!] = Misplaced(first, next);

            last = first;
            if (end is not null)
            {
                if (Read(end) is not { } ends) unsized = PacketRoles.End;
                else if (ends < first)
                {
                    wrong[end] = $"A field ends at or after the bit it starts at: {Said(ends)} is before {Said(first)}.";
                    unsized = PacketRoles.End;
                }
                else last = ends;
            }
        }

        next = last + 1;

        // What is past the last row is not drawn, as Mermaid draws none of it; the number that took it there says so.
        var most = (long)MostRows * config.BitsPerRow;
        if (last >= most && (end ?? count ?? start) is { } far && !wrong.ContainsKey(far))
            wrong[far] = $"A packet is drawn to {Said(MostRows)} rows of {Said(config.BitsPerRow)} bits, which bit {Said(last)} is past.";

        var said = wrong.Count == 0 ? field : AstRewrite.Each(field, node => wrong.TryGetValue(node, out var reason) ? node.Saying(reason) : node);

        return new PacketFieldNode(said)
        {
            First = first,
            Last = last,
            Spans = Spans(first, Math.Min(last, most - 1)),
            Unsized = unsized,
        };
    }

    /// <summary>The runs of the bits from <paramref name="first"/> to <paramref name="last"/>, a row each — none where the first is past the last row.</summary>
    private List<PacketSpan> Spans(long first, long last)
    {
        var spans = new List<PacketSpan>();
        var row = config.BitsPerRow;

        for (var at = first; at <= last;)
        {
            var ends = Math.Min(last, ((at / row) + 1) * row - 1);
            spans.Add(new PacketSpan((int)(at / row), at, ends));
            at = ends + 1;
        }

        return spans;
    }

    /// <summary>Why a field written to start at <paramref name="first"/> starts in the wrong place, when the bit after the field before it is <paramref name="next"/>.</summary>
    private static string Misplaced(long first, long next) =>
        next == 0 ? $"The first field starts at bit 0, not at {Said(first)}."
        : first > next ? $"The field before this one ends at bit {Said(next - 1)}, so this one starts at {Said(next)}: bits {Said(next)} to {Said(first - 1)} belong to no field."
        : $"Bit {Said(first)} already belongs to a field above this one, which ends at bit {Said(next - 1)}: this one starts at {Said(next)}.";

    /// <summary>The bit number written in <paramref name="field"/> as <paramref name="role"/>, or null where none is written there.</summary>
    private static ContentNode? Number(ContentNode field, string role) =>
        field.SelfAndDescendants().FirstOrDefault(node => node.Kind == Kinds.Number && node.Role == role);

    /// <summary>What a bit number says, where it is written and is a whole number — null where it is still to write or wrong.</summary>
    private static long? Read(ContentNode number) =>
        number is { Trouble: null, Width: > 0 } && long.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var bit) ? bit : null;

    private static string Said(long bit) => bit.ToString(CultureInfo.InvariantCulture);
}
