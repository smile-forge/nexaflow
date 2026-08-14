using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// How one value becomes octets, and how octets become that value again.
///
/// <para>
/// This is the half of <see cref="Pattern"/> that was never structural. A pattern says two unrelated
/// things at once — how a field is <i>shaped out of other fields</i> (a region, a run of elements, a set
/// of bit runs) and how a single value is <i>laid down</i> (a fixed-width integer, a continuation chain,
/// a marked integer). Only the first concerns the walk. Only the second differs between one integer
/// encoding and another, and it was written as arms of a <c>switch</c> in the codec, which is why adding
/// an encoding meant editing the engine and why a field the switch had no arm for was silently skipped.
/// </para>
///
/// <para>
/// <b>Both directions, one object.</b> A form knows how to lay a value down and how to take it back, so
/// the pair cannot drift — which the split version did: reading a marked integer used to be "work out the
/// width" in one method and "read that many octets" in another, two functions of the same marker bits
/// that had to agree by hand.
/// </para>
/// </summary>
/// <summary>
/// Which end of a multi-group encoding carries the most significant part.
///
/// <para>
/// Named as a notion rather than after either family that exhibits it. Most-significant-first reads
/// <c>8f 65</c> as 2021; least-significant-first reads <c>8f 01</c> as 143. There is no defensible
/// default — omitting the parameter is how a general name comes to sit over one family's codec.
/// </para>
/// </summary>
public enum GroupOrder
{
    MostSignificantFirst,
    LeastSignificantFirst,
}

public abstract record WireForm
{
    /// <summary>
    /// Whether the octets themselves say where this ends.
    ///
    /// <para>
    /// The one thing the walk has to ask, and the reason it is asked here rather than decided by the walk:
    /// a self-delimiting form settles its own extent while reading, and anything else has to be told by
    /// something already settled. That is the direction asymmetry in a single property — going out, extent
    /// always falls out of the value; coming in, it either falls out of the octets or it does not.
    /// </para>
    /// </summary>
    public abstract bool SelfDelimiting { get; }

    /// <summary>Bits this always occupies, where the declaration fixes it. Null means it depends on the
    /// value, which is the case the resolver's facet ordering exists for.</summary>
    public virtual int? FixedBits => null;

    /// <summary>Lays a value down. How many bits that took is whatever the writer advanced by.</summary>
    public abstract void Lay(BitWriter wire, ProtoValue value, Wiring how);

    /// <summary>
    /// Takes the value back, and says how many bits it consumed.
    /// </summary>
    /// <param name="bits">How far this runs, where something else settled it. Ignored by a form that
    /// delimits itself, and required by one that does not.</param>
    public abstract Taken Take(BitCursor ahead, int? bits, Wiring how);

    /// <summary>A whole number of octets, where this form is fixed at one.</summary>
    public int? FixedOctets => FixedBits is { } bits && bits % 8 == 0 ? bits / 8 : null;

    // ── The forms ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A run of bits, most significant first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The form that lets a bit run stop being part of a shape. A group of runs was a
    /// <c>Pattern.Bits</c> holding its own list of them, which made every run two things at once: a node
    /// in the graph with its own requirements path, and an entry in a shape carrying the same name and
    /// width. Reading survived that — reading needs only the width — and writing did not, because it has
    /// to find what computes each run and the copies inside the shape compute nothing.
    /// </para>
    /// <para>
    /// As a form there is no second copy. Runs are ordinary fields packed together, and eight bits going
    /// by is an octet, which the writer already believed. Nothing about a group of them is a special case:
    /// three fields of five, three and eight bits produce two octets because that is what laying down
    /// sixteen bits does.
    /// </para>
    /// </remarks>
    public sealed record Run(int Bits) : WireForm
    {
        public override bool SelfDelimiting => true;
        public override int? FixedBits => Bits;

        public override void Lay(BitWriter wire, ProtoValue value, Wiring how)
            => wire.Put(value.AsInt(), Bits);

        public override Taken Take(BitCursor ahead, int? bits, Wiring how)
        {
            if (!ahead.Holds(Bits)) throw how.Wrong($"wants {Bits} bit(s) and {ahead.Remaining} remain");

            return new Taken(ProtoValue.Of(ahead.Read(Bits)), Bits);
        }
    }

