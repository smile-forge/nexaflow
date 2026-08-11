using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.State;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// What each party believes, and what a message does to it.
///
/// <para>
/// <b>There is no conversation object here.</b> A conversation is a reading applied afterwards to a
/// sequence of messages, not something the engine holds — what exists is the current belief and a message
/// that moves it. Two of the tests below are a request/response exchange and one is a light being switched
/// on, and the second is not a degenerate case of the first: they are the same mechanism, and only one of
/// them has anything to correlate.
/// </para>
///
/// <para>
/// BACnet/IP is connectionless and runs up to 255 confirmed transactions at once against the same device.
/// Its five captures are two of them interleaved: invoke 1 is a ReadProperty and the two ways it can end,
/// invoke 2 is a segmented acknowledgement and the segment ack for it. A single state for the run would
/// have them overwrite each other, and a state per connection is no use to a protocol with no connection —
/// so a subject with more than one instance is told apart by a <b>concept</b>, the invoke id, which is one
/// idea appearing as four different fields depending on which packing arrived.
/// </para>
///
/// <para>
/// And every view belongs to a <b>party</b>. Sending a request tells you what you now believe; what the
/// device believes is a guess until something comes back. Keeping those apart is what makes "the request
/// is outstanding" a different state from "the request arrived", which is the difference every retry and
/// timeout rule is actually about.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol state model — tree nodes land with the engine")]
public class StandingTests
{
    private static readonly Party Us = new() { Id = "us", About = "the machine running this document." };
    private static readonly Party Them = new() { Id = "them", About = "the device being talked to." };

    private static readonly Phase Idle = new() { Id = "idle", About = "no transaction with this id." };
    private static readonly Phase Asked = new() { Id = "asked", About = "a request is outstanding." };
    private static readonly Phase Answering = new() { Id = "answering", About = "a segmented reply is in flight." };
    private static readonly Phase Done = new() { Id = "done", About = "the transaction has an outcome." };

    /// <summary>Which packing arrived, by APDU type — one document describes all five.</summary>
    private static Expr Pdu(long type) => Expr.Parse($"fields.pduType.value == {type}");

    /// <summary>An acknowledgement, whole or in pieces. The type alone does not say: the segmented bit
    /// does, and leaving it out made both moves match at once — which the engine refused rather than
    /// picking whichever was written last.</summary>
    private static Expr Ack(bool segmented) => Expr.Parse(
        $"fields.pduType.value == 3 && (fields.pduFlags.value band 0x08) {(segmented ? "!=" : "==")} 0");

    private static (Subject Subject, MessageDef Message) Confirmed()
    {
        var message = TaggedUnionCaptureTests.Definition();

        // The invoke id is one idea with four spellings. Which field carries it depends on which packing
        // arrived, and the concept is what says they are the same thing.
        var byId = new Concept
        {
            Id = "theInvokeId",
            About = "which of the transactions in flight with this device a message belongs to.",
            Of = [.. new[] { "invokeId", "ackInvokeId", "ackedInvokeId", "errorInvokeId" }
                        .Select(id => message.AllFields.Single(f => f.Id == id))],
        };

        var known = message with { Concepts = [.. message.Concepts, byId] };

        Transition Move(Party whose, Phase from, Phase to, Expr when, Bearing way, Confidence how, string why)
            => new()
            {
                Whose = whose, From = from, To = to, On = known, When = when,
                Way = way, Confidence = how, Because = why,
            };

        return (new Subject
        {
            Id = "confirmedTransaction",
            About = "one confirmed request and whatever becomes of it.",
            Start = Idle,
            Parties = [Us, Them],
            Distinguishes = byId,

            Transitions =
            [
                // We asked. We know that; that they heard is a guess until something comes back, and the
                // whole point of saying so is that a lost request looks exactly like a slow one.
                Move(Us, Idle, Asked, Pdu(0), Bearing.Sent, Confidence.Known,
                    "a request may be sent when this invoke id is free."),
                Move(Them, Idle, Asked, Pdu(0), Bearing.Sent, Confidence.Presumed,
                    "the device is presumed to have the request; nothing has confirmed it."),

                // An answer, in one piece or in several.
                Move(Us, Asked, Done, Ack(segmented: false), Bearing.Received, Confidence.Known,
                    "an acknowledgement answers an outstanding request."),
                Move(Us, Asked, Done, Pdu(5), Bearing.Received, Confidence.Known,
                    "an error is an outcome too — the transaction is over either way."),

                // A segmented reply, and the ack that keeps it coming.
                Move(Us, Asked, Answering, Ack(segmented: true), Bearing.Received, Confidence.Known,
                    "a segment starts a reply that arrives in pieces."),
                Move(Us, Answering, Answering, Pdu(4), Bearing.Sent, Confidence.Known,
                    "acknowledging a segment asks for the next one."),
            ],
        }, known);
    }

