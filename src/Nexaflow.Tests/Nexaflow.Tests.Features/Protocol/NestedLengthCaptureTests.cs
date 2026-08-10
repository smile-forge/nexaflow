using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Transforms;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The go/no-go gate: <b>two captures of very different sizes, from one document, byte-exact both ways.</b>
///
/// <para>
/// The protocol is SNMPv2c, chosen because its encoding is the hardest thing in the corpus and because the
/// two captures differ in exactly the way that breaks a length model. A 73-octet request and a 337-octet
/// response have the <i>same shape</i> at five levels of nesting, and their length fields are
/// (1,1,1,1,1) octets in one and (2,3,3,3,3) in the other. Every enclosing length counts the octets of the
/// lengths inside it, so a payload growing by one octet can grow the packet by two.
/// </para>
///
/// <para>
/// The corpus predicted this would need an iterated size-resolution pass run to a fixed point. It does not.
/// Spans in a nesting grammar form a tree, so the extents settle bottom-up in one demand-driven pass and a
/// genuine cycle would be reported as one. The prediction's own footnote — "resolve sizes bottom-up over
/// the field tree, which is legal because span dependencies in a nesting grammar form a tree, never a
/// cycle" — is what actually happens, and it falls out of the facet ordering rather than being arranged.
/// </para>
///
/// <para>
/// Nothing here is BER-specific in the engine. The length shape is a value carried inline below a
/// threshold and in a counted run of octets above it; the integers are minimal two's-complement, which the
/// converter table already had; and the hierarchical identifier is a <b>document transform</b>, the same
/// one whose round-trip law is proved in <see cref="TransformLanguageTests"/> — it is not a converter, and
/// that is the point.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class NestedLengthCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);

    /// <summary>
    /// A length below 128 rides in the marker octet; at or above, the marker counts the octets that carry
    /// it. Four is the ceiling here, so a length is at most five octets.
    /// </summary>
    private static Pattern Length => new Pattern.EscapedInline(InlineLimit: 0x80, MaxOctets: 4);

    /// <summary>A tag, a length, and a nested field list the length measures.</summary>
    private static Field Constructed(string id, Expr tag, params Field[] content) => new()
    {
        Id = id,
        Pattern = new Pattern.Group(
        [
            new Field { Id = $"{id}Tag", Pattern = U8, Value = tag },
            new Field { Id = $"{id}Len", Pattern = Length, Value = Expr.Parse($"fields.{id}Body.extent") },
            new Field
            {
                Id = $"{id}Body",
                Pattern = new Pattern.Group(content, Expr.Parse($"fields.{id}Len.value")),
            },
        ]),
    };

    /// <summary>
    /// A tag, a length, and one value the length measures.
    ///
    /// <para>
    /// The <i>value</i> takes the plain name and the wrapper is suffixed, not the other way round. Naming
    /// the wrapper is the obvious choice and the wrong one: everything that refers to this construct wants
    /// the number or the text, and a record of three members wearing the name it asked for is a type error
    /// several layers away from the mistake.
    /// </para>
    /// </summary>
    private static Field Primitive(string id, Expr tag, Expr value,
                                   string? via = null, Transform? through = null) => new()
    {
        Id = $"{id}Tlv",
        Pattern = new Pattern.Group(
        [
            new Field { Id = $"{id}Tag", Pattern = U8, Value = tag },
            new Field { Id = $"{id}Len", Pattern = Length, Value = Expr.Parse($"fields.{id}.extent") },
            new Field
            {
                Id = id,
                Pattern = Pattern.Opaque.Measured(Expr.Parse($"fields.{id}Len.value")),
                Value = value,
                Via = via,
                Through = through,
            },
        ]),
    };

    /// <summary>
    /// One structure of the list, and the reason the two captures can share a document: its value is
    /// whatever its tag says it is. The request carries three empty values; the response carries text, a
    /// number and text — same declaration, decided by the octet on the wire in one direction and by the
    /// structure being written in the other.
    /// </summary>
    private static Field Binding() => Constructed("binding", Expr.Parse("0x30"),
        // The hierarchical identifier is a DOCUMENT transform, not an engine converter. Its leading-pair
        // merge is one family's rule, and a converter for it would be a protocol specific wearing a
        // general name.
        Primitive("name", Expr.Parse("0x06"), Expr.Parse("item.name"),
                  through: TransformLanguageTests.ObjectIdentifier),

        new Field
        {
            Id = "carried",
            Pattern = new Pattern.Group(
            [
                new Field { Id = "carriedTag", Pattern = U8, Value = Expr.Parse("item.carriedTag") },
                new Field
                {
                    Id = "carriedLen",
                    Pattern = Length,
                    Value = Expr.Parse("fields.carriedBody.extent"),
                },
                new Field
                {
                    Id = "carriedBody",
                    Pattern = new Pattern.Group(
                    [
                        new Field
                        {
                            Id = "content",
                            Pattern = new Pattern.Choice(Expr.Parse("fields.carriedTag.value"),
                            [
                                // Nothing at all — a request asks by naming, and carries no value.
                                new Arm("empty", 0x05, []),

                                new Arm("text", 0x04,
                                [
                                    new Field
                                    {
                                        Id = "text",
                                        Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.carriedLen.value")),
                                        Value = Expr.Parse("item.text"),
                                        Via = "unascii",
                                    },
                                ]),

                                new Arm("number", 0x43,
                                [
                                    new Field
                                    {
                                        Id = "number",
                                        Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.carriedLen.value")),
                                        Value = Expr.Parse("item.number"),
                                        Via = "minint",
                                    },
                                ]),

                                // The tag space is open, so the arms cannot be proved exhaustive and a
                                // fallback is required rather than assumed.
                                new Arm("opaque", null,
                                [
                                    new Field
                                    {
                                        Id = "raw",
                                        Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.carriedLen.value")),
                                        Value = Expr.Parse("item.raw"),
                                    },
                                ]),
                            ]),
                        },
                    ], Expr.Parse("fields.carriedLen.value")),
                },
            ]),
        });

    /// <summary>
    /// The document. Five levels of nesting, every one of them length-prefixed, and the same text serves a
    /// request and a response four and a half times its size.
    /// </summary>
    private static MessageDef Message() => new()
    {
        Id = "nestedMessage",
        Fields =
        [
            Constructed("envelope", Expr.Parse("0x30"),
                Primitive("version", Expr.Parse("0x02"), Expr.Parse("inputs.version"), via: "minint"),
                Primitive("community", Expr.Parse("0x04"), Expr.Parse("inputs.community"), via: "unascii"),

                Constructed("operation", Expr.Parse("inputs.operationTag"),
                    Primitive("requestId", Expr.Parse("0x02"), Expr.Parse("inputs.requestId"), via: "minint"),
                    Primitive("errorStatus", Expr.Parse("0x02"), Expr.Parse("inputs.errorStatus"), via: "minint"),
                    Primitive("errorIndex", Expr.Parse("0x02"), Expr.Parse("inputs.errorIndex"), via: "minint"),

                    Constructed("bindings", Expr.Parse("0x30"),
                        new Field
                        {
                            Id = "boundList",
                            Value = Expr.Parse("inputs.boundList"),
                            Pattern = new Pattern.Chain(Binding(), Expr.Parse("room > 0")),
                        }))),
        ],
    };

    private static byte[] Capture(int index) => ProtocolCorpus.Get("snmp").Captures[index].Bytes;

    // ── The gate ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Message()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void Both_captures_re_encode_to_the_exact_original_octets()
    {
        // THE gate. Every one of the eleven-plus length fields is withheld from the inputs, so a length
        // that was echoed rather than measured fails here — and the two captures resolve the same
        // declaration to entirely different widths.
        foreach (int index in (int[])[0, 1])
        {
            var original = Capture(index);
            var codec = new MessageCodec(Message());
            var reEncoded = codec.Encode(InputsFrom(codec.Decode(original)));

            Assert.AreEqual(original.Length, reEncoded.Length, $"capture {index}: length");
            CollectionAssert.AreEqual(original, reEncoded,
                $"capture {index} did not round-trip.\nexpected {Convert.ToHexString(original).ToLowerInvariant()}"
              + $"\nactual   {Convert.ToHexString(reEncoded).ToLowerInvariant()}");
        }
    }

    [TestMethod]
    public void The_short_request_resolves_every_length_to_one_octet()
    {
        var decoded = new MessageCodec(Message()).Decode(Capture(0));

        Assert.AreEqual(1, decoded["version"].AsInt());
        Assert.AreEqual("public", decoded["community"].AsText());
        Assert.AreEqual(0xa0, decoded["operationTag"].AsInt());
        Assert.AreEqual(821915, decoded["requestId"].AsInt(), "three octets, not four — 0c 8a 9b");

        Assert.AreEqual(71, decoded["envelopeLen"].AsInt());
        Assert.AreEqual(58, decoded["operationLen"].AsInt());
        Assert.AreEqual(45, decoded["bindingsLen"].AsInt());

        var bound = decoded["boundList"].AsList();
        Assert.AreEqual(3, bound.Count, "no count field exists — the region ends the list");

        CollectionAssert.AreEqual(
            new[] { "1.3.6.1.2.1.1.1.0", "1.3.6.1.2.1.1.3.0", "1.3.6.1.4.1.2021.10.1.3.1" },
            bound.Select(b => ((ProtoValue.Rec)b).Members["name"].AsText()).ToArray());

        foreach (var structure in bound)
            Assert.AreEqual("empty", ((ProtoValue.Rec)structure).Members["content"].AsText(),
                "a request names what it wants and carries no value");
    }

    [TestMethod]
    public void The_long_response_resolves_the_same_fields_to_two_and_three_octet_lengths()
    {
        var decoded = new MessageCodec(Message()).Decode(Capture(1));

        // Same fields, same document, widths chosen by the values.
        Assert.AreEqual(333, decoded["envelopeLen"].AsInt(), "82 01 4d — three octets");
        Assert.AreEqual(318, decoded["operationLen"].AsInt());
        Assert.AreEqual(303, decoded["bindingsLen"].AsInt());
        Assert.AreEqual(821915, decoded["requestId"].AsInt(), "echoed byte-exactly from the request");

        var bound = decoded["boundList"].AsList().Cast<ProtoValue.Rec>().ToList();
        Assert.AreEqual(3, bound.Count);

        // Three different value shapes in one list, each chosen by its own tag.
        CollectionAssert.AreEqual(new[] { "text", "number", "text" },
            bound.Select(b => b.Members["content"].AsText()).ToArray());

        StringAssert.StartsWith(bound[0].Members["text"].AsText(), "Cisco IOS Software");
        Assert.AreEqual(247, bound[0].Members["text"].AsText().Length,
            "sized to force the one-count-octet form: 81 f7");
        Assert.AreEqual(9740722, bound[1].Members["number"].AsInt());
        Assert.AreEqual("0.31", bound[2].Members["text"].AsText());
    }

    [TestMethod]
    public void A_length_field_widens_because_of_the_lengths_inside_it()
    {
        // The feedback the corpus measured: growing the payload by one octet can grow the packet by two,
        // because a length crossed a width boundary and its extra octet is itself counted by every
        // enclosing length. Nothing iterates to discover this — the extents settle bottom-up.
        var codec = new MessageCodec(Message());
        var inputs = InputsFrom(codec.Decode(Capture(1)));

        int Sized(int payload)
        {
            var list = ((ProtoValue.List)Member(inputs.Root("inputs"), "boundList")).Items;
            var first = With((ProtoValue.Rec)list[0], ("text", ProtoValue.Of(new string('x', payload))));

            inputs.Set("inputs", With(inputs.Root("inputs"),
                ("boundList", new ProtoValue.List([first, .. list.Skip(1)]))));

            return codec.Encode(inputs).Length;
        }

        Assert.AreEqual(Sized(126) + 1, Sized(127), "still one length octet either side");
        Assert.AreEqual(Sized(127) + 2, Sized(128),
            "the value's length crosses into the escaped form, and the octet it gains is counted by the "
          + "four lengths enclosing it — so +1 of payload is +2 of packet");
        Assert.AreEqual(Sized(254) + 1, Sized(255), "both still inside the one-count-octet form");
        Assert.AreEqual(Sized(255) + 2, Sized(256),
            "and again where the count itself needs a second octet");
    }

    // ── What holds it together ────────────────────────────────────────────────

    [TestMethod]
    public void A_length_carried_in_more_octets_than_it_needs_is_refused()
    {
        // 30 81 47 … is legal in the wild and means the same as 30 47. Accepting it makes
        // encode(decode(b)) != b, because the encoder emits the short form. It is rejected rather than
        // remembered, for the same reason a padded continuation chain is.
        var padded = (byte[])[0x30, 0x81, 0x47, .. Capture(0)[2..]];

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Message()).Decode(padded));
        StringAssert.Contains(ex.Message, "shortest");
    }

    [TestMethod]
    public void A_region_that_claims_more_than_its_container_allows_is_an_error()
    {
        // Shrink the outermost length by one and the region below it now runs past its parent. Caught at
        // the boundary rather than by reading into the next message.
        var shrunk = (byte[])[.. Capture(0)];
        shrunk[1] = 0x46;

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Message()).Decode(shrunk));
        StringAssert.Contains(ex.Message, "past the");
    }

    [TestMethod]
    public void A_region_whose_contents_do_not_fill_it_is_an_error()
    {
        // The other half, on a message small enough to state plainly: a region declaring three octets and
        // holding two is not a spare octet, it is a disagreement — and the leftover would otherwise be
        // read as the next field of the container.
        var message = new MessageDef
        {
            Id = "underfilled",
            Fields =
            [
                new Field { Id = "len", Pattern = U8, Value = Expr.Parse("fields.body.extent") },
                new Field
                {
                    Id = "body",
                    Pattern = new Pattern.Group(
                    [
                        new Field { Id = "a", Pattern = U8, Value = Expr.Parse("inputs.a") },
                        new Field { Id = "b", Pattern = U8, Value = Expr.Parse("inputs.b") },
                    ], Expr.Parse("fields.len.value")),
                },
            ],
        };

        var codec = new MessageCodec(message);
        Assert.AreEqual(0, codec.Validate().Count, string.Join("\n", codec.Validate()));

        Assert.AreEqual(0xbb, codec.Decode([0x02, 0xaa, 0xbb])["b"].AsInt(), "the honest case still reads");

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Decode([0x03, 0xaa, 0xbb, 0xcc]));
        StringAssert.Contains(ex.Message, "consumed");
    }

    [TestMethod]
    public void The_hierarchical_identifier_is_a_document_transform_and_not_an_engine_converter()
    {
        // The debt that was discharged when the transform language landed, now carrying a real capture.
        // If this ever becomes a converter again, the engine has taken back one family's rule.
        Assert.IsNull(ConverterTable.Default.All
            .FirstOrDefault(c => c.Name is "oid" or "unoid"));

        var codec = new MessageCodec(Message());
        var decoded = codec.Decode(Capture(0));

        Assert.AreEqual("1.3.6.1.4.1.2021.10.1.3.1",
            ((ProtoValue.Rec)decoded["boundList"].AsList()[2]).Members["name"].AsText(),
            "including the arc that needs two octets — 8f 65 is 2021");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Every length in the document is derived, so none of them is fed back in.</summary>
    private static EvalScope InputsFrom(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        foreach (var (name, value) in decoded.Captures)
            if (!name.EndsWith("Len", StringComparison.Ordinal)) inputs[name] = value;

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