    /// <summary>An integer of a width the declaration fixes.</summary>
    public sealed record Scalar(int Octets, bool BigEndian, bool Signed) : WireForm
    {
        public override bool SelfDelimiting => true;
        public override int? FixedBits => Octets * 8;

        public override void Lay(BitWriter wire, ProtoValue value, Wiring how)
        {
            long held = value.AsInt();
            var octets = new byte[Octets];

            for (int i = Octets - 1; i >= 0; i--) { octets[i] = (byte)(held & 0xFF); held >>= 8; }

            foreach (var octet in BigEndian ? octets : octets.Reverse()) wire.Put(octet, 8);
        }

        public override Taken Take(BitCursor ahead, int? bits, Wiring how)
        {
            var octets = ahead.Octets(Octets, how);
            long value = 0;

            foreach (var octet in BigEndian ? octets : octets.Reverse()) value = (value << 8) | octet;

            if (Signed && Octets < 8 && (value & (1L << ((Octets * 8) - 1))) != 0)
                value -= 1L << (Octets * 8);

            return new Taken(ProtoValue.Of(value), Octets * 8);
        }
    }

    /// <summary>A run of octets carried without interpretation, sized by the declaration or by something
    /// already settled.</summary>
    public sealed record Opaque(int? Octets) : WireForm
    {
        public override bool SelfDelimiting => Octets is not null;
        public override int? FixedBits => Octets * 8;

        public override void Lay(BitWriter wire, ProtoValue value, Wiring how)
        {
            var octets = value.AsBytes();

            if (Octets is { } declared && octets.Length != declared)
                throw how.Wrong($"is {declared} octet(s) and was given {octets.Length}");

            wire.Put(octets);
        }

        public override Taken Take(BitCursor ahead, int? bits, Wiring how)
        {
            int wanted = Octets ?? (bits is { } told && told % 8 == 0
                ? told / 8
                : throw how.Wrong("is a run of octets that nothing sized"));

            return new Taken(ProtoValue.Of(ahead.Octets(wanted, how)), wanted * 8);
        }
    }

    /// <summary>
    /// An integer spread across octets with a continue flag, so its width is a function of its value.
    /// </summary>
    /// <remarks>
    /// The codec is borrowed from the converter table rather than written again, because a second copy is
    /// a second thing to keep true and the group-order parameter is exactly where a duplicate quietly
    /// picks one family's answer.
    /// </remarks>
    public sealed record Varint(GroupOrder Order, int MaxOctets, bool Minimal) : WireForm
    {
        public override bool SelfDelimiting => true;

        private ProtoValue Grouping
            => ProtoValue.Of(Order == GroupOrder.MostSignificantFirst ? "msbFirst" : "lsbFirst");

        public override void Lay(BitWriter wire, ProtoValue value, Wiring how)
        {
            var octets = how.Apply("base128", value, Grouping).AsBytes();

            if (octets.Length > MaxOctets)
                throw how.Wrong($"needs {octets.Length} octet(s), past the {MaxOctets} it allows");

            wire.Put(octets);
        }

        public override Taken Take(BitCursor ahead, int? bits, Wiring how)
        {
            // The continue flag always marks "another group follows", whichever order the groups are in,
            // so finding the end never needs to know which order that is.
            for (int n = 0; n < MaxOctets; n++)
            {
                if (!ahead.Holds((n + 1) * 8))
                    throw how.Wrong("has a continuation chain that runs off the end of its region");

                if ((ahead.Peek(8, skip: n * 8) & 0x80) != 0) continue;

                var octets = ahead.Octets(n + 1, how);
                return new Taken(how.Apply("unbase128", ProtoValue.Of(octets), Grouping), (n + 1) * 8);
            }

            throw how.Wrong($"is still continuing after {MaxOctets} octet(s), which is its declared bound");
        }
    }

    /// <summary>A small value carried in one octet, escaping to a counted run when it will not fit.</summary>
    public sealed record EscapedInline(long InlineLimit, int MaxOctets, bool Minimal) : WireForm
    {
        public override bool SelfDelimiting => true;