    private static DecodeResult Capture(MessageDef message, int index)
        => new MessageCodec(message).Decode(ProtocolCorpus.Get("bacnet").Captures[index].Bytes);

    /// <summary>The request that invoke 2 is an answer to. The corpus is a five-packet excerpt and does
    /// not contain it, so it is the captured request with its invoke id changed — which is a real BACnet
    /// request, and the point is that the segmented reply cannot be accounted for without one.</summary>
    private static DecodeResult Asking(MessageDef message, byte invokeId)
    {
        var bytes = (byte[])[.. ProtocolCorpus.Get("bacnet").Captures[0].Bytes];
        bytes[8] = invokeId;

        return new MessageCodec(message).Decode(bytes);
    }

    // ── A subject with many instances ─────────────────────────────────────────

    [TestMethod]
    public void The_subject_validates()
    {
        var (subject, _) = Confirmed();
        Assert.AreEqual(0, subject.Validate().Count, string.Join("\n", subject.Validate()));
    }

    [TestMethod]
    public void Two_transactions_on_one_socket_do_not_touch_each_other()
    {
        // The case a single state cannot hold. Both are live at once against the same device, and nothing
        // separates them but the number.
        var (subject, message) = Confirmed();
        var live = new Standing(subject);

        live.Observe(message, Capture(message, 0), Bearing.Sent);        // request, invoke 1
        live.Observe(message, Asking(message, 2), Bearing.Sent);         // request, invoke 2
        live.Observe(message, Capture(message, 2), Bearing.Received);    // its segmented reply

        Assert.AreEqual(2, live.Tracked.Count);

        Assert.AreEqual(Asked, live.PhaseOf(ProtoValue.Of(1L), Us), "invoke 1 is still waiting");
        Assert.AreEqual(Answering, live.PhaseOf(ProtoValue.Of(2L), Us), "invoke 2 is mid-reply");

        // And the answer to one does not end the other.
        live.Observe(message, Capture(message, 1), Bearing.Received);    // ack, invoke 1

        Assert.AreEqual(Done, live.PhaseOf(ProtoValue.Of(1L), Us));
        Assert.AreEqual(Answering, live.PhaseOf(ProtoValue.Of(2L), Us), "untouched");
    }

    [TestMethod]
    public void What_we_know_and_what_we_presume_are_kept_apart()
    {
        // One message on the wire, two views moved, and only one of them observed. A model with a single
        // state cannot say that the device's half is a guess — which is the half a lost packet falsifies.
        var (subject, message) = Confirmed();
        var live = new Standing(subject);

        var progress = live.Observe(message, Capture(message, 0), Bearing.Sent);

        Assert.AreEqual(2, progress.Moves.Count);

        Assert.AreEqual(Confidence.Known, progress.Moves.Single(m => m.Whose == Us).How);
        Assert.AreEqual(Confidence.Presumed, progress.Moves.Single(m => m.Whose == Them).How,
            "that the device has it is inferred, and a dropped datagram makes it wrong");
    }

    [TestMethod]
    public void An_error_ends_the_transaction_the_same_way_an_answer_does()
    {
        var (subject, message) = Confirmed();
        var live = new Standing(subject);

        live.Observe(message, Capture(message, 0), Bearing.Sent);
        live.Observe(message, Capture(message, 4), Bearing.Received);    // error, invoke 1

        Assert.AreEqual(Done, live.PhaseOf(ProtoValue.Of(1L), Us));
    }

    [TestMethod]
    public void A_segment_ack_is_legal_only_while_a_reply_is_in_flight()
    {
        var (subject, message) = Confirmed();
        var live = new Standing(subject);

        var segmentAck = Capture(message, 3);

        Assert.IsFalse(live.WouldAccept(message, segmentAck, Bearing.Sent), "nothing is being answered yet");

        live.Observe(message, Asking(message, 2), Bearing.Sent);
        live.Observe(message, Capture(message, 2), Bearing.Received);    // the segment, invoke 2

        Assert.IsTrue(live.WouldAccept(message, segmentAck, Bearing.Sent));
        live.Observe(message, segmentAck, Bearing.Sent);

        Assert.AreEqual(Answering, live.PhaseOf(ProtoValue.Of(2L), Us), "still mid-reply, waiting for more");
    }

    // ── A subject with one instance, and no exchange at all ───────────────────

    private static readonly Phase Unknown = new() { Id = "unknown", About = "nobody has said." };
    private static readonly Phase On = new() { Id = "on" };
    private static readonly Phase Off = new() { Id = "off" };

