using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Packet;

/// <summary>
/// What a change to a packet's fields is written as: a field put in, taken out, made longer or shorter, or carried to another
/// place among them — the write half of <see cref="PacketParser"/> for what is about fields rather than words.
///
/// <para>
/// <strong>The fields stay contiguous, and keep their lengths.</strong> Whatever is changed, every field is numbered again from bit
/// 0, each starting at the bit after the one before it and taking as many bits as it did — a field made longer or shorter the
/// number it is given — so a packet that read before still reads, and no field changes width because another moved.
/// </para>
/// <para>
/// <strong>A field keeps the form it is written in.</strong> Only its numbers are written over: a range stays a range, one bit
/// stays one bit until it is made longer and becomes a range, and a field counting its bits keeps its count, which no change of
/// place touches. Its label, the space round its numbers and a comment after it are left as they were. A number still to write
/// stays still to write, and the end of a field whose width cannot be known stays as it was written, wrong as it may be.
/// </para>
/// </summary>
public static class PacketWriting
{
    /// <summary>What a field put in says before anybody writes in it.</summary>
    private const string Unwritten = ": \"\"";

    /// <summary>Every field, in the order written.</summary>
    public static List<ContentPart> Fields(ContentPart root) => [.. root.SelfAndDescendants().Where(part => part.Node is PacketFieldNode)];

    /// <summary>The field <paramref name="part"/> is in — the field itself, or anything written in it — or null.</summary>
    public static ContentPart? FieldOf(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Node is PacketFieldNode) return at;

