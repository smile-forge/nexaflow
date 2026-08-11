using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Names that point at other names.
///
/// <para>
/// The protocol is mDNS. A name is a run of length-prefixed labels ending in a zero octet — or ending in a
/// two-octet <b>pointer</b> whose low fourteen bits are an absolute offset into the datagram, at which
/// more labels are found. The response carries four of them, and one points at an offset that lies inside
/// another record's payload rather than at anything the message declares.
/// </para>
///
/// <para>
/// The interesting discovery is what the wire layer does <b>not</b> need. Following a pointer is not
/// required to read the structure: a name is self-delimiting either way, and a record's payload is bounded
/// by its own length. Turning <c>nexaprint</c> plus a pointer to offset 12 into
/// <c>nexaprint._http._tcp.local</c> is interpretation — it says what the octets mean, not how they are
/// laid out — and it belongs on a node of its own rather than in the codec. So the seek-and-follow the
/// corpus predicted would be needed here is not, and the decoder stays a forward walk.
/// </para>
///
/// <para>
/// <b>The query round-trips; the response is read only.</b> Writing a compressed name means choosing what
/// to compress against and knowing where that landed, which needs a position the resolver does not
/// currently compute. A query carries no pointers, so it needs none of that — and asking and understanding
/// the answer is the whole of what discovery does.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class CompressedNameCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);
    private static Pattern U32 => new Pattern.Scalar(4, BigEndian: true);

    /// <summary>
    /// Labels until something that is not one, then the two-or-one octets that end it.
    ///
    /// <para>
    /// The ending is a span whose width the octets decide: two if the top two bits mark a pointer, one for
    /// the zero that means root. As octets it round-trips either way, and nothing in the layout depends on
    /// which it turned out to be.
    /// </para>
    /// </summary>
    private static Field Name(string id) => new()
    {
        Id = id,
        Pattern = new Pattern.Group(
        [
            new Field
            {
                Id = $"{id}Labels",
                Value = Expr.Parse($"item.{id}Labels"),
                Pattern = new Pattern.Chain(
                    new Field
                    {
                        Id = $"{id}Label",
                        Pattern = new Pattern.Group(
                        [
                            new Field
                            {
                                Id = $"{id}LabelLength",
                                Pattern = U8,
                                Value = Expr.Parse($"fields.{id}LabelText.extent"),
                            },
                            new Field
                            {
                                Id = $"{id}LabelText",
                                Pattern = Pattern.Opaque.Measured(Expr.Parse($"fields.{id}LabelLength.value")),
                                // The structure is composite, so what a chain hands back is everything it bound —
                                // which is what feeds straight back in on the way out.
                                Value = Expr.Parse($"item.{id}LabelText"),
                                Via = "unutf8",
                            },
                        ]),
                    },
                    // A label follows while the next octet is a length rather than a terminator or the
                    // top-bits marker of a pointer.
                    Expr.Parse("peek != 0 && (peek band 0xc0) == 0")),
            },
            new Field
            {
                Id = $"{id}Ending",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("(peek band 0xc0) == 0xc0 ? 2 : 1")),
                Value = Expr.Parse($"item.{id}Ending"),
            },
        ]),
    };

    private static Field Counted(string id, string count, Field element) => new()
    {
        Id = id,
        Value = Expr.Parse($"inputs.{id}"),
        Pattern = new Pattern.Chain(element, Expr.Parse($"ordinal < fields.{count}.value")),
    };

    private static Field Question() => new()
    {
        Id = "question",
        Pattern = new Pattern.Group(
        [
            Name("asked"),
            new Field { Id = "askedType", Pattern = U16, Value = Expr.Parse("item.askedType") },
            new Field { Id = "askedClass", Pattern = U16, Value = Expr.Parse("item.askedClass") },
        ]),
    };

    /// <summary>
    /// One record. Its payload is a length-bounded region whose shape depends on the type — which is a
    /// choice, and the reason the payload's own names are reachable at all rather than a run of octets.
    /// </summary>
    private static Field Record(string id) => new()
    {
        Id = id,
        Pattern = new Pattern.Group(
        [
            Name($"{id}Name"),
            new Field { Id = $"{id}Type", Pattern = U16, Value = Expr.Parse("item.type") },
            new Field { Id = $"{id}Class", Pattern = U16, Value = Expr.Parse("item.klass") },
            new Field { Id = $"{id}Ttl", Pattern = U32, Value = Expr.Parse("item.ttl") },
            new Field
            {
                Id = $"{id}Length",
                Pattern = U16,
                Value = Expr.Parse($"fields.{id}Payload.extent"),
            },
            new Field
            {
                Id = $"{id}Payload",
                Pattern = new Pattern.Group(
                [
                    new Field
                    {
                        Id = $"{id}Content",
                        Pattern = new Pattern.Choice(Expr.Parse($"fields.{id}Type.value"),
                        [
                            new Arm("name", 12, [Name($"{id}Pointed")]),

                            new Arm("service", 33,
                            [
                                new Field { Id = $"{id}Priority", Pattern = U16, Value = Expr.Parse("item.priority") },
                                new Field { Id = $"{id}Weight", Pattern = U16, Value = Expr.Parse("item.weight") },
                                new Field { Id = $"{id}Port", Pattern = U16, Value = Expr.Parse("item.port") },
                                Name($"{id}Host"),
                            ]),

                            new Arm("opaque", null,
                            [
                                new Field
                                {
                                    Id = $"{id}Raw",
                                    Pattern = Pattern.Opaque.Measured(Expr.Parse($"fields.{id}Length.value")),
                                    Value = Expr.Parse("item.raw"),
                                },
                            ]),
                        ]),
                    },
                ], Expr.Parse($"fields.{id}Length.value")),
            },
        ]),
    };

    private static MessageDef Definition() => new()
    {
        Id = "discovery",
        Context = Context.Given.These("identifier", "flags", "questions", "answers", "authority", "additional"),
        Fields =
        [
            new Field { Id = "identifier", Pattern = U16, Value = Expr.Parse("inputs.identifier") },
            new Field { Id = "flags", Pattern = U16, Value = Expr.Parse("inputs.flags") },
            new Field { Id = "questionCount", Pattern = U16, Value = Expr.Parse("fields.questions.extent > 0 ? (inputs.questions |> count()) : 0") },
            new Field { Id = "answerCount", Pattern = U16, Value = Expr.Parse("inputs.answers |> count()") },
            new Field { Id = "authorityCount", Pattern = U16, Value = Expr.Parse("inputs.authority |> count()") },
            new Field { Id = "additionalCount", Pattern = U16, Value = Expr.Parse("inputs.additional |> count()") },

            Counted("questions", "questionCount", Question()),
            Counted("answers", "answerCount", Record("answer")),
            Counted("authority", "authorityCount", Record("authority")),
            Counted("additional", "additionalCount", Record("additional")),
        ],
    };

    private static byte[] Capture(int index) => ProtocolCorpus.Get("mdns").Captures[index].Bytes;

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Definition()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void The_query_decodes_into_labels_and_a_root_ending()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(0));

        Assert.AreEqual(1, decoded["questionCount"].AsInt());
        Assert.AreEqual(0, decoded["answerCount"].AsInt());

        var question = (ProtoValue.Rec)decoded["questions"].AsList()[0];

        CollectionAssert.AreEqual(new[] { "_http", "_tcp", "local" },
            question.Members["askedLabels"].AsList().Select(Label).ToArray());

        Assert.AreEqual("00", question.Members["askedEnding"].ToString(), "one octet of root");
        Assert.AreEqual(12, question.Members["askedType"].AsInt());
        Assert.AreEqual(0x8001, question.Members["askedClass"].AsInt());
    }

    [TestMethod]
    public void The_query_re_encodes_to_the_exact_original_octets()
    {
        // Every label length is withheld, and so are the four counts: they come back from how many
        // structures there are, not from the header that announced them.
        var codec = new MessageCodec(Definition());
        var original = Capture(0);

        var reEncoded = codec.Encode(InputsFrom(codec.Decode(original)));

        CollectionAssert.AreEqual(original, reEncoded,
            $"expected {Convert.ToHexString(original).ToLowerInvariant()}"
          + $"\nactual   {Convert.ToHexString(reEncoded).ToLowerInvariant()}");
    }

    [TestMethod]
    public void The_response_reads_its_pointers_without_following_them()
    {
        // Four pointers, at offsets 12, 40, 23 and 70. The last two are the awkward ones: 23 lands in the
        // middle of a label run, and 70 lands inside another record's payload — neither is anything the
        // message declares as a whole. Nothing here needs to care, because reading the structure never
        // asks where a pointer goes.
        var decoded = new MessageCodec(Definition()).Decode(Capture(1));

        Assert.AreEqual(0, decoded["questionCount"].AsInt(), "a response carries no questions");
        Assert.AreEqual(1, decoded["answerCount"].AsInt());
        Assert.AreEqual(3, decoded["additionalCount"].AsInt());

        var answer = (ProtoValue.Rec)decoded["answers"].AsList()[0];
        Assert.AreEqual(12, answer.Members["answerType"].AsInt(), "a name record");
        Assert.AreEqual(4500, answer.Members["answerTtl"].AsInt());

        // Its payload is a name: one label, then a pointer at the rest.
        CollectionAssert.AreEqual(new[] { "nexaprint" },
            answer.Members["answerPointedLabels"].AsList().Select(Label).ToArray());
        Assert.AreEqual("c00c", answer.Members["answerPointedEnding"].ToString());

        var extra = decoded["additional"].AsList().Cast<ProtoValue.Rec>().ToList();

        // Each of the three begins with a name that is nothing but a pointer — no labels at all.
        CollectionAssert.AreEqual(new[] { "c028", "c028", "c046" },
            extra.Select(r => r.Members["additionalNameEnding"].ToString()).ToArray());

        foreach (var record in extra)
            Assert.AreEqual(0, record.Members["additionalNameLabels"].AsList().Count);

        // And the service record's own payload carries a further pointer, past three fixed values.
        var service = extra[0];
        Assert.AreEqual(33, service.Members["additionalType"].AsInt());
        Assert.AreEqual(80, service.Members["additionalPort"].AsInt());
        Assert.AreEqual("c017", service.Members["additionalHostEnding"].ToString(),
            "the one that points into the middle of a label run");
    }

    [TestMethod]
    public void Every_octet_of_the_response_is_accounted_for()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(1));

        var covered = decoded.Spans.Select(s => (s.Offset, s.Length)).Distinct()
                                   .OrderBy(s => s.Offset).ToList();
        int at = 0;
        foreach (var (offset, length) in covered)
        {
            Assert.AreEqual(at, offset, "spans must be contiguous with no gap");
            at += length;
        }

        Assert.AreEqual(127, at);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The text out of one label structure.</summary>
    private static string Label(ProtoValue label)
        => ((ProtoValue.Rec)label).Members.First(m => m.Key.EndsWith("LabelText", StringComparison.Ordinal)).Value.AsText();

    private static EvalScope InputsFrom(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        foreach (var (name, value) in decoded.Captures)
            if (!name.EndsWith("Count", StringComparison.Ordinal)) inputs[name] = value;

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }
}
