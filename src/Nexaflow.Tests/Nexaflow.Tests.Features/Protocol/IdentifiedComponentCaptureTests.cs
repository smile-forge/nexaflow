using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Components that say what they are, and a field elsewhere that points at one of them.
///
/// <para>
/// The protocol is HTTP/1.1, and it is here for one reason the delimited-text corpus entry could not
/// reach. That entry decided header order and addressing were "questions about what the octets mean" —
/// the wire has an ordered list of lines either way — and for SSDP that was true. It is not true here:
/// the body's extent comes from the header called <c>Content-Length</c>, so <b>which line that is</b>
/// stops being a matter of interpretation and becomes the thing that decides where the message ends.
/// </para>
///
/// <para>
/// A chain cannot say it. A chain repeats one declared element, so <c>Content-Length</c> is the third
/// instance in this message and the first in the next, and an edge has nothing to point at — the identity
/// lives in the data. Declaring each kind separately puts it back in the graph, and the edge that sizes
/// the body becomes an ordinary read of an ordinary node.
/// </para>
///
/// <para>
/// What makes a kind pointable is <b>cardinality</b>, and it is declared. <c>Set-Cookie</c> may come any
/// number of times, so there is not one of it to point at; the engine refuses an edge into one and says
/// why. Its plurality lives in the data — a list going out, a list coming back — and not in a second
/// shape.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class IdentifiedComponentCaptureTests
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

    /// <summary>A span running to the end of its line.</summary>
    private static Field Line(string id, string source) => new()
    {
        Id = id,
        Pattern = Pattern.Opaque.Before(LineEnd),
        Value = Expr.Parse(source),
        Via = "unascii",
    };

    // ── The document ──────────────────────────────────────────────────────────

    /// <summary>
    /// One kind of header line: the colon after the name, the value, the newline that ends it.
    ///
    /// <para>
    /// The punctuation is declared <b>per kind</b> rather than once for the block. It is a few more nodes
    /// and no assumption: a shape that bracketed every kind the same way would be the engine asserting
    /// something true of this protocol today and false of the next line that folds, chunks, or counts
    /// instead of terminating. Saying it here is also where a reader looks for it — after this kind, a
    /// newline.
    /// </para>
    /// </summary>
    private static Field[] HeaderLine(string sort, string source) =>
    [
        Literal($"{sort}Colon", ": "),
        Line($"{sort}Value", source),
        Literal($"{sort}End", "\r\n"),
    ];

    /// <summary>
    /// The header block: a run of components, each announcing itself with the name before the colon.
    ///
    /// <para>
    /// Four kinds and three different places their values come from, which is the point of them being
    /// separate declarations. <c>Server</c> is written from what the caller supplied. <c>Content-Length</c>
    /// is written from another part of the message and nothing supplies it at all. <c>Set-Cookie</c> comes
    /// from the component itself, because there may be several and each is its own.
    /// </para>
    /// </summary>
    private static Field Headers() => new()
    {
        Id = "headers",
        Value = Expr.Parse("inputs.headers"),
        Pattern = new Pattern.Assorted(
            // No value of its own: what gets written here is the key of whichever kind was selected. A
            // document that also wrote it by hand would have said the same thing twice.
            new Field { Id = "headerName", Pattern = Pattern.Opaque.Before(": "), Via = "unascii" },
            [
                Arm.On("server", ProtoValue.Of("Server"),
                    HeaderLine("server", "inputs.server")),

                // The whole reason for the shape. This is a node, so the body can read it.
                Arm.On("contentLength", ProtoValue.Of("Content-Length"),
                    HeaderLine("contentLength", "fields.body.extent |> decimal()")),

                Arm.On("setCookie", ProtoValue.Of("Set-Cookie"),
                    HeaderLine("setCookie", "item.setCookieValue"), repeats: true),

                // Required, not optional: nothing enumerates every string, so the cover can only be closed
                // by a fallback — which is also what the protocol demands, since a header nobody knows has
                // to be carried through rather than dropped.
                Arm.Otherwise("other",
                    HeaderLine("other", "item.otherValue"), repeats: true),
            ],
            Expr.Parse("room > 0 && peek != 0x0d")),
    };

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

            Headers(),
            Literal("blankLine", "\r\n"),

            // The edge that could not be drawn before. On the way out the header is written from the
            // body's extent; on the way in the body is sized by the header. One relationship, read from
            // either end, with a node at each end of it.
            new Field
            {
                Id = "body",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.contentLengthValue.value |> undecimal()")),
                Value = Expr.Parse("inputs.body"),
                Via = "unascii",
            },
        ],
    };

    // ── The capture ───────────────────────────────────────────────────────────

    private const string Capture =
        "HTTP/1.1 200 OK\r\n"
      + "Server: nginx\r\n"
      + "Set-Cookie: a=1\r\n"
      + "Content-Length: 12\r\n"
      + "Set-Cookie: b=2\r\n"
      + "X-Trace: zz\r\n"
      + "\r\n"
      + "hello world!";

    private static byte[] Octets => [.. Capture.Select(c => (byte)c)];

    private static ProtoValue Component(string sort, params (string, ProtoValue)[] with)
        => EvalScope.Record([("sort", ProtoValue.Of(sort)), .. with]);

    /// <summary>
    /// What the caller supplies. Note what is <b>not</b> here: the content length. Nobody passes it, because
    /// it is not an input — it is a fact about another part of the message.
    /// </summary>
    private static EvalScope Inputs() => new EvalScope().Set("inputs", EvalScope.Record(
        ("version", ProtoValue.Of("HTTP/1.1")),
        ("code", ProtoValue.Of("200")),
        ("reason", ProtoValue.Of("OK")),
        ("server", ProtoValue.Of("nginx")),
        ("body", ProtoValue.Of("hello world!")),
        ("headers", new ProtoValue.List(
        [
            Component("server"),
            Component("setCookie", ("setCookieValue", ProtoValue.Of("a=1"))),
            Component("contentLength"),
            Component("setCookie", ("setCookieValue", ProtoValue.Of("b=2"))),
            Component("other", ("headerName", ProtoValue.Of("X-Trace")),
                               ("otherValue", ProtoValue.Of("zz"))),
        ]))));

    // ── Both directions ───────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Answer()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void It_encodes_to_the_capture()
    {
        // Byte-exact, and the interesting octets are "12": nothing supplied them. The header is written
        // from the body's extent, and it sits four lines ahead of the body it measures.
        CollectionAssert.AreEqual(Octets, new MessageCodec(Answer()).Encode(Inputs()));
    }

    [TestMethod]
    public void A_component_declared_once_is_a_node_the_body_can_be_sized_by()
    {
        var decoded = new MessageCodec(Answer()).Decode(Octets);

        Assert.AreEqual("hello world!", decoded["body"].AsText());

        // Not "the third header" and not "a header called Content-Length" — a field, read the same way any
        // other field is read.
        Assert.AreEqual("12", decoded["contentLengthValue"].AsText());
        Assert.AreEqual("nginx", decoded["serverValue"].AsText());
    }

    [TestMethod]
    public void A_component_that_may_repeat_arrives_as_a_list_and_not_as_a_second_shape()
    {
        var decoded = new MessageCodec(Answer()).Decode(Octets);

        var components = decoded["headers"].AsList();
        Assert.AreEqual(5, components.Count);

        CollectionAssert.AreEqual(
            new[] { "server", "setCookie", "contentLength", "setCookie", "other" },
            components.Select(c => ((ProtoValue.Rec)c).Members["sort"].AsText()).ToArray());

        // Plurality lives in the data. There is one `Set-Cookie` in the graph and two of them here.
        CollectionAssert.AreEqual(new[] { "a=1", "b=2" },
            components.Where(c => ((ProtoValue.Rec)c).Members["sort"].AsText() == "setCookie")
                      .Select(c => ((ProtoValue.Rec)c).Members["setCookieValue"].AsText()).ToArray());
    }

    [TestMethod]
    public void A_kind_the_document_never_heard_of_comes_back_as_it_arrived()
    {
        var decoded = new MessageCodec(Answer()).Decode(Octets);

        var unknown = (ProtoValue.Rec)decoded["headers"].AsList()[4];

        // The token has to travel with it: nothing in the declaration says what an unrecognised header is
        // called, so a component that was merely unknown would otherwise come back as a different one.
        Assert.AreEqual("X-Trace", unknown.Members["headerName"].AsText());
        Assert.AreEqual("zz", unknown.Members["otherValue"].AsText());
    }

    [TestMethod]
    public void The_whole_thing_round_trips_in_the_order_it_arrived()
    {
        var codec = new MessageCodec(Answer());
        var decoded = codec.Decode(Octets);

        // Handed straight back: what a decode produces is what an encode consumes. The order is preserved
        // even though the protocol would have accepted any other — a message that arrived one way is
        // written back the way it came rather than the way this document would have chosen.
        var again = codec.Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("version", decoded["version"]),
            ("code", decoded["code"]),
            ("reason", decoded["reason"]),
            ("server", decoded["serverValue"]),
            ("body", decoded["body"]),
            ("headers", decoded["headers"]))));

        CollectionAssert.AreEqual(Octets, again);
    }

    // ── What it refuses ───────────────────────────────────────────────────────

    [TestMethod]
    public void A_component_declared_once_arriving_twice_is_refused()
    {
        // Not pedantry. Something points at this node, so two of them means the edge resolves to whichever
        // came last — and two content lengths is the textbook way to show one body to a proxy and a
        // different one to what sits behind it.
        var twice = Capture.Replace("X-Trace: zz", "Content-Length: 3");

        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => new MessageCodec(Answer()).Decode([.. twice.Select(c => (byte)c)]));

        StringAssert.Contains(ex.Message, "'contentLength' arrived twice");
    }

    /// <summary>The document with the body sized by something it may not be sized by.</summary>
    private static MessageDef Reaching(string sizedBy) => Sized(Answer() with { Id = "reaching" }, sizedBy);

    private static MessageDef Sized(MessageDef message, string by) => message with
    {
        Fields = [.. message.Fields.Select(f => f.Id != "body" ? f : new Field
        {
            Id = "body",
            Pattern = Pattern.Opaque.Measured(Expr.Parse(by)),
            Value = Expr.Parse("inputs.body"),
            Via = "unascii",
        })],
    };

    [TestMethod]
    public void A_component_that_may_come_more_than_once_is_not_in_scope_to_be_pointed_at()
    {
        // The rule that makes any of this work, stated the other way round — and it needed no new check,
        // because a kind that repeats is scoped exactly as a chain's instances are. There is no one
        // `Set-Cookie`, so from out here the name means nothing at all rather than meaning whichever
        // appearance settled last.
        var issues = new MessageCodec(Reaching("fields.setCookieValue.extent")).Validate();

        Assert.IsTrue(issues.Any(i => i.Contains("references 'setCookieValue'") && i.Contains("not a field in scope")),
            string.Join("\n", issues));
    }

    [TestMethod]
    public void The_token_is_in_scope_and_still_may_not_be_pointed_at()
    {
        // The case scoping does not catch, and the reason the check exists. The token is one declaration
        // read once per component, so it is genuinely in scope — and from out here it names whichever
        // component happened to come last, which is nobody's intent and would resolve silently.
        var issues = new MessageCodec(Reaching("fields.headerName.extent")).Validate();

        Assert.IsTrue(issues.Any(i => i.Contains("'body' reads 'headerName'")
                                   && i.Contains("names the last one")),
            string.Join("\n", issues));
    }

    [TestMethod]
    public void Kinds_named_by_text_must_declare_what_happens_to_the_ones_nobody_knows()
    {
        // Nothing enumerates every string, so the engine cannot prove the kinds are exhaustive and says so
        // rather than letting an unrecognised header bind no fields and report nothing.
        var closed = Answer() with { Id = "closed" };

        var pattern = (Pattern.Assorted)closed.Fields.Single(f => f.Id == "headers").Pattern;

        closed = closed with
        {
            Fields = [.. closed.Fields.Select(f => f.Id != "headers" ? f : new Field
            {
                Id = "headers",
                Value = Expr.Parse("inputs.headers"),
                Pattern = pattern with { Sorts = [.. pattern.Sorts.Where(s => s.Name != "other")] },
            })],
        };

        var issues = new MessageCodec(closed).Validate();

        Assert.IsTrue(issues.Any(i => i.Contains("cannot compute which values this token can take")),
            string.Join("\n", issues));
    }

    // ── Absence ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A component that is simply not there costs the writer nothing.
    ///
    /// <para>
    /// Worth pinning, because optionality is usually where a wire format starts needing lookahead. Here it
    /// needs none in either direction: on the way out a component exists because the value lists it, and on
    /// the way in it announces itself, so there is never a point at which the walk has to guess whether
    /// what it is looking at is one thing absent or the next thing present.
    /// </para>
    /// </summary>
    [TestMethod]
    public void A_component_nobody_asked_for_is_simply_not_written()
    {
        var octets = new MessageCodec(Answer()).Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("version", ProtoValue.Of("HTTP/1.1")), ("code", ProtoValue.Of("200")),
            ("reason", ProtoValue.Of("OK")), ("server", ProtoValue.Of("nginx")),
            ("body", ProtoValue.Of("")),
            ("headers", new ProtoValue.List([Component("server")])))));

        Assert.AreEqual("HTTP/1.1 200 OK\r\nServer: nginx\r\n\r\n",
            new string([.. octets.Select(b => (char)b)]));
    }

    /// <summary>
    /// Reading one that is not there says so, by name.
    ///
    /// <para>
    /// The diagnostic is the whole test. Before the graph knew which fields belong to a component that may
    /// be absent, this came out of the converter as <c>expected Text, got Null</c> — true, and from three
    /// layers below anything an author could act on. What a reader needs to be told is that the header
    /// saying how long the body is never arrived.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Reading_a_component_that_did_not_arrive_names_it()
    {
        var without = Capture.Replace("Content-Length: 12\r\n", "").Replace("hello world!", "");

        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => new MessageCodec(Answer()).Decode([.. without.Select(c => (byte)c)]));

        StringAssert.Contains(ex.Message, "'body' reads 'contentLengthValue'");
        StringAssert.Contains(ex.Message, "'contentLength' component of 'headers'");
    }

    [TestMethod]
    public void A_text_key_against_a_token_that_reads_octets_is_refused()
    {
        // The quiet one, and the reason it is worth a check of its own: nothing here fails. The token reads
        // as octets, no text key equals octets, so every component takes the fallback and the message
        // decodes cleanly having understood none of itself.
        var raw = Answer() with { Id = "raw" };

        var pattern = (Pattern.Assorted)raw.Fields.Single(f => f.Id == "headers").Pattern;

        raw = raw with
        {
            Fields = [.. raw.Fields.Select(f => f.Id != "headers" ? f : new Field
            {
                Id = "headers",
                Value = Expr.Parse("inputs.headers"),
                Pattern = pattern with
                {
                    Token = new Field { Id = "headerName", Pattern = Pattern.Opaque.Before(": ") },
                },
            })],
        };

        Assert.IsTrue(new MessageCodec(raw).Validate()
            .Any(i => i.Contains("no key can ever match one")),
            string.Join("\n", new MessageCodec(raw).Validate()));
    }

    [TestMethod]
    public void A_token_the_document_also_writes_by_hand_is_refused()
    {
        // Two answers to which kind this is, and nothing says which wins — so a component could announce
        // one kind and be packed as another.
        var doubled = Answer() with { Id = "doubled" };

        var pattern = (Pattern.Assorted)doubled.Fields.Single(f => f.Id == "headers").Pattern;

        doubled = doubled with
        {
            Fields = [.. doubled.Fields.Select(f => f.Id != "headers" ? f : new Field
            {
                Id = "headers",
                Value = Expr.Parse("inputs.headers"),
                Pattern = pattern with
                {
                    Token = new Field
                    {
                        Id = "headerName",
                        Pattern = Pattern.Opaque.Before(": "),
                        Via = "unascii",
                        Value = Expr.Parse("'Server'"),
                    },
                },
            })],
        };

        Assert.IsTrue(new MessageCodec(doubled).Validate()
            .Any(i => i.Contains("has a value of its own")));
    }
}