        public override void Lay(BitWriter wire, ProtoValue value, Wiring how)
        {
            long held = value.AsInt();

            if (held < 0) throw how.Wrong($"cannot be negative, got {held}");

            if (held < InlineLimit) { wire.Put(held, 8); return; }

            var octets = how.Apply("minuint", value, ProtoValue.Of("oneByte")).AsBytes();

            if (octets.Length > MaxOctets)
                throw how.Wrong($"needs {octets.Length} octet(s), past the {MaxOctets} it allows");

            wire.Put(InlineLimit + octets.Length, 8);
            wire.Put(octets);
        }

        public override Taken Take(BitCursor ahead, int? bits, Wiring how)
        {
            if (!ahead.Holds(8)) throw how.Wrong("has no octets left for its marker");

            long marker = ahead.Peek(8);

            if (marker < InlineLimit)
            {
                ahead.Skip(8);
                return new Taken(ProtoValue.Of(marker), 8);
            }

            long counted = marker - InlineLimit;

            if (counted > MaxOctets)
                throw how.Wrong($"has a marker counting {counted} octet(s), past the {MaxOctets} it allows");

            ahead.Skip(8);
            var octets = ahead.Octets((int)counted, how);

            return new Taken(how.Apply("unminuint", ProtoValue.Of(octets)), (int)(counted + 1) * 8);
        }
    }

    /// <summary>An integer whose leading bits say how wide it is, and whose remaining bits are part
    /// of it.</summary>
    public sealed record Prefixed(int Marker, IReadOnlyList<int> Widths, bool Minimal) : WireForm
    {
        public override bool SelfDelimiting => true;

        /// <summary>The largest value a given width holds, once the marker has taken its bits.</summary>
        public long Ceiling(int octets) => (1L << ((octets * 8) - Marker)) - 1;

        /// <summary>The narrowest width that holds a value, and which marker selects it.</summary>
        public int Narrowest(long value)
        {
            for (int i = 0; i < Widths.Count; i++)
                if (value <= Ceiling(Widths[i])) return i;

            return -1;
        }

        public override void Lay(BitWriter wire, ProtoValue value, Wiring how)
        {
            long held = value.AsInt();

            if (held < 0) throw how.Wrong($"cannot be negative, got {held}");

            int narrowest = Narrowest(held);

            if (narrowest < 0)
                throw how.Wrong(
                    $"needs more than the {Widths[^1]} octet(s) it allows — the widest it offers holds up "
                  + $"to {Ceiling(Widths[^1])}");

            int width = Widths[narrowest];
            var octets = new byte[width];

            for (int i = width - 1; i >= 0; i--) { octets[i] = (byte)(held & 0xFF); held >>= 8; }

            octets[0] |= (byte)(narrowest << (8 - Marker));
            wire.Put(octets);
        }

        public override Taken Take(BitCursor ahead, int? bits, Wiring how)
        {
            if (!ahead.Holds(8)) throw how.Wrong("has no octets left for its marker");

            int selected = (int)ahead.Peek(Marker);

            if (selected >= Widths.Count)
                throw how.Wrong($"has a marker of {selected}, and only {Widths.Count} width(s) are offered");

            int width = Widths[selected];
            var octets = ahead.Octets(width, how);

            // The marker's own bits come off the first octet; what is left of it is the value's top.
            long value = octets[0] & ((1 << (8 - Marker)) - 1);

            for (int i = 1; i < octets.Length; i++) value = (value << 8) | octets[i];

            return new Taken(ProtoValue.Of(value), width * 8);
        }
    }
}

/// <summary>What came off the wire, and how much of it went.</summary>
public readonly record struct Taken(ProtoValue Value, int Bits);

/// <summary>
/// What a form is allowed to reach for: the closed converter set, and whose field this is.
/// </summary>
/// <remarks>
/// Passed in rather than held, so a form stays a description of an encoding rather than a thing wired to
/// a table — and so an error names the field an author has to go and look at, which is the only reason
/// the owner is here at all.
/// </remarks>
public readonly record struct Wiring(ConverterTable Converters, string Owner)
{
    public ProtoValue Apply(string converter, ProtoValue value, params ProtoValue[] args)
        => Converters.TryGet(converter, out var found) && found is not null
            ? found.Apply(value, args)
            : throw new ProtoTypeException($"field '{Owner}': unknown converter '{converter}'");

    public ProtoTypeException Wrong(string complaint) => new($"field '{Owner}' {complaint}");
}

