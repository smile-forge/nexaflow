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

    // ── Framing decided by what turned up ─────────────────────────────────────

    /// <summary>
    /// The same message, with the body framed the way the protocol really frames it.
    ///
    /// <para>
    /// Three ways to know where a body ends and <b>no discriminator anywhere on the wire</b>: it is counted
    /// if a length arrived, chunked if a transfer coding arrived, and otherwise runs to the end. Writing a
    /// key expression for that would be inventing a field that does not exist, so the packings say when
    /// each is an option instead.
    /// </para>
    ///
    /// <para>
    /// Guards were refused everywhere else in this engine, on the grounds that sibling conditions cannot be
    /// checked for cover or overlap. Conditions over <c>present.*</c> can: the parts are declared and
    /// finite, so every combination is tried at document time and exactly one packing must apply in each.
    /// Both here being present is malformed under RFC 9112 §6.1, and it falls out — neither packing is an
    /// option in that world, and the engine says so rather than picking whichever was written first.
    /// </para>
    /// </summary>
    private static MessageDef Framed() => Answer() with
    {
        Id = "framed",
        Fields = [.. Answer().Fields.Select(f => f.Id != "body" ? f : new Field
        {
            Id = "body",
            Value = Expr.Parse("inputs.body"),
            Pattern = new Pattern.Choice(null,
            [
                Arm.While("counted", Expr.Parse("present.contentLengthValue"),
                    [new Field
                    {
                        Id = "countedBody",
                        Pattern = Pattern.Opaque.Measured(
                            Expr.Parse("fields.contentLengthValue.value |> undecimal()")),
                        Value = Expr.Parse("inputs.body"),
                        Via = "unascii",
                    }]),

                Arm.While("toClose", Expr.Parse("!present.contentLengthValue"),
                    [new Field
                    {
                        Id = "restOfIt",
                        Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                        Value = Expr.Parse("inputs.body"),
                        Via = "unascii",
                    }]),
            ]),
        })],
    };

    [TestMethod]
    public void Framing_turns_on_which_headers_arrived_and_the_cover_is_proved()
    {
        var issues = new MessageCodec(Framed()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void A_length_that_arrived_counts_the_body()
    {
        var decoded = new MessageCodec(Framed()).Decode(Octets);

        Assert.AreEqual("counted", decoded["body"].AsText(), "the packing that applied");
        Assert.AreEqual("hello world!", decoded["countedBody"].AsText());
    }

    [TestMethod]
    public void No_length_and_no_coding_runs_to_the_end()
    {
        var without = Capture.Replace("Content-Length: 12\r\n", "");

        var decoded = new MessageCodec(Framed()).Decode([.. without.Select(c => (byte)c)]);

        Assert.AreEqual("toClose", decoded["body"].AsText());
        Assert.AreEqual("hello world!", decoded["restOfIt"].AsText());
    }

    [TestMethod]
    public void It_still_encodes_to_the_capture_when_the_framing_is_decided_this_way()
    {
        // The other direction asks the same question of the same declaration: a reader knows the length
        // arrived because it bound, a writer knows because the value listed it. That symmetry is the whole
        // reason presence is vocabulary rather than a state a header sets when it is seen.
        CollectionAssert.AreEqual(Octets, new MessageCodec(Framed()).Encode(Inputs()));
    }

    /// <summary>
    /// The framing rule written the way RFC 9112 §6.1 states it — a transfer coding overrides a length —
    /// and then only half implemented.
    /// </summary>
    private static MessageDef Uncovered()
    {
        var headers = (Pattern.Assorted)Headers().Pattern;

        return Framed() with
        {
            Id = "uncovered",
            Fields = [.. Framed().Fields.Select(f => f.Id switch
            {
                "headers" => new Field
                {
                    Id = "headers",
                    Value = Expr.Parse("inputs.headers"),
                    Pattern = headers with
                    {
                        Sorts = [.. headers.Sorts, Arm.On("chunked", ProtoValue.Of("Transfer-Encoding"),
                            HeaderLine("chunked", "'chunked'"))],
                    },
                },

                "body" => new Field
                {
                    Id = "body",
                    Value = Expr.Parse("inputs.body"),
                    Pattern = new Pattern.Choice(null,
                    [
                        Arm.While("counted",
                            Expr.Parse("present.contentLengthValue && !present.chunkedValue"),
                            [Line("countedBody", "inputs.body")]),

                        Arm.While("toClose",
                            Expr.Parse("!present.contentLengthValue && !present.chunkedValue"),
                            [Line("restOfIt", "inputs.body")]),
                    ]),
                },

                _ => f,
            })],
        };
    }

    [TestMethod]
    public void A_world_no_packing_covers_is_named_at_document_time()
    {
        // Saying the coding overrides the length and then not writing the packing for it is the ordinary
        // way this goes wrong, and it stays invisible until a peer sends one. Here it is a refusal naming
        // the combination — possible only because presence has a finite, declared domain, and exactly what
        // could never be said about a guard over an arbitrary expression.
        //
        // Note the limit, which is the right one: the engine enumerates the parts the conditions NAME. A
        // header nothing branches on is not a world, and could not be — HTTP has dozens, and demanding a
        // packing per combination of them would be absurd. A document says a part matters by mentioning it.
        var issues = new MessageCodec(Uncovered()).Validate();

        Assert.IsTrue(issues.Any(i => i.Contains("with chunkedValue and no contentLengthValue")
                                   && i.Contains("no packing is an option")),
            string.Join("\n", issues));

        Assert.IsTrue(issues.Any(i => i.Contains("with chunkedValue and contentLengthValue")),
            string.Join("\n", issues));
    }

    [TestMethod]
    public void A_packing_that_could_apply_twice_over_is_caught_at_document_time()
    {
        // The other half of the same proof, and the one that makes conditions admissible where guards were
        // not. These two overlap where neither part arrived, and nothing at run time would ever have said
        // so — whichever was written first would simply always have won.
        var overlapping = Framed() with
        {
            Id = "overlapping",
            Fields = [.. Framed().Fields.Select(f => f.Id != "body" ? f : new Field
            {
                Id = "body",
                Value = Expr.Parse("inputs.body"),
                Pattern = new Pattern.Choice(null,
                [
                    Arm.While("a", Expr.Parse("!present.serverValue"), [Line("aBody", "inputs.body")]),
                    Arm.While("b", Expr.Parse("!present.contentLengthValue"), [Line("bBody", "inputs.body")]),
                ]),
            })],
        };

        var issues = new MessageCodec(overlapping).Validate();

        Assert.IsTrue(issues.Any(i => i.Contains("2 packings are options at once")), string.Join("\n", issues));
        Assert.IsTrue(issues.Any(i => i.Contains("no contentLengthValue and no serverValue")),
            string.Join("\n", issues));
    }

    // ── What an absent part means ─────────────────────────────────────────────

    /// <summary>
    /// The same document, saying what an absent length means instead of leaving it a hole.
    ///
    /// <para>
    /// This is a sentence from a standard, not a convenience: a response with no length and no coding has
    /// no body. Once the document says so, everything downstream simply works — nothing needs to learn
    /// that a part is optional, because the value is there either way.
    /// </para>
    /// </summary>
    private static MessageDef Assuming()
    {
        var headers = (Pattern.Assorted)Answer().Fields.Single(f => f.Id == "headers").Pattern;

        var sorts = headers.Sorts.Select(s => s.Name != "contentLength" ? s
            : Arm.On("contentLength", ProtoValue.Of("Content-Length"),
            [
                Literal("contentLengthColon", ": "),
                new Field
                {
                    Id = "contentLengthValue",
                    Pattern = Pattern.Opaque.Before(LineEnd),
                    Value = Expr.Parse("fields.body.extent |> decimal()"),
                    Via = "unascii",
                    Assumed = new Default
                    {
                        Id = "noBody",
                        Value = ProtoValue.Of("0"),
                        Because = "A response that states no length and no transfer coding has no body.",
                    },
                },
                Literal("contentLengthEnd", "\r\n"),
            ])).ToList();

        return Answer() with
        {
            Id = "assuming",
            Fields = [.. Answer().Fields.Select(f => f.Id != "headers" ? f : new Field
            {
                Id = "headers",
                Value = Expr.Parse("inputs.headers"),
                Pattern = headers with { Sorts = sorts },
            })],
        };
    }

    [TestMethod]
    public void An_absent_part_means_what_the_document_says_it_means()
    {
        var without = Capture.Replace("Content-Length: 12\r\n", "").Replace("hello world!", "");

        // No error and no special case: the length is 0 because the document says an absent one is, and
        // the span that reads it never learns that anything was missing.
        var decoded = new MessageCodec(Assuming()).Decode([.. without.Select(c => (byte)c)]);

        Assert.AreEqual("", decoded["body"].AsText());
    }

    [TestMethod]
    public void An_assumed_value_is_never_written()
    {
        // The only pair of behaviours that is self-consistent: assumed on the way in, and written on the
        // way out by not writing it. A wire carrying the default explicitly would be a second encoding of
        // the same message.
        var octets = new MessageCodec(Assuming()).Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("version", ProtoValue.Of("HTTP/1.1")), ("code", ProtoValue.Of("204")),
            ("reason", ProtoValue.Of("No Content")), ("server", ProtoValue.Of("nginx")),
            ("body", ProtoValue.Of("")),
            ("headers", new ProtoValue.List([Component("server")])))));

        Assert.AreEqual("HTTP/1.1 204 No Content\r\nServer: nginx\r\n\r\n",
            new string([.. octets.Select(b => (char)b)]));
    }

    [TestMethod]
    public void The_default_is_in_the_graph_with_the_reason_for_it()
    {
        // A node, so something reviewing a document can ask what it assumes rather than reading every
        // field to find out — and can be told where the assumption comes from.
        var assumed = Assuming().Graph.Of<Assumes>().Single();

        Assert.AreEqual("contentLengthValue", assumed.From.Name);
        Assert.AreEqual("0", ((Default)assumed.To).Value.AsText());
        StringAssert.Contains(((Default)assumed.To).Because, "has no body");
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
