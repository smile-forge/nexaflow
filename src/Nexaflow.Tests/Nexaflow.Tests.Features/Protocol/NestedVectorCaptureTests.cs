using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Length-prefixed vectors, four levels deep, from one document.
///
/// <para>
/// The protocol is TLS 1.2. Its building block is a length followed by however many homogeneous items fit
/// in it, with no tag and no count, and it nests: a record contains a handshake, which contains an
/// extension block, which contains extensions, one of which contains a name list. 98 of the request's 196
/// octets are inside that block.
/// </para>
///
/// <para>
/// The corpus listed eight blockers here and <b>none of them needed anything new</b>. Byte-budgeted
/// iteration is a chain inside a bounded region; a per-iteration capture scope is what a chain instance
/// already has; dispatch on a just-decoded tag is a choice; <c>remaining()</c> is <c>room</c>; a two-level
/// frame is two nested regions. That is the whole point of the last eleven steps, and it is the first
/// protocol where the answer was "this is already expressible" rather than "this needs one more notion".
/// </para>
///
/// <para>
/// Both captures come from one document, because a request and a response differ by which packing the
/// handshake type selects — 196 octets against 113, one declaration.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class NestedVectorCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);
    private static Pattern U24 => new Pattern.Scalar(3, BigEndian: true);

    /// <summary>
    /// A length, then a region it bounds. The universal shape here, and the one that makes "however many
    /// fit" decidable in both directions: the length measures the region on the way out and bounds it on
    /// the way in.
    /// </summary>
    private static Field[] Vector(string id, Pattern width, IReadOnlyList<Field> content) =>
    [
        new Field { Id = $"{id}Length", Pattern = width, Value = Expr.Parse($"fields.{id}.extent") },
        new Field { Id = id, Pattern = new Pattern.Group(content, Expr.Parse($"fields.{id}Length.value")) },
    ];

    /// <summary>Items until the region runs out. No count exists anywhere in the protocol.</summary>
    private static Field Filling(string id, Field element, string source) => new()
    {
        Id = id,
        Value = Expr.Parse(source),
        Pattern = new Pattern.Chain(element, Expr.Parse("room > 0")),
    };

    private static Field Number(string id, string source) => new()
    {
        Id = id,
        Pattern = U16,
        Value = Expr.Parse(source),
    };

    private static Field Blob(string id, string source) => new()
    {
        Id = id,
        Pattern = Pattern.Opaque.Measured(Expr.Parse($"fields.{id}Length.value")),
        Value = Expr.Parse(source),
    };

    /// <summary>One extension: a type, a length, and that many octets. What is inside them depends on the
    /// type, which is interpretation — at this level an extension body is a run of octets, and the two
    /// captures between them carry bodies of 0, 1, 2, 5, 10, 16 and 22 octets.</summary>
    private static Field Extension() => new()
    {
        Id = "extension",
        Pattern = new Pattern.Group(
        [
            Number("extensionType", "item.extensionType"),
            new Field { Id = "extensionBodyLength", Pattern = U16, Value = Expr.Parse("fields.extensionBody.extent") },
            new Field
            {
                Id = "extensionBody",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.extensionBodyLength.value")),
                Value = Expr.Parse("item.extensionBody"),
            },
        ]),
    };

    private static MessageDef Definition() => new()
    {
        Id = "handshakeRecord",
        Context = Context.Given.These(
            "contentType", "recordVersion", "handshakeType", "extensions", "unreadBody",
            "clientVersion", "clientRandom", "clientSessionId", "cipherSuites", "compressionMethods",
            "serverVersion", "serverRandom", "serverSessionId", "chosenSuite", "chosenCompression"),

        Fields =
        [
            new Field { Id = "contentType", Pattern = U8, Value = Expr.Parse("inputs.contentType") },
            new Field { Id = "recordVersion", Pattern = U16, Value = Expr.Parse("inputs.recordVersion") },

            .. Vector("record", U16,
            [
                new Field { Id = "handshakeType", Pattern = U8, Value = Expr.Parse("inputs.handshakeType") },

                .. Vector("handshake", U24,
                [
                    new Field
                    {
                        Id = "body",

                        // One request and one response, told apart by an octet already read. The two
                        // packings are not variations on each other: one offers a list of suites and one
                        // names the single suite chosen, and a compression method that is a vector in the
                        // first is a bare octet in the second.
                        Pattern = new Pattern.Choice(Expr.Parse("fields.handshakeType.value"),
                        [
                            new Arm("offer", 1,
                            [
                                Number("clientVersion", "inputs.clientVersion"),
                                new Field { Id = "clientRandom", Pattern = new Pattern.Opaque(32), Value = Expr.Parse("inputs.clientRandom") },
                                new Field { Id = "clientSessionIdLength", Pattern = U8, Value = Expr.Parse("fields.clientSessionId.extent") },
                                Blob("clientSessionId", "inputs.clientSessionId"),

                                .. Vector("cipherSuites", U16,
                                    [Filling("suites", new Field { Id = "suite", Pattern = U16, Value = Expr.Parse("item") },
                                             "inputs.cipherSuites")]),

                                .. Vector("compression", U8,
                                    [Filling("methods", new Field { Id = "method", Pattern = U8, Value = Expr.Parse("item") },
                                             "inputs.compressionMethods")]),

                                .. Vector("offeredExtensions", U16,
                                    [Filling("extensions", Extension(), "inputs.extensions")]),
                            ]),

                            new Arm("accept", 2,
                            [
                                Number("serverVersion", "inputs.serverVersion"),
                                new Field { Id = "serverRandom", Pattern = new Pattern.Opaque(32), Value = Expr.Parse("inputs.serverRandom") },
                                new Field { Id = "serverSessionIdLength", Pattern = U8, Value = Expr.Parse("fields.serverSessionId.extent") },
                                Blob("serverSessionId", "inputs.serverSessionId"),

                                Number("chosenSuite", "inputs.chosenSuite"),
                                new Field { Id = "chosenCompression", Pattern = U8, Value = Expr.Parse("inputs.chosenCompression") },

                                .. Vector("acceptedExtensions", U16,
                                    [Filling("acceptedList", Extension(), "inputs.extensions")]),
                            ]),

                            // The exhaustiveness check demanded this and was right to: there are ten of
                            // these types and a document knowing two of them must say what it does with
                            // the rest. Demanding it also found a modelling error — the extension block
                            // had been a sibling of the choice, as though it were part of the handshake
                            // framing. It is part of each body, and an unrecognised body has none that
                            // anyone can find.
                            new Arm("other", null,
                            [
                                new Field
                                {
                                    Id = "unreadBody",
                                    Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                                    Value = Expr.Parse("inputs.unreadBody"),
                                },
                            ]),
                        ]),
                    },

                ]),
            ]),
        ],
    };

    private static byte[] Capture(int index) => ProtocolCorpus.Get("tls").Captures[index].Bytes;

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Definition()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void The_offer_decodes_through_four_levels_of_length_prefix()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(0));

        Assert.AreEqual(0x16, decoded["contentType"].AsInt());
        Assert.AreEqual(191, decoded["recordLength"].AsInt(), "the record, less its own five octets");
        Assert.AreEqual(187, decoded["handshakeLength"].AsInt(), "and the handshake, less its four");
        Assert.AreEqual("offer", decoded["body"].AsText());

        Assert.AreEqual(0x0303, decoded["clientVersion"].AsInt());
        Assert.AreEqual(32, decoded["clientRandom"].AsBytes().Length);
        Assert.AreEqual(32, decoded["clientSessionId"].AsBytes().Length);

        // Eight suites in sixteen octets, with the count stated nowhere.
        var suites = decoded["suites"].AsList().Select(s => s.AsInt()).ToArray();
        Assert.AreEqual(8, suites.Length);
        CollectionAssert.AreEqual(new long[] { 0xc02b, 0xc02f, 0xc02c, 0xc030, 0xcca9, 0xcca8, 0x009c, 0x002f }, suites);

        CollectionAssert.AreEqual(new long[] { 0 }, decoded["methods"].AsList().Select(m => m.AsInt()).ToArray());

        var extensions = decoded["extensions"].AsList().Cast<ProtoValue.Rec>().ToList();
        Assert.AreEqual(98, decoded["offeredExtensionsLength"].AsInt(), "half the datagram");

        // The first is the server name, and its body is the 16 octets the name list occupies.
        Assert.AreEqual(0x0000, extensions[0].Members["extensionType"].AsInt());
        Assert.AreEqual(16, extensions[0].Members["extensionBody"].AsBytes().Length);
    }

    [TestMethod]
    public void The_acceptance_takes_the_other_packing_of_the_same_document()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(1));

        Assert.AreEqual("accept", decoded["body"].AsText());
        Assert.AreEqual(0xc02f, decoded["chosenSuite"].AsInt(), "one, chosen from the eight offered");
        Assert.AreEqual(0, decoded["chosenCompression"].AsInt(), "a bare octet where the offer had a vector");

        var extensions = decoded["acceptedList"].AsList().Cast<ProtoValue.Rec>().ToList();
        Assert.AreEqual(6, extensions.Count);

        // Three of the six carry nothing at all — an extension whose presence is the whole message.
        Assert.AreEqual(3, extensions.Count(e => e.Members["extensionBody"].AsBytes().Length == 0));
    }

    [TestMethod]
    public void Both_captures_re_encode_to_the_exact_original_octets()
    {
        // Nine length fields between the two, every one withheld. The record length depends on the
        // handshake length, which depends on which packing was chosen, which depends on a vector whose
        // own length depends on how many items it was given.
        foreach (int index in (int[])[0, 1])
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
    public void Dropping_a_suite_rewrites_every_length_above_it()
    {
        // Four levels, all derived. Removing two octets from the innermost vector takes two octets off its
        // own length, the handshake's and the record's, and nothing in the inputs mentions any of them.
        var codec = new MessageCodec(Definition());
        var inputs = InputsFrom(codec.Decode(Capture(0)));

        var suites = ((ProtoValue.List)Member(inputs.Root("inputs"), "cipherSuites")).Items;
        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("cipherSuites", new ProtoValue.List([.. suites.Take(suites.Count - 1)]))));

        var shorter = codec.Encode(inputs);
        var decoded = codec.Decode(shorter);

        Assert.AreEqual(Capture(0).Length - 2, shorter.Length);
        Assert.AreEqual(189, decoded["recordLength"].AsInt());
        Assert.AreEqual(185, decoded["handshakeLength"].AsInt());
        Assert.AreEqual(14, decoded["cipherSuitesLength"].AsInt());
        Assert.AreEqual(7, decoded["suites"].AsList().Count);
    }

    [TestMethod]
    public void A_length_that_disagrees_with_what_it_bounds_is_refused()
    {
        // The innermost region declaring more than its items fill. Under a count-driven reader this is
        // invisible; under a bounded region it is a disagreement between the declaration and the data.
        var tampered = (byte[])[.. Capture(0)];
        tampered[77] = 0x12;      // the cipher-suite vector's length, two octets too long

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Definition()).Decode(tampered));
        StringAssert.Contains(ex.Message, "cannot be an extent");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EvalScope InputsFrom(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        foreach (var (name, value) in decoded.Captures)
            if (!name.EndsWith("Length", StringComparison.Ordinal)) inputs[name] = value;

        inputs["cipherSuites"] = decoded["suites"];
        inputs["compressionMethods"] = decoded["methods"];

        inputs["extensions"] = new ProtoValue.List(
        [
            .. (decoded["extensions"].IsNull ? decoded["acceptedList"] : decoded["extensions"]).AsList().Cast<ProtoValue.Rec>().Select(e => EvalScope.Record(
                ("extensionType", e.Members["extensionType"]),
                ("extensionBody", e.Members["extensionBody"]))),
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
