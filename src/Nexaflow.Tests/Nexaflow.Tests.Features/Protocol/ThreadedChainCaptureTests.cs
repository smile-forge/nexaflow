using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Structures that cannot be named without the ones before them.
///
/// <para>
/// The protocol is CoAP. Each option records <b>how far its number has moved since the previous option</b>
/// rather than what its number is, so the sixth option's identity is not recoverable from its own octets —
/// it is 0, +11, +0, +4, +0, +2, +235 accumulated. Nothing in a per-structure walk can produce that, which
/// is why a chain can thread a value along and each structure moves it on.
/// </para>
///
/// <para>
/// On the way out the same thread runs the other way: an option knows the number it wants, and the delta
/// it must write is that number minus whatever the options before it have already accumulated. That makes
/// each structure depend on the one before it — a genuine ordering, held by the worklist as a dependency
/// rather than assumed by a loop.
/// </para>
///
/// <para>
/// Two nibbles in one octet, each escaping to one or two more octets when it will not fit. They are
/// arithmetic on a plain octet rather than a bit group, because on the way out neither nibble is supplied:
/// both are computed, one from the accumulated number and one from a length that is itself derived.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class ThreadedChainCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);

    /// <summary>Below 13 the nibble is the value; 13 and 14 escape to one or two further octets, biased so
    /// that the short forms are never spelled the long way.</summary>
    private const string Nibble = "(n < 13 ? n : (n < 269 ? 13 : 14))";

    private const string DeltaNibble = "fields.optionHeader.value >> 4";
    private const string LengthNibble = "fields.optionHeader.value band 0x0f";

    /// <summary>How many octets the escape spends: none, one, or two.</summary>
    private static string Spilled(string nibble) => $"let n = {nibble} in n < 13 ? 0 : (n == 13 ? 1 : 2)";

    /// <summary>Recovering the value on the way in, from the nibble and whatever spilled past it.</summary>
    private static string Widened(string nibble, string spilt)
        => $"let n = {nibble} in n < 13 ? n : (n == 13 ? {spilt} + 13 : {spilt} + 269)";

    /// <summary>And composing it on the way out.</summary>
    private static string Spilling(string whole)
        => $"let d = {whole} in d < 13 ? 0 : (d < 269 ? d - 13 : d - 269)";

    private static readonly string ReadDelta = Widened(DeltaNibble, "fields.deltaExtension.value");
    private static readonly string ReadLength = Widened(LengthNibble, "fields.lengthExtension.value");

    /// <summary>
    /// Minimal-width unsigned octets where zero takes none at all — which is what makes the escape and its
    /// absence the same field rather than two.
    /// </summary>
    private static Conversion Spill => new("minuint", [ProtoValue.Of("empty")]);

    /// <summary>
    /// One option. Its number is <c>carried + delta</c>; its delta on the way out is
    /// <c>item.number - carried</c>.
    /// </summary>
    private static Field Option(out Field optionHeader)
    {
        optionHeader = new Field
        {
            Id = "optionHeader",
            Pattern = U8,
            Value = Expr.Parse(
                "let d = item.number - carried in "
              + "let v = fields.optionValue.extent in "
              + $"((let n = d in {Nibble}) * 16) + (let n = v in {Nibble})"),
        };

        return new Field
        {
            Id = "option",
            Pattern = new Pattern.Group(
        [
            // Both nibbles are derived, so the octet is composed rather than supplied. The delta comes
            // from where the chain has got to; the length from the extent of a value further down, which
            // has not been measured yet when this is written and does not need to have been.
            optionHeader,

            // Not a choice between packings. The first attempt made these two arms and a fallback, and it
            // could not be encoded: the expression that rebuilds the value names both escape widths, so
            // the dependency on the arm that was NOT taken pointed at a node nobody realised. The resolver
            // was right — the document was wrong. An escape is one run of octets whose width follows a
            // nibble, and zero is a perfectly good width for it.
            new Field
            {
                Id = "deltaExtension",
                Pattern = Pattern.Opaque.Measured(Expr.Parse(Spilled(DeltaNibble))),
                Value = Expr.Parse(Spilling("item.number - carried")),
                Via = Spill,
            },

            new Field
            {
                Id = "lengthExtension",
                Pattern = Pattern.Opaque.Measured(Expr.Parse(Spilled(LengthNibble))),
                Value = Expr.Parse(Spilling("fields.optionValue.extent")),
                Via = Spill,
            },

            new Field
            {
                Id = "optionValue",
                Pattern = Pattern.Opaque.Measured(Expr.Parse(ReadLength)),
                Value = Expr.Parse("item.value"),
            },
            ]),
        };
    }

    /// <summary>
    /// The message. Note what ends the option chain: not a count, and not the region — a marker octet the
    /// continuation has to <b>look at without consuming</b>.
    /// </summary>
    private static MessageDef Definition()
    {
        var option = Option(out var optionHeader);
        var options = new Field
        {
            Id = "options",
            Value = Expr.Parse("inputs.options"),
            Pattern = new Pattern.Chain(option,
                Continues: Expr.Parse("room > 0 && peek != 0xff"),
                Seed: Expr.Parse("0"),
                Carry: Expr.Parse($"carried + ({ReadDelta})")),
        };

        var header = new Field
        {
            Id = "header",
            Pattern = new Pattern.Bits([new("version", 2), new("kind", 2), new("tokenLength", 4)]),
            Value = Expr.Parse("inputs.header"),
        };

        return new MessageDef
        {
            Id = "datagram",
            Context = Context.Given.These("header", "code", "messageId", "token", "options", "payload"),
            Fields =
            [
                header,
            new Field
            {
                Id = "code",
                Pattern = new Pattern.Bits([new("codeClass", 3), new("codeDetail", 5)]),
                Value = Expr.Parse("inputs.code"),
            },
            new Field { Id = "messageId", Pattern = U16, Value = Expr.Parse("inputs.messageId") },
            new Field
            {
                Id = "token",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.header.value.tokenLength")),
                Value = Expr.Parse("inputs.token"),
            },

            options,

            // At most one, and the direction-asymmetry earns its keep again: on the way out the caller
            // either supplied a payload or did not, and on the way in there either are octets left or
            // there are not.
            new Field
            {
                Id = "payload",
                Value = Expr.Parse("inputs.payload"),
                Pattern = new Pattern.Chain(
                    new Field
                    {
                        Id = "carriedPayload",
                        Pattern = new Pattern.Group(
                        [
                            new Field { Id = "marker", Pattern = U8, Value = Expr.Parse("0xff") },
                            new Field
                            {
                                Id = "body",
                                Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                                Value = Expr.Parse("item"),
                            },
                        ]),
                    },
                    Expr.Parse("ordinal < 1 && room > 0")),
            },
            ],

            Rules =
            [
                new Rule.Domain
                {
                    Field = header,
                    Run = "version",
                    Allowed = [ValueRange.Exactly(1)],
                    Because = "only one version of this framing has ever been defined, and a datagram "
                            + "announcing another is not one this document can claim to have read.",
                },

                // The rule that started the whole refactor. Its subject is a segment inside a repeated
                // structure, so while rules named their subject there was nowhere to attach it: the name
                // resolved at message scope and the option header is not there.
                new Rule.Invariant
                {
                    Within = optionHeader,
                    Must = Expr.Parse($"({DeltaNibble}) != 15 && ({LengthNibble}) != 15"),
                    Because = "15 is reserved in both nibbles — the octet that would carry it is the "
                            + "marker that ends the list, so an option claiming it is not an option.",
                },

                // Found by writing the test for the rule above: a number too large for the escape to
                // express produced a nibble claiming two octets over an extension of three, and encoded
                // without complaint. The nibble and the octets are two statements of one width, and
                // nothing had been checking they agreed.
                new Rule.Invariant
                {
                    Within = option,
                    Must = Expr.Parse($"fields.deltaExtension.extent == ({Spilled(DeltaNibble)}) "
                                    + $"&& fields.lengthExtension.extent == ({Spilled(LengthNibble)})"),
                    Because = "the nibble says how many octets follow it, so an extension of a different "
                            + "width is a header describing a structure other than the one written.",
                },

                // A packing exclusion: about how two structures sit relative to each other, not about
                // anything inside either. It needs the pair, which no rule about one node can see.
                new Rule.Arrangement
                {
                    Chain = options,
                    Must = Expr.Parse("item.optionHeader >= 0"),
                    Because = "structures are written in ascending order of the number they name, which is "
                            + "what makes a delta from the previous one meaningful at all.",
                },
            ],
        };
    }

    private static byte[] Capture(int index) => ProtocolCorpus.Get("coap").Captures[index].Bytes;

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Definition()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void An_options_number_is_recovered_from_every_option_before_it()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(0));

        Assert.AreEqual(1, decoded["version"].AsInt());
        Assert.AreEqual(0x1234, decoded["messageId"].AsInt());
        Assert.AreEqual(4, decoded["token"].AsBytes().Length);

        var options = decoded["options"].AsList();
        Assert.AreEqual(6, options.Count, "the chain ends where the octets do, with no count anywhere");

        // 0 +11 +0 +4 +0 +2 +235. The last option's identity is not in its own octets.
        var numbers = Accumulated(options);
        CollectionAssert.AreEqual(new long[] { 11, 11, 15, 15, 17, 252 }, numbers);

        var values = options.Select(o => ((ProtoValue.Rec)o).Members["optionValue"].AsBytes()).ToList();
        Assert.AreEqual("sensors", System.Text.Encoding.ASCII.GetString(values[0]));
        Assert.AreEqual("temperature", System.Text.Encoding.ASCII.GetString(values[1]));
        Assert.AreEqual(26, values[3].Length, "a length that needed the one-octet escape");
        Assert.AreEqual(8, values[5].Length);
    }

    [TestMethod]
    public void A_two_octet_escape_is_read_the_same_way_as_a_one_octet_one()
    {
        // The third capture exists for the cases the first cannot reach: no token at all, and a delta
        // large enough to need the wider escape.
        var decoded = new MessageCodec(Definition()).Decode(Capture(2));

        Assert.AreEqual(0, decoded["tokenLength"].AsInt());
        Assert.AreEqual(0, decoded["token"].AsBytes().Length, "no token octets follow at all");

        var options = decoded["options"].AsList();
        Assert.AreEqual(2, options.Count);
        CollectionAssert.AreEqual(new long[] { 11, 2049 }, Accumulated(options));
    }

    [TestMethod]
    public void A_payload_is_read_when_there_is_one_and_not_invented_when_there_is_not()
    {
        var withPayload = new MessageCodec(Definition()).Decode(Capture(1));

        Assert.AreEqual(3, withPayload["options"].AsList().Count);

        var carried = withPayload["payload"].AsList();
        Assert.AreEqual(1, carried.Count);

        var body = ((ProtoValue.Rec)carried[0]).Members["body"].AsBytes();
        Assert.AreEqual("{\"temp\":21.5,\"unit\":\"C\"}", System.Text.Encoding.ASCII.GetString(body));

        // And a request carries none — the marker is absent rather than present and empty.
        Assert.AreEqual(0, new MessageCodec(Definition()).Decode(Capture(0))["payload"].AsList().Count);
    }

    [TestMethod]
    public void All_three_captures_re_encode_to_the_exact_original_octets()
    {
        // THE test, and the one the threaded value exists for: on the way out every delta is the option's
        // number minus what the options before it accumulated, so each structure depends on the one before
        // it. Nothing supplies a delta, a nibble, or any of the four possible extension octets.
        foreach (int index in (int[])[0, 1, 2])
        {
            var original = Capture(index);
            var codec = new MessageCodec(Definition());
            var reEncoded = codec.Encode(InputsFrom(codec.Decode(original)));

            Assert.AreEqual(original.Length, reEncoded.Length, $"capture {index}: length");
            CollectionAssert.AreEqual(original, reEncoded,
                $"capture {index} did not round-trip.\nexpected {Convert.ToHexString(original).ToLowerInvariant()}"
              + $"\nactual   {Convert.ToHexString(reEncoded).ToLowerInvariant()}");
        }
    }

    [TestMethod]
    public void Moving_one_option_number_rewrites_the_deltas_after_it()
    {
        // Proof the deltas are threaded rather than carried through: raising the second option's number
        // by one lowers the next delta by one, and nothing in the inputs mentions either.
        var codec = new MessageCodec(Definition());
        var inputs = InputsFrom(codec.Decode(Capture(2)));

        var options = ((ProtoValue.List)Member(inputs.Root("inputs"), "options")).Items;
        var first = With((ProtoValue.Rec)options[0], ("number", ProtoValue.Of(12L)));

        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("options", new ProtoValue.List([first, options[1]]))));

        var decoded = codec.Decode(codec.Encode(inputs));

        CollectionAssert.AreEqual(new long[] { 12, 2049 }, Accumulated(decoded["options"].AsList()),
            "the second option keeps the number it asked for, by writing a smaller delta");
    }

    [TestMethod]
    public void A_rule_about_a_segment_inside_the_repeated_structure_reaches_every_one_of_them()
    {
        // The case that could not be attached to anything, and the reason identity stopped being a name.
        // 15 is reserved in both nibbles; the option header is a segment inside the chain element, so
        // while a rule named its subject there was no scope in which that name meant anything.
        var codec = new MessageCodec(Definition());

        foreach (var (offset, nibble) in ((int, string)[])[(8, "delta"), (16, "length")])
        {
            var tampered = (byte[])[.. Capture(0)];
            tampered[offset] = offset == 8 ? (byte)0xf7 : (byte)0x0f;

            var ex = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Decode(tampered));
            StringAssert.Contains(ex.Message, "!= 15", nibble);
            StringAssert.Contains(ex.Message, "15 is reserved in both nibbles", nibble);
        }

        // The honest captures still read, so the rule is not simply refusing everything.
        Assert.AreEqual(6, codec.Decode(Capture(0))["options"].AsList().Count);
    }

    [TestMethod]
    public void A_rule_about_a_structure_is_checked_on_the_way_out_as_well()
    {
        // The same rule, the other direction. Asking for option number 1908 from a running total of 11
        // needs a delta of 1897, which is 14 in the nibble — legal — but asking for a value whose length
        // needs the reserved nibble is not, and nothing about the octets exists yet to notice it.
        var codec = new MessageCodec(Definition());
        var inputs = InputsFrom(codec.Decode(Capture(2)));

        var options = ((ProtoValue.List)Member(inputs.Root("inputs"), "options")).Items;

        // Ask for a number the escape cannot express: past 269 + 65535 the extension needs three octets
        // while the nibble can only say two. Before the rule existed this encoded silently into a header
        // describing a structure other than the one written — a real path found by trying to test the
        // path I meant to test.
        var beyond = With((ProtoValue.Rec)options[1], ("number", ProtoValue.Of(11L + 269L + 65536L)));

        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("options", new ProtoValue.List([options[0], beyond]))));

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Encode(inputs));
        StringAssert.Contains(ex.Message, "in structure 1");
        StringAssert.Contains(ex.Message, "a header describing a structure other than the one written");

        // The honest captures are untouched by it.
        foreach (int index in (int[])[0, 1, 2])
            CollectionAssert.AreEqual(Capture(index),
                codec.Encode(InputsFrom(codec.Decode(Capture(index)))), $"capture {index}");
    }

    [TestMethod]
    public void Which_rule_is_reached_first_is_the_documents_decision()
    {
        // Two rules on one node. The first failure is the one anyone reads, and "this is not a version we
        // know" is why a packet is wrong where "it is internally inconsistent" is merely also true. Order
        // rides on the edge because one rule can constrain several nodes and need not come in the same
        // place at each of them.
        var codec = new MessageCodec(Definition());
        var ordered = Definition().Graph.Of<Constrains>().OrderBy(e => e.Order).ToList();

        Assert.AreEqual(4, ordered.Count, "four rules, four edges");
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, ordered.Select(e => e.Order).ToArray(),
            "declaration order unless the document says otherwise");

        // And the edges are what the codec reads: the version rule is written first, so a datagram that
        // breaks both it and the reserved-nibble rule reports the version.
        var tampered = (byte[])[.. Capture(0)];
        tampered[0] = 0x84;      // version 2, and the token length nibble left intact
        tampered[8] = 0xf7;      // …plus a reserved delta nibble further in

        StringAssert.Contains(Assert.ThrowsExactly<ProtoTypeException>(() => codec.Decode(tampered)).Message,
            "only one version of this framing");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The option numbers, rebuilt from the deltas the way a reader has to.</summary>
    private static long[] Accumulated(IReadOnlyList<ProtoValue> options)
    {
        List<long> numbers = [];
        long running = 0;

        foreach (var option in options.Cast<ProtoValue.Rec>())
        {
            long header = option.Members["optionHeader"].AsInt();
            long nibble = header >> 4;

            long spilt = option.Members["deltaExtension"].AsInt();

            running += nibble < 13 ? nibble : nibble == 13 ? spilt + 13 : spilt + 269;

            numbers.Add(running);
        }

        return [.. numbers];
    }

    /// <summary>
    /// Feeds the decode back in, with each option carrying the <i>number</i> it wants rather than the
    /// delta it was written with — which is the whole point: the caller says what it means, and the
    /// document works out what to put on the wire.
    /// </summary>
    private static EvalScope InputsFrom(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);
        foreach (var (name, value) in decoded.Captures) inputs[name] = value;

        var numbers = Accumulated(decoded["options"].AsList());

        inputs["options"] = new ProtoValue.List(
        [
            .. decoded["options"].AsList().Select((o, i) => EvalScope.Record(
                ("number", ProtoValue.Of(numbers[i])),
                ("value", ((ProtoValue.Rec)o).Members["optionValue"]))),
        ]);

        inputs["payload"] = new ProtoValue.List(
        [
            .. decoded["payload"].AsList().Select(p => ((ProtoValue.Rec)p).Members["body"]),
        ]);

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }

    private static ProtoValue Member(ProtoValue record, string name)
        => record is ProtoValue.Rec r && r.Members.TryGetValue(name, out var v) ? v : ProtoValue.Nothing;

    private static ProtoValue With(ProtoValue record, params (string Name, ProtoValue Value)[] overrides)
    {
        var members = record is ProtoValue.Rec r
            ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
            : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

        foreach (var (name, value) in overrides) members[name] = value;
        return new ProtoValue.Rec(members);
    }
}