    /// <summary>
    /// A light. There is no correlation, no pairing and no reply — a command goes out and what we believe
    /// about the world changes. The belief is <i>presumed</i>, because nothing has confirmed it, and that
    /// is the entire content of what a fire-and-forget command tells you.
    /// </summary>
    private static (Subject Subject, MessageDef Message) Lamp()
    {
        var command = new MessageDef
        {
            Id = "switch",
            Context = Context.Given.These("wanted"),
            Fields =
            [
                new Field
                {
                    Id = "wanted",
                    Pattern = new Pattern.Scalar(1, BigEndian: true),
                    Value = Expr.Parse("inputs.wanted"),
                },
            ],
        };

        Transition Told(Phase to, long wanted, string why) => new()
        {
            Whose = Them, To = to, On = command, When = Expr.Parse($"fields.wanted.value == {wanted}"),
            Way = Bearing.Sent, Confidence = Confidence.Presumed, Because = why,

            // From anywhere: switching a light on is legal whether or not it was already on, and there is
            // no state of the world in which the command is refused.
            From = null,
        };

        return (new Subject
        {
            Id = "lampPower",
            About = "whether the light is lit. One light, so nothing tells one of these from another.",
            Start = Unknown,
            Parties = [Them],

            Transitions =
            [
                Told(On, 1, "a light can always be told to switch on."),
                Told(Off, 0, "and always to switch off."),
            ],
        }, command);
    }

    [TestMethod]
    public void A_command_with_no_reply_still_changes_what_we_believe()
    {
        var (subject, command) = Lamp();
        Assert.AreEqual(0, subject.Validate().Count, string.Join("\n", subject.Validate()));

        var codec = new MessageCodec(command);
        var live = new Standing(subject);

        Assert.AreEqual(Unknown, live.PhaseOf(Them), "nobody has said yet");

        var progress = live.Observe(command, codec.Decode([0x01]), Bearing.Sent);

        Assert.AreEqual(On, live.PhaseOf(Them));
        Assert.AreEqual(Confidence.Presumed, progress.Moves.Single().How,
            "the light is believed to be on, and nothing has confirmed it");

        // Told again, from a state it is already in. Nothing about a light makes that illegal, and a model
        // that demanded a from-phase for every move would have had to invent one that says so.
        live.Observe(command, codec.Decode([0x01]), Bearing.Sent);
        Assert.AreEqual(On, live.PhaseOf(Them));

        live.Observe(command, codec.Decode([0x00]), Bearing.Sent);
        Assert.AreEqual(Off, live.PhaseOf(Them));
    }

    [TestMethod]
    public void A_subject_with_one_instance_needs_nothing_in_the_packet_to_identify_it()
    {
        // The reason the identity is optional rather than a required field carrying a constant. There is
        // one light; asking the message which one would be asking a question the protocol does not have.
        var (subject, command) = Lamp();

        Assert.IsNull(subject.Distinguishes);

        var live = new Standing(subject);
        live.Observe(command, new MessageCodec(command).Decode([0x01]), Bearing.Sent);

        CollectionAssert.AreEqual(new[] { Standing.Only }, live.Tracked.ToArray());
    }

    // ── What it refuses ───────────────────────────────────────────────────────

    [TestMethod]
    public void A_message_that_is_legal_in_itself_can_still_be_wrong_here()
    {
        // Stateful denial, the kind the illegals taxonomy named and nothing could express until now. Every
        // octet of this acknowledgement is well formed and every value is in range; it is refused because
        // of what has not happened.
        var (subject, message) = Confirmed();
        var live = new Standing(subject);

        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => live.Observe(message, Capture(message, 1), Bearing.Received));

        StringAssert.Contains(ex.Message, "legal in itself and not here");
        StringAssert.Contains(ex.Message, "would have to be at 'asked' and is at 'idle'");
        StringAssert.Contains(ex.Message, "an acknowledgement answers an outstanding request");
    }

    [TestMethod]
    public void Answering_a_transaction_that_has_already_finished_is_refused()
    {
        var (subject, message) = Confirmed();
        var live = new Standing(subject);

        live.Observe(message, Capture(message, 0), Bearing.Sent);
        live.Observe(message, Capture(message, 1), Bearing.Received);

        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => live.Observe(message, Capture(message, 1), Bearing.Received));

        StringAssert.Contains(ex.Message, "is at 'done'");

        // Until the id is released, at which point it is a new conversation and not a resumed one.
        Assert.IsTrue(live.Release(ProtoValue.Of(1L)));
        Assert.AreEqual(Idle, live.PhaseOf(ProtoValue.Of(1L), Us));
    }

    [TestMethod]
    public void A_state_nothing_reaches_is_an_authoring_error()
    {
        var (subject, message) = Confirmed();
        var unreachable = new Phase { Id = "abandoned" };

        var broken = new Subject
        {
            Id = subject.Id,
            Start = subject.Start,
            Parties = subject.Parties,
            Distinguishes = subject.Distinguishes,
            Transitions =
            [
                .. subject.Transitions,
                new Transition
                {
                    Whose = Us, From = unreachable, To = Done, On = message, When = Pdu(3),
                    Way = Bearing.Received, Because = "unreachable.",
                },
            ],
        };

        StringAssert.Contains(string.Join("\n", broken.Validate()), "nothing reaches 'abandoned'");
    }
}
