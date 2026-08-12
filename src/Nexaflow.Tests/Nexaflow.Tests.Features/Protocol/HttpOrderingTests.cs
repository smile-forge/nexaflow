using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The same headers in a different order, and the same message either way.
///
/// <para>
/// HTTP/1.1 lets a sender arrange its headers however it likes, and two arrangements of the same set mean
/// the same thing. That is easy to say and has two halves that pull in opposite directions. A reader must
/// find <c>Content-Length</c> by <b>what it is</b> rather than by where it sat, or the body's extent
/// depends on the sender's whim. A writer must put the components back <b>where they came from</b>, or a
/// message that arrived one way goes out another and every signature, cache key and byte-for-byte
/// comparison over it is worthless.
/// </para>
///
/// <para>
/// Both fall out of the same declaration and neither needed a rule. Order lives in the value — a decode
/// produces the components in wire order and an encode writes them in the order it is handed — while
/// identity lives in the graph, because each kind is separately declared and the edge that sizes the body
/// points at a node. Nothing anywhere sorts, canonicalises or groups, and this is what proves it.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class HttpOrderingTests
{
    private static readonly byte[] LineEnd = [0x0d, 0x0a];

    private static Field Literal(string id, string octets) => new()
    {
        Id = id,
        Pattern = new Pattern.Opaque(octets.Length),
        Value = Expr.Parse($"'{octets}' |> unascii()"),
    };

    private static Field Upto(string id, string separator, string source) => new()
    {
        Id = id,
        Pattern = Pattern.Opaque.Before(separator),
        Value = Expr.Parse(source),
        Via = "unascii",
    };

    private static Field Line(string id, string source) => new()
    {
        Id = id,
        Pattern = Pattern.Opaque.Before(LineEnd),
        Value = Expr.Parse(source),
        Via = "unascii",
    };

    private static Field[] HeaderLine(string sort, string source) =>
    [
        Literal($"{sort}Colon", ": "),
        Line($"{sort}Value", source),
        Literal($"{sort}End", "\r\n"),
    ];

    // ── The document ──────────────────────────────────────────────────────────

    /// <summary>
    /// A response whose header block says nothing about arrangement, because there is nothing to say: the
    /// kinds are declared, the run is a run, and where each component sits is data.
    /// </summary>
    private static MessageDef Answer() => new()
    {
        Id = "answer",
        Context = Context.Given.These("version", "code", "reason", "server", "headers", "body"),
        Fields =
        [
            Upto("version", " ", "inputs.version"),
            Literal("afterVersion", " "),
            Upto("code", " ", "inputs.code"),
            Literal("afterCode", " "),
            Line("reason", "inputs.reason"),
            Literal("afterReason", "\r\n"),

            new Field
            {
                Id = "headers",
                Value = Expr.Parse("inputs.headers"),
                Pattern = new Pattern.Assorted(
                    new Field { Id = "headerName", Pattern = Pattern.Opaque.Before(": "), Via = "unascii" },
                    [
                        Arm.On("server", ProtoValue.Of("Server"),
                            HeaderLine("server", "inputs.server")),

                        Arm.On("contentLength", ProtoValue.Of("Content-Length"),
                            HeaderLine("contentLength", "fields.body.extent |> decimal()")),

                        Arm.On("setCookie", ProtoValue.Of("Set-Cookie"),
                            HeaderLine("setCookie", "item.setCookieValue"), repeats: true),

                        Arm.Otherwise("other",
                            HeaderLine("other", "item.otherValue"), repeats: true),
                    ],
                    Expr.Parse("room > 0 && peek != 0x0d")),
            },

            Literal("blankLine", "\r\n"),

            new Field
            {
                Id = "body",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.contentLengthValue.value |> undecimal()")),
                Value = Expr.Parse("inputs.body"),
                Via = "unascii",
            },
        ],
    };

    // ── The captures ──────────────────────────────────────────────────────────

    private const string Body = "hello world!";

    /// <summary>
    /// One set of headers, arranged three ways. The third is the one that matters most: the header the body
    /// is sized by comes <b>last</b>, so nothing about reading the message in order helps — the length is
    /// known only after every other header has been read, and the span it measures follows both.
    /// </summary>
    private static readonly string[][] Orderings =
    [
        ["Server: nginx", "Content-Length: 12", "X-Trace: zz"],
        ["Content-Length: 12", "X-Trace: zz", "Server: nginx"],
        ["X-Trace: zz", "Server: nginx", "Content-Length: 12"],
    ];

    private static byte[] Response(params string[] headers)
        => [.. ("HTTP/1.1 200 OK\r\n" + string.Concat(headers.Select(h => h + "\r\n")) + "\r\n" + Body)
                .Select(c => (byte)c)];

    private static string Text(byte[] octets) => new([.. octets.Select(b => (char)b)]);

    private static string[] Sorts(DecodeResult decoded)
        => [.. decoded["headers"].AsList().Select(c => ((ProtoValue.Rec)c).Members["sort"].AsText())];

    /// <summary>Hands a decode straight back to the encoder. What a decode produces is what an encode
    /// consumes, so nothing here rearranges, filters or re-derives anything.</summary>
    private static byte[] Again(MessageCodec codec, DecodeResult decoded, ProtoValue? headers = null)
        => codec.Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("version", decoded["version"]),
            ("code", decoded["code"]),
            ("reason", decoded["reason"]),
            ("server", decoded["serverValue"]),
            ("body", decoded["body"]),
            ("headers", headers ?? decoded["headers"]))));

    // ── Reading ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Answer()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void Every_ordering_binds_the_same_values_to_the_same_parts()
    {
        // Three wires, one interpretation. The length is found because it is a declared kind and not
        // because it is the second line, which is the whole reason the header block is an assortment
        // rather than a chain of look-alike lines.
        var codec = new MessageCodec(Answer());

        foreach (var ordering in Orderings)
        {
            var decoded = codec.Decode(Response(ordering));
            string which = string.Join(" | ", ordering);

            Assert.AreEqual("nginx", decoded["serverValue"].AsText(), which);
            Assert.AreEqual("12", decoded["contentLengthValue"].AsText(), which);
            Assert.AreEqual(Body, decoded["body"].AsText(), which);

            var unknown = decoded["headers"].AsList()
                .Cast<ProtoValue.Rec>().Single(c => c.Members["sort"].AsText() == "other");

            Assert.AreEqual("X-Trace", unknown.Members["headerName"].AsText(), which);
            Assert.AreEqual("zz", unknown.Members["otherValue"].AsText(), which);
        }
    }

    [TestMethod]
    public void The_arrangement_itself_is_part_of_what_was_read()
    {
        // The other half of the same claim, and the half a document that quietly sorted would fail. The
        // three decodes agree about every value and disagree about the sequence, which is what makes the
        // sequence recoverable rather than merely tolerated.
        var codec = new MessageCodec(Answer());

        var sequences = Orderings.Select(o => Sorts(codec.Decode(Response(o)))).ToList();

        CollectionAssert.AreEqual(new[] { "server", "contentLength", "other" }, sequences[0]);
        CollectionAssert.AreEqual(new[] { "contentLength", "other", "server" }, sequences[1]);
        CollectionAssert.AreEqual(new[] { "other", "server", "contentLength" }, sequences[2]);
    }

    // ── Writing ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Each_ordering_is_written_back_the_way_it_arrived_and_not_the_way_the_document_declares()
    {
        // A canonicalising codec passes the reading tests above and fails here, which is why byte-exactness
        // is the assertion and not "the same headers came back". Note that none of the three matches the
        // order the kinds are declared in for all of them, so nothing could be passing by coincidence.
        var codec = new MessageCodec(Answer());

        foreach (var ordering in Orderings)
        {
            var octets = Response(ordering);
            CollectionAssert.AreEqual(octets, Again(codec, codec.Decode(octets)), string.Join(" | ", ordering));
        }
    }

    [TestMethod]
    public void Rearranging_the_value_rearranges_the_wire_and_changes_nothing_else()
    {
        // Order is data, stated as directly as it can be: take one message apart, put its components back
        // in a different sequence, and what comes out is the octets a peer that had chosen that sequence
        // would have sent. Nothing in the document had to be told about the change.
        var codec = new MessageCodec(Answer());

        var decoded = codec.Decode(Response(Orderings[0]));
        var components = decoded["headers"].AsList();

        ProtoValue Named(string sort)
            => components.Single(c => ((ProtoValue.Rec)c).Members["sort"].AsText() == sort);

        var rearranged = new ProtoValue.List([Named("other"), Named("server"), Named("contentLength")]);

        CollectionAssert.AreEqual(Response(Orderings[2]), Again(codec, decoded, rearranged));
    }

    // ── A kind that comes more than once ──────────────────────────────────────

    /// <summary>
    /// Two <c>Set-Cookie</c> lines with something else between them, and the same two with nothing between.
    ///
    /// <para>
    /// The failure this guards is the tempting one: a plural kind arrives as a list, and a codec that holds
    /// the list somewhere of its own and writes it out when it reaches that kind would gather the two
    /// cookies together. Both messages below would then go out as the second, which is a different sequence
    /// of octets from the one that arrived and is exactly what a signature over the header block would
    /// notice.
    /// </para>
    /// </summary>
    [TestMethod]
    public void A_kind_that_repeats_keeps_its_place_among_the_others_rather_than_being_gathered_up()
    {
        var codec = new MessageCodec(Answer());

        var apart = Response("Set-Cookie: a=1", "Server: nginx", "Set-Cookie: b=2", "Content-Length: 12");
        var together = Response("Set-Cookie: a=1", "Set-Cookie: b=2", "Server: nginx", "Content-Length: 12");

        CollectionAssert.AreEqual(new[] { "setCookie", "server", "setCookie", "contentLength" },
            Sorts(codec.Decode(apart)));

        CollectionAssert.AreEqual(new[] { "setCookie", "setCookie", "server", "contentLength" },
            Sorts(codec.Decode(together)));

        CollectionAssert.AreEqual(apart, Again(codec, codec.Decode(apart)));
        CollectionAssert.AreEqual(together, Again(codec, codec.Decode(together)));

        // And the values keep wire order too, which is the one thing about a repeated header that is not
        // free arrangement: RFC 9110 §5.3 says a recipient may join them with commas, and that is only
        // sound if the order they came in survives.
        CollectionAssert.AreEqual(new[] { "a=1", "b=2" },
            codec.Decode(apart)["headers"].AsList().Cast<ProtoValue.Rec>()
                 .Where(c => c.Members["sort"].AsText() == "setCookie")
                 .Select(c => c.Members["setCookieValue"].AsText()).ToArray());
    }

    [TestMethod]
    public void An_arrangement_the_document_was_never_shown_still_round_trips()
    {
        // Nothing in the declaration enumerates arrangements, so this is not a fourth case so much as a
        // demonstration that there are no cases — a header the document has never heard of, twice, on
        // either side of the one the body is sized by.
        var codec = new MessageCodec(Answer());

        var octets = Response("X-Trace: zz", "Content-Length: 12", "X-Request-Id: 7", "Server: nginx");

        var decoded = codec.Decode(octets);

        CollectionAssert.AreEqual(new[] { "other", "contentLength", "other", "server" }, Sorts(decoded));
        Assert.AreEqual(Body, decoded["body"].AsText());

        CollectionAssert.AreEqual(octets, Again(codec, decoded));
        StringAssert.Contains(Text(Again(codec, decoded)), "X-Request-Id: 7");
    }
}