        return null;
    }

    /// <summary>How many bits a field takes as it is drawn — one, where how many cannot be known.</summary>
    public static long Size(ContentPart field) => field.Node is PacketFieldNode said ? said.Last - said.First + 1 : 1;

    /// <summary>Whether how many bits a field takes is known — not where its end or its count is still to write, or wrong.</summary>
    public static bool Sized(ContentPart field) => field.Node is PacketFieldNode { Unsized: null };

    /// <summary>A field made <paramref name="bits"/> long, and every field after it numbered on from it.</summary>
    public static ContentChange Resized(ContentPart root, ContentPart field, long bits, int caret)
    {
        var fields = Fields(root);
        var writes = Renumbered(fields, [.. fields.Select(each => new Standing(each, each == field ? bits : null))]);

        return new ContentChange(writes, After(writes, caret));
    }

    /// <summary>
    /// The fields <paramref name="chosen"/> taken out, line and all, and every field left numbered on from bit 0 — the caret where
    /// the first of them was.
    /// </summary>
    public static ContentChange Removed(ContentPart root, IReadOnlyList<ContentPart> chosen)
    {
        var fields = Fields(root);
        var kept = fields.Where(field => !chosen.Contains(field)).ToList();
        var writes = Renumbered(kept, [.. kept.Select(field => new Standing(field, null))]);

        foreach (var field in chosen) writes.Add(new ContentWrite(Line(field), string.Empty));

        // The last line has no line ending of its own to take with it, so the one before it gives up its own instead.
        if (chosen.Contains(fields[^1]) && !Ends(Line(fields[^1]))
            && fields.LastOrDefault(field => !chosen.Contains(field)) is { } before && Ending(Line(before)) is { } ending)
            writes.Add(new ContentWrite(ending, string.Empty));

        return new ContentChange(writes, After(writes, Line(chosen[0]).Start));
    }

    /// <summary>
    /// A field one bit long put in on a line of its own beside <paramref name="at"/> — after it, or before it — written in the
    /// form <paramref name="at"/> is, and every field after it numbered on from it; the caret in its label.
    /// </summary>
    public static ContentChange Inserted(ContentPart root, ContentPart at, bool after, string newline)
    {
        var fields = Fields(root);
        var place = fields.IndexOf(at) + (after ? 1 : 0);

        var standing = fields.Select(field => new Standing(field, null)).ToList();
        standing.Insert(place, new Standing(null, 1));

        var laid = Laid(standing);
        var fresh = Fresh(Counted(at), laid[place].First);
        var own = laid.First(each => each.Standing.Field == at).Text;

        var writes = Writes(fields.Where(field => field != at), laid);
        var indent = Indent(Line(at));
        var written = after ? own + newline + indent + fresh : fresh + newline + indent + own;

        var opened = (after ? own.Length + newline.Length + indent.Length : 0) + fresh.IndexOf('"', StringComparison.Ordinal) + 1;
        var caret = After(writes, at.Start) + opened;

        writes.Add(new ContentWrite(at, written));
        return new ContentChange(writes, caret);
    }

    /// <summary>
    /// A field one bit long put in on a line of its own after everything written — the first field, where there is none — and
    /// the caret in its label.
    /// </summary>
    public static ContentChange Appended(ContentPart root, string newline)
    {
        var fields = Fields(root);
        if (fields.Count > 0) return Inserted(root, fields[^1], after: true, newline);

        var last = root.Children.Last(part => part.Kind == MermaidKinds.Line);
        var said = last.Node.Print();
        var fresh = Fresh(counted: false, 0);

        var written = Ends(last) ? said + fresh + newline : said + newline + fresh;
        var caret = last.Start + (Ends(last) ? said.Length : said.Length + newline.Length) + fresh.IndexOf('"', StringComparison.Ordinal) + 1;

        return new ContentChange([new ContentWrite(last, written)], caret);
    }

    /// <summary>
    /// The fields <paramref name="chosen"/> carried and let go on <paramref name="over"/>: they and it change places, the rest
    /// shifting over, and every field numbered on from bit 0 in its new place. Each field's line is given the field now standing
    /// there, so not one character of any field changes but its numbers. Null where they are let go on themselves.
    /// </summary>
    public static ContentChange? Moved(ContentPart root, IReadOnlyList<ContentPart> chosen, ContentPart over)
    {
        if (chosen.Contains(over)) return null;

        var fields = Fields(root);
        var order = fields.Where(field => !chosen.Contains(field)).ToList();
        var at = order.IndexOf(over) + (fields.IndexOf(over) > fields.IndexOf(chosen[0]) ? 1 : 0);
        order.InsertRange(at, chosen);

        var laid = Laid([.. order.Select(field => new Standing(field, null))]);
        var writes = new List<ContentWrite>();

        for (var place = 0; place < fields.Count; place++)
            if (laid[place].Text != fields[place].Node.Print())
                writes.Add(new ContentWrite(fields[place], laid[place].Text!));

        return new ContentChange(writes, After(writes, fields[at].Start));
    }

    // ── Numbering ───────────────────────────────────────────────────────────

    /// <summary>A field in the order the change leaves them — or one being put in, where it has none — and how many bits it is made to take, or null for as many as it takes.</summary>
    private readonly record struct Standing(ContentPart? Field, long? Bits);

    /// <summary>A field where it now stands: the bit it starts at, and how it is written there — null for one being put in.</summary>
    private readonly record struct Placed(Standing Standing, long First, string? Text);

    /// <summary>The fields numbered from bit 0 in the order given, each on from the one before it.</summary>
    private static List<Placed> Laid(IReadOnlyList<Standing> order)
    {
        var laid = new List<Placed>(order.Count);
        var next = 0L;

        foreach (var standing in order)
        {
            var bits = standing.Bits ?? Size(standing.Field!);
            laid.Add(new Placed(standing, next, standing.Field is { } field ? Respelled(field, next, standing.Bits) : null));
            next += bits;
        }

        return laid;
    }

    /// <summary>Each of <paramref name="fields"/>, numbered as <paramref name="order"/> says, written over where its numbers change.</summary>
    private static List<ContentWrite> Renumbered(IReadOnlyList<ContentPart> fields, IReadOnlyList<Standing> order) =>
        Writes(fields, Laid(order));

    /// <summary>A write over each of <paramref name="fields"/> whose numbers change where it stands in <paramref name="laid"/>.</summary>
    private static List<ContentWrite> Writes(IEnumerable<ContentPart> fields, IReadOnlyList<Placed> laid)
    {
        var writes = new List<ContentWrite>();

        foreach (var field in fields)
            if (laid.First(each => each.Standing.Field == field).Text is { } text && text != field.Node.Print())
                writes.Add(new ContentWrite(field, text));

        return writes;
    }

    /// <summary>
    /// <paramref name="field"/> as it is written, numbered to start at <paramref name="first"/> and — where <paramref name="bits"/>
    /// says — made that many bits long, in the form it is written in.
    /// </summary>
    private static string Respelled(ContentPart field, long first, long? bits)
    {
        var said = (PacketFieldNode)field.Node;
        var start = Number(field, PacketRoles.Start);
        var end = Number(field, PacketRoles.End);
        var count = Number(field, PacketRoles.Bits);
        var last = first + (bits ?? Size(field)) - 1;

        var swaps = new List<(ContentPart Number, string Text)>();

        if (count is not null)
        {
            if (bits is { } made) swaps.Add((count, Said(made)));
        }
        else if (end is not null)
        {
            if (start is { Length: > 0 }) swaps.Add((start, Said(first)));
            if (bits is not null || (said.Unsized is null && end.Length > 0)) swaps.Add((end, Said(last)));
        }
        else if (start is not null && (start.Length > 0 || bits is not null))
        {
            swaps.Add((start, last > first ? Said(first) + PacketParser.Dash + Said(last) : Said(first)));
        }

        var text = field.Node.Print();
        foreach (var (number, written) in swaps.OrderByDescending(swap => swap.Number.Start))
            text = text[..(number.Start - field.Start)] + written + text[(number.End - field.Start)..];

        return text;
    }

    /// <summary>A field one bit long, still to be called anything: counting its bit, or numbering it.</summary>
    private static string Fresh(bool counted, long first) =>
        (counted ? PacketParser.Plus + "1" : Said(first)) + Unwritten;

    /// <summary>Whether a field counts its bits rather than numbering them.</summary>
    private static bool Counted(ContentPart field) => Number(field, PacketRoles.Bits) is not null;

    /// <summary>The bit number written in <paramref name="field"/> as <paramref name="role"/>, or null where none is written as that.</summary>
    private static ContentPart? Number(ContentPart field, string role) =>
        field.SelfAndDescendants().FirstOrDefault(part => part.Kind == Kinds.Number && part.Role == role);

    private static string Said(long bit) => bit.ToString(CultureInfo.InvariantCulture);

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>The line a field is written on.</summary>
    private static ContentPart Line(ContentPart field) => field.Parent is { Kind: MermaidKinds.Line } line ? line : field;

    /// <summary>The space a line starts with.</summary>
    private static string Indent(ContentPart line) =>
        line.Children.FirstOrDefault() is { Kind: Kinds.Space } space && space.Start == line.Start ? space.Text : string.Empty;

    /// <summary>The line ending that closes a line, where it has one.</summary>
    private static ContentPart? Ending(ContentPart line) =>
        line.Children.LastOrDefault() is { Kind: Kinds.Space } space && space.Text.EndsWith('\n') ? space : null;

    /// <summary>Whether a line is closed by a line ending of its own.</summary>
    private static bool Ends(ContentPart line) => Ending(line) is not null;

    /// <summary>Where <paramref name="offset"/> as the source stood is, once <paramref name="writes"/> are made.</summary>
    private static int After(IEnumerable<ContentWrite> writes, int offset) =>
        offset + writes.Where(write => write.End <= offset).Sum(write => write.Text.Length - write.Length);
}