/// <summary>
/// Octets, built out of bits.
/// </summary>
/// <remarks>
/// The engine's unit of emission is a <b>bit</b>, and an octet is what falls out when eight of them have
/// gone by. That is not only for bit groups: it is the one thing that makes a field which is not a whole
/// number of octets expressible at all, and there are protocols with them.
/// </remarks>
public sealed class BitWriter
{
    private readonly List<byte> _octets = [];
    private long _held;
    private int _bits;

    /// <summary>How many bits have gone by.</summary>
    public int Written { get; private set; }

    public void Put(long value, int width)
    {
        for (int at = width - 1; at >= 0; at--)
        {
            _held = (_held << 1) | ((value >> at) & 1);

            if (++_bits != 8) continue;

            _octets.Add((byte)_held);
            _held = 0;
            _bits = 0;
        }

        Written += width;
    }

    public void Put(byte[] octets) { foreach (var octet in octets) Put(octet, 8); }

    /// <summary>What has been written since a mark, for a node to hold as its own octets.</summary>
    public byte[] Since(int mark)
        => mark % 8 != 0 || Written % 8 != 0
            ? []
            : [.. _octets.Skip(mark / 8).Take((Written - mark) / 8)];

    /// <summary>
    /// Everything written, where it comes to whole octets.
    /// </summary>
    /// <remarks>
    /// A message that ends mid-octet is an error rather than something padded, for the reason padding is
    /// always an error unless a document asked for it: the octets that go out have to be the ones the
    /// document accounted for.
    /// </remarks>
    public byte[] Done(string id)
        => _bits == 0
            ? [.. _octets]
            : throw new ProtoTypeException(
                  $"message '{id}' comes to {Written} bits, which is not a whole number of octets. What "
                + "goes out has to be what the document accounted for, so this is an error rather than "
                + "something padded.");
}

/// <summary>
/// A place in the octets being read, counted in bits.
/// </summary>
/// <remarks>
/// Bits rather than octets for the same reason the writer counts them: a field that does not start on an
/// octet boundary can be described, so it has to be readable. Everything here works at whatever alignment
/// it finds itself at, and the octet-aligned case is not a special one.
/// </remarks>
public sealed class BitCursor(byte[] octets, int at = 0)
{
    /// <summary>Bits from the start of what is being read.</summary>
    public int At { get; private set; } = at;

    public int Remaining => (octets.Length * 8) - At;

    /// <summary>Whether this many bits are still ahead.</summary>
    public bool Holds(int bits) => Remaining >= bits;

    /// <summary>The next bits as an unsigned number, without moving.</summary>
    public long Peek(int bits, int skip = 0)
    {
        if (!Holds(skip + bits))
            throw new ProtoTypeException(
                $"{bits} bit(s) wanted at bit {At + skip} and only {Remaining - skip} remain");

        long value = 0;

        for (int i = 0; i < bits; i++)
        {
            int bit = At + skip + i;
            value = (value << 1) | (long)((octets[bit >> 3] >> (7 - (bit & 7))) & 1);
        }

        return value;
    }

    public void Skip(int bits) => At += bits;

    public long Read(int bits) { var value = Peek(bits); At += bits; return value; }

    /// <summary>This many octets from here, at whatever alignment here happens to be.</summary>
    public byte[] Octets(int count, Wiring how)
    {
        if (!Holds(count * 8))
            throw how.Wrong($"wants {count} octet(s) and {Remaining / 8} remain");

        var taken = new byte[count];

        for (int i = 0; i < count; i++) taken[i] = (byte)Read(8);

        return taken;
    }

    /// <summary>What was read since a mark, for a node to hold as its own octets. The counterpart of the
    /// writer's, and the reason a self-delimiting form need not say what it consumed twice.</summary>
    public byte[] Since(int mark)
    {
        if (mark % 8 != 0 || At % 8 != 0) return [];

        var taken = new byte[(At - mark) / 8];

        for (int i = 0; i < taken.Length; i++) taken[i] = octets[(mark / 8) + i];

        return taken;
    }

    /// <summary>Everything from here on, without moving — for a form that has to look before it commits,
    /// and for a span that runs up to something it has to find first.</summary>
    public byte[] Ahead()
    {
        var rest = new byte[Remaining / 8];

        for (int i = 0; i < rest.Length; i++) rest[i] = (byte)Peek(8, skip: i * 8);

        return rest;
    }
}
