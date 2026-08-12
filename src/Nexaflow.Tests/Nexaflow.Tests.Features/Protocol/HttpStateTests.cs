using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.State;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// What an HTTP/1.1 connection is, said as a subject.
///
/// <para>
/// The subject here is <b>the connection</b>, and that is forced rather than chosen. BACnet's subject is a
/// transaction, told from its neighbours by an invoke id in the packet; HTTP has no such number anywhere.
/// A response is the answer to a request because it is the <i>next thing on the socket</i>, and nothing
/// else about it says so. So there is nothing for <see cref="Subject.Distinguishes"/> to name, it is null,
/// and the pairing lives entirely in the phase — a response is legal because a request is outstanding, and
/// a second request is not, because one already is. Ordering as correlation is not a degenerate case of
/// correlation by identifier; it is a different mechanism, and this is what it looks like when it is
/// written down.
/// </para>
///
/// <para>
/// Two things end a connection's usefulness and they end it differently. <c>Connection: close</c> says this
/// exchange is the last, so the phase it moves to has no request leaving it — and it does <b>not</b> end
/// the instance, because the socket is still there and only the host can see it go. A <c>101</c> says the
/// octets after this response are not HTTP at all, which is a stranger thing: the description stops
/// applying rather than the conversation stopping.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol state model — tree nodes land with the engine")]
public class HttpStateTests
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

    private static Field Token() =>
        new() { Id = "headerName", Pattern = Pattern.Opaque.Before(": "), Via = "unascii" };

    private static Expr MoreHeaders() => Expr.Parse("room > 0 && peek != 0x0d");

    // ── The two documents ─────────────────────────────────────────────────────

    private static MessageDef Ask() => new()
    {
        Id = "request",
        Context = Context.Given.These("method", "target", "version", "host", "headers"),
        Fields =
        [
            Upto("method", " ", "inputs.method"),
            Literal("afterMethod", " "),
            Upto("target", " ", "inputs.target"),
            Literal("afterTarget", " "),
            Line("version", "inputs.version"),
            Literal("afterVersion", "\r\n"),

            new Field
            {
                Id = "headers",
                Value = Expr.Parse("inputs.headers"),
                Pattern = new Pattern.Assorted(Token(),
                [
                    Arm.On("host", ProtoValue.Of("Host"), HeaderLine("host", "inputs.host")),
                    Arm.Otherwise("other", HeaderLine("other", "item.otherValue"), repeats: true),
                ], MoreHeaders()),
            },

            Literal("blankLine", "\r\n"),
        ],
    };

    /// <summary>
    /// The protocol a connection can switch to. Trivial on purpose — the point is that it knows nothing
    /// about HTTP and HTTP knows nothing about it.
    /// </summary>
    private static MessageDef Switched() => new()
    {
        Id = "switched",
        Context = Context.Given.These("tag", "text"),
        Fields =
        [
            new Field
            {
                Id = "tag",
                Pattern = new Pattern.Scalar(1, BigEndian: true),
                Value = Expr.Parse("inputs.tag"),
            },
            new Field
            {
                Id = "textLength",
                Pattern = new Pattern.Scalar(1, BigEndian: true),
                Value = Expr.Parse("fields.text.extent"),
            },
            new Field
            {
                Id = "text",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.textLength.value")),
                Value = Expr.Parse("inputs.text"),
                Via = "unascii",
            },
        ],
    };

    /// <summary>
    /// The response, with the one thing an upgrade can be said as today: a trailing part of the message
    /// that announced it, carrying another protocol, taken when the <c>Upgrade</c> header arrived.
    ///
    /// <para>
    /// A layer hangs off a <b>field</b>, so what is expressible is "the rest of this message is something
    /// else". That is a real reading of a <c>101</c> and it is not the one the standard states: RFC 9110
    /// §15.2.2 says the octets <i>after</i> the response belong to the new protocol, which is a fact about
    /// the connection rather than about this message. The two coincide only because nothing follows a
    /// <c>101</c> on that connection that is HTTP, so a reader that treats the remainder as a trailing
    /// field lands in the right place. See the test at the bottom for where the two come apart.
    /// </para>
    /// </summary>
    private static MessageDef Answer() => new()
    {
        Id = "response",
        Context = Context.Given.These(
            "version", "code", "reason", "connection", "upgrade", "body", "upgraded", "headers"),
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
                Pattern = new Pattern.Assorted(Token(),
                [
                    Arm.On("connection", ProtoValue.Of("Connection"),
                        HeaderLine("connection", "inputs.connection")),

                    Arm.On("upgrade", ProtoValue.Of("Upgrade"),
                        HeaderLine("upgrade", "inputs.upgrade")),

                    Arm.Otherwise("other", HeaderLine("other", "item.otherValue"), repeats: true),
                ], MoreHeaders()),
            },

            Literal("blankLine", "\r\n"),

            new Field
            {
                Id = "rest",
                Value = Expr.Parse("inputs.body"),
                Pattern = new Pattern.Choice(null,
                [
                    Arm.While("switched", Expr.Parse("present.upgradeValue"),
                        [new Field
                        {
                            Id = "switchedStream",
                            Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                            Value = Expr.Parse("inputs.upgraded"),
                            Carries = new Subprotocol
                            {
                                Id = "whatItSwitchedTo",
                                Carries = new Carriage.Described(Switched()),
                                About = "the protocol this connection stops being HTTP in favour of.",
                            },
                        }]),

                    Arm.While("plain", Expr.Parse("!present.upgradeValue"),
                        [new Field
                        {
                            Id = "payload",
                            Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                            Value = Expr.Parse("inputs.body"),
                            Via = "unascii",
                        }]),
                ]),
            },
        ],
    };

    // ── The subject ───────────────────────────────────────────────────────────

    private static readonly Party Us = new() { Id = "us", About = "the client running this document." };
    private static readonly Party Them = new() { Id = "them", About = "the server at the other end." };

    private static readonly Phase Idle = new()
    {
        Id = "idle", About = "the connection is open and nothing is outstanding on it.",
    };

    private static readonly Phase Awaiting = new()
    {
        Id = "awaiting", About = "a request has gone and its answer has not come back.",
    };

    private static readonly Phase Closing = new()
    {
        Id = "closing",
        About = "the last exchange has finished and no further request may be sent. The socket is still "
              + "open; only the host can see it go.",
    };

    private static readonly Phase Elsewhere = new()
    {
        Id = "elsewhere", About = "the connection is carrying something that is not HTTP.",
    };

    /// <summary>What arrived, read off the response. Absence answers too: a header nobody sent binds no
    /// capture, so a read of it is nothing and every comparison against it is false — which is the right
    /// answer here and is why none of these needs a presence test.</summary>
    private static Expr Says(string what) => Expr.Parse($"fields.connectionValue.value == '{what}'");

    private static Expr Ordinary() =>
        Expr.Parse("fields.code.value != '101' && fields.connectionValue.value != 'close'");

    private static Expr Upgrading() =>
        Expr.Parse("fields.code.value == '101' && fields.connectionValue.value != 'close'");

    private static (Subject Subject, MessageDef Request, MessageDef Response) Connection()
    {
        var request = Ask();
        var response = Answer();

        Transition Move(Party whose, Phase from, Phase to, MessageDef on, Expr when,
                        Bearing way, Confidence how, string why)
            => new()
            {
                Whose = whose, From = from, To = to, On = on, When = when,
                Way = way, Confidence = how, Because = why,
            };

        return (new Subject
        {
            Id = "httpConnection",
            About = "one HTTP/1.1 connection and what may happen on it next.",
            Start = Idle,
            Parties = [Us, Them],

            // Null, and not by omission. See the class comment: nothing in an HTTP message says which
            // exchange it belongs to, so there is no concept to point at here.
            Distinguishes = null,

            Transitions =
            [
                Move(Us, Idle, Awaiting, request, Expr.Parse("true"), Bearing.Sent, Confidence.Known,
                    "a request may be sent when nothing is outstanding on the connection."),
                Move(Them, Idle, Awaiting, request, Expr.Parse("true"), Bearing.Sent, Confidence.Presumed,
                    "the server is presumed to have the request; nothing has confirmed it."),

                Move(Us, Awaiting, Idle, response, Ordinary(), Bearing.Received, Confidence.Known,
                    "an ordinary answer completes the exchange and the connection is free again."),
                Move(Them, Awaiting, Idle, response, Ordinary(), Bearing.Received, Confidence.Known,
                    "the server wrote this answer, so what it believes is observed rather than guessed."),

                Move(Us, Awaiting, Closing, response, Says("close"), Bearing.Received, Confidence.Known,
                    "a close says this exchange was the last one this connection carries."),
                Move(Them, Awaiting, Closing, response, Says("close"), Bearing.Received, Confidence.Known,
                    "the server said so itself."),

                Move(Us, Awaiting, Elsewhere, response, Upgrading(), Bearing.Received, Confidence.Known,
                    "a 101 means what follows on this connection is a different protocol."),
                Move(Them, Awaiting, Elsewhere, response, Upgrading(), Bearing.Received, Confidence.Known,
                    "the server chose the protocol, so it is not guessing about the switch."),
            ],
        }, request, response);
    }

    // ── Captures ──────────────────────────────────────────────────────────────

    private static byte[] Octets(string text) => [.. text.Select(c => (byte)c)];

    private const string Request = "GET /index.html HTTP/1.1\r\nHost: example.org\r\n\r\n";

    private const string Ok = "HTTP/1.1 200 OK\r\nX-Trace: zz\r\n\r\n";

    private const string Closed = "HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n";

    private static readonly byte[] Switching =
        [.. Octets("HTTP/1.1 101 Switching Protocols\r\nUpgrade: chat\r\nConnection: Upgrade\r\n\r\n"),
         0x07, 0x02, (byte)'h', (byte)'i'];

    private static DecodeResult Decoded(MessageDef message, byte[] octets)
        => new MessageCodec(message).Decode(octets);

    // ── The shape of it ───────────────────────────────────────────────────────

    [TestMethod]
    public void Both_documents_validate()
    {
        foreach (var message in (MessageDef[])[Ask(), Answer(), Switched()])
        {
            var issues = new MessageCodec(message).Validate();
            Assert.AreEqual(0, issues.Count, $"{message.Id}:\n" + string.Join("\n", issues));
        }
    }

    [TestMethod]
    public void The_subject_validates()
    {
        var (subject, _, _) = Connection();
        Assert.AreEqual(0, subject.Validate().Count, string.Join("\n", subject.Validate()));
    }

    // ── Pairing, with nothing in the message to pair on ───────────────────────

    [TestMethod]
    public void A_response_is_the_answer_to_the_request_before_it_because_of_where_the_connection_stands()
    {
        // The whole of HTTP/1.1's correlation, and there is nothing in either message that says it. The
        // phase is the pairing: a response is legal because a request is outstanding, and a second request
        // is not legal because one already is.
        var (subject, request, response) = Connection();
        var live = new Standing(subject);

        Assert.IsNull(subject.Distinguishes, "nothing in an HTTP message identifies which exchange it is");

        live.Observe(request, Decoded(request, Octets(Request)), Bearing.Sent);
        Assert.AreEqual(Awaiting, live.PhaseOf(Us));

        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => live.Observe(request, Decoded(request, Octets(Request)), Bearing.Sent));

        StringAssert.Contains(ex.Message, "legal in itself and not here");
        StringAssert.Contains(ex.Message, "would have to be at 'idle' and is at 'awaiting'");

        live.Observe(response, Decoded(response, Octets(Ok)), Bearing.Received);
        Assert.AreEqual(Idle, live.PhaseOf(Us));

        // And now another one may go, on the same connection, with nothing distinguishing it from the first.
        live.Observe(request, Decoded(request, Octets(Request)), Bearing.Sent);
        Assert.AreEqual(Awaiting, live.PhaseOf(Us));

        // One instance throughout: there is no second connection and no second exchange to tell apart.
        CollectionAssert.AreEqual(new[] { Standing.Only }, live.Tracked.ToArray());
    }

    /// <summary>
    /// What happens when a document tries to pair the HTTP way that every other protocol pairs.
    ///
    /// <para>
    /// The interesting result is that this is refused at <b>document</b> time rather than surviving until a
    /// packet arrives. A concept is a lookup into the message, and one that resolves to nothing is a
    /// reference to an idea instead of to a part — so the subject is told, in the specification's own terms,
    /// that its messages carry no such thing. The runtime refusal below is the same sentence from the other
    /// side, and it is worth having both: a subject reaching for an identifier HTTP does not have is the
    /// single likeliest mistake in modelling it.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Pairing_by_something_in_the_message_is_refused_because_no_part_of_one_is_that()
    {
        var (plain, request, response) = Connection();

        var exchange = new Concept
        {
            Id = "theExchange",
            About = "which request an answer belongs to — which HTTP/1.1 does not write down anywhere.",
            Of = [],
        };

        var wishful = new Subject
        {
            Id = plain.Id, Start = plain.Start, Parties = plain.Parties,
            Transitions = plain.Transitions, Distinguishes = exchange,
        };

        Assert.IsTrue(wishful.Validate().Any(i => i.Contains("has no 'theExchange'")
                                               && i.Contains("nothing says which one of these it is about")),
            string.Join("\n", wishful.Validate()));

        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => new Standing(wishful).Observe(request, Decoded(request, Octets(Request)), Bearing.Sent));

        StringAssert.Contains(ex.Message, "'request' has no 'theExchange'");

        // Not a peculiarity of the request: the answer has nothing to correlate on either, which is the
        // point — there is no message of this protocol that could carry it.
        Assert.AreEqual(0, response.NamedAll(exchange).Count);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    [TestMethod]
    public void A_close_moves_the_connection_to_a_phase_no_request_leaves()
    {
        var (subject, request, response) = Connection();
        var live = new Standing(subject);

        live.Observe(request, Decoded(request, Octets(Request)), Bearing.Sent);
        live.Observe(response, Decoded(response, Octets(Closed)), Bearing.Received);

        Assert.AreEqual(Closing, live.PhaseOf(Us));

        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => live.Observe(request, Decoded(request, Octets(Request)), Bearing.Sent));

        StringAssert.Contains(ex.Message, "is at 'closing'");
        StringAssert.Contains(ex.Message, "a request may be sent when nothing is outstanding");
    }

    [TestMethod]
    public void Both_ends_know_a_connection_is_closing_and_only_one_end_was_guessing_before_that()
    {
        // The two-party model earning its keep on a protocol that is not connectionless. Sending a request
        // tells us nothing certain about the server; reading its answer tells us plenty, because the server
        // wrote it. So the same subject holds a guess on the way out and an observation on the way in.
        var (subject, request, response) = Connection();
        var live = new Standing(subject);

        var asked = live.Observe(request, Decoded(request, Octets(Request)), Bearing.Sent);

        Assert.AreEqual(Confidence.Known, asked.Moves.Single(m => m.Whose == Us).How);
        Assert.AreEqual(Confidence.Presumed, asked.Moves.Single(m => m.Whose == Them).How,
            "a lost request looks exactly like a slow one");

        var answered = live.Observe(response, Decoded(response, Octets(Closed)), Bearing.Received);

        Assert.AreEqual(Confidence.Known, answered.Moves.Single(m => m.Whose == Them).How,
            "the server authored the close, so its half of this is observed and not inferred");

        Assert.AreEqual(Closing, live.PhaseOf(Them));
    }

    [TestMethod]
    public void A_closing_connection_is_not_a_finished_one_and_only_the_host_can_say_it_is_gone()
    {
        // Why the close transition does not declare `Ends`. Ending an instance frees it, and a freed
        // instance is back at the start — which would say the connection is ready for another request, the
        // exact opposite of what the header meant. The socket outlives the last exchange by however long
        // the host takes to close it, and that is a lifetime the engine cannot see.
        var (subject, request, response) = Connection();
        var live = new Standing(subject);

        live.Observe(request, Decoded(request, Octets(Request)), Bearing.Sent);
        var progress = live.Observe(response, Decoded(response, Octets(Closed)), Bearing.Received);

        Assert.IsFalse(progress.Ended);
        Assert.AreEqual(Closing, live.PhaseOf(Us), "still refusing requests");

        // The host closes the socket. Now there is nothing, and the next connection starts where any
        // connection starts.
        live.Forget();
        Assert.AreEqual(Idle, live.PhaseOf(Us));
    }

    // ── Upgrade ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void A_101_moves_the_connection_out_of_the_protocol_that_described_it()
    {
        var (subject, request, response) = Connection();
        var live = new Standing(subject);

        live.Observe(request, Decoded(request, Octets(Request)), Bearing.Sent);
        live.Observe(response, Decoded(response, Switching), Bearing.Received);

        Assert.AreEqual(Elsewhere, live.PhaseOf(Us));
        Assert.AreEqual(Elsewhere, live.PhaseOf(Them));

        // And HTTP stops applying, which the subject says by having no move out of here for either
        // document. A request after a switch is not a protocol error at the wrong time — it is octets that
        // would be read by something else entirely.
        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => live.Observe(request, Decoded(request, Octets(Request)), Bearing.Sent));

        StringAssert.Contains(ex.Message, "is at 'elsewhere'");
    }

    /// <summary>
    /// What the seam can carry today, and where it stops.
    ///
    /// <para>
    /// The <c>101</c> below decodes into HTTP down to the blank line and into the switched protocol after
    /// it, and re-encodes to the same octets. That works because a <see cref="Subprotocol"/> hangs off a
    /// <b>field</b>, and there is a field here for it to hang off: the remainder of the response. Which is
    /// a reading of the standard rather than the standard — see the note on <see cref="Answer"/>. The seam
    /// is per-field, so "everything after this message on this connection" can only be said by making it
    /// part of this message, and a reader therefore has to hold the whole switched stream to finish
    /// decoding the response that announced it.
    /// </para>
    /// </summary>
    [TestMethod]
    public void The_protocol_switched_to_can_be_said_as_a_trailing_part_of_the_response_that_announced_it()
    {
        var response = Answer();
        var codec = new MessageCodec(response);

        var decoded = codec.Decode(Switching);

        Assert.AreEqual("101", decoded["code"].AsText());
        Assert.AreEqual("chat", decoded["upgradeValue"].AsText());
        Assert.AreEqual("switched", decoded["rest"].AsText(), "the packing that applied");

        var inner = (ProtoValue.Rec)decoded["switchedStream"];
        Assert.AreEqual(7, inner.Members["tag"].AsInt());
        Assert.AreEqual("hi", inner.Members["text"].AsText());

        // Both ways, from one declaration, and byte-exact across the seam.
        var again = codec.Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("version", decoded["version"]),
            ("code", decoded["code"]),
            ("reason", decoded["reason"]),
            ("connection", decoded["connectionValue"]),
            ("upgrade", decoded["upgradeValue"]),
            ("body", ProtoValue.Of("")),
            ("upgraded", EvalScope.Record(("tag", inner.Members["tag"]), ("text", inner.Members["text"]))),
            ("headers", decoded["headers"]))));

        CollectionAssert.AreEqual(Switching, again);
    }

    [TestMethod]
    public void A_response_that_announces_no_switch_reads_the_remainder_as_itself()
    {
        // The other side of the same choice, and the reason the arms are conditioned on presence rather
        // than on the status code: what decides is which headers turned up, and there is no field anywhere
        // holding a value that means "switched".
        var response = Answer();

        var decoded = new MessageCodec(response).Decode(Octets(Ok + "some body"));

        Assert.AreEqual("plain", decoded["rest"].AsText());
        Assert.AreEqual("some body", decoded["payload"].AsText());
    }
}
