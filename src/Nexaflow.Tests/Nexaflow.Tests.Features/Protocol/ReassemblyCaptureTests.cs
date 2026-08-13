using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.State;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A message spread over several of them, put back together by the document.
///
/// <para>
/// The protocol is WebSocket. What it is here for is the thing the corpus had no instance of: a value that
/// no single message carries. Everything before this could be read out of the octets in front of it —
/// a length, a digest, a component identified by a token — and a fragmented message is none of that. It is
/// what arrived, and then what arrived, and then what arrived, and the wire never says the whole of it.
/// </para>
///
/// <para>
/// <b>Nothing here looks back at an earlier message, and nothing can.</b> What the engine has in front of it
/// is this message, the state, the inputs and the graph — there is no fifth thing, and no history to
/// consult. So a message assembled from several is not the engine remembering frames; it is the document
/// having written each frame's contribution into a slot as it went, and the slot being all that survives.
/// A fragment that has been folded in is gone.
/// </para>
///
/// <para>
/// That is why this needed no reassembly machinery, and why what it did need was so small: a move could set
/// a slot from the message and not from the slot. A state layer like that describes every protocol that
/// remembers and none that accumulates. With <c>kept</c>, "what is there already, and then this" is a
/// sentence a document can write — and the engine still holds no opinion about fragments, because it never
/// sees two of them at once.
/// </para>
///
/// <para>
/// The masking needed nothing whatever. A frame's payload is exclusive-ored with a four-octet key that
/// travels with it, and the engine's <c>xor</c> already cycles a key of any length — so the key being a
/// field rather than a constant is expressible because a converter's arguments in a pipeline are ordinary
/// expressions.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec and state — tree nodes land with the engine")]
public class ReassemblyCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);

    /// <summary>What the payload says, once the mask is taken off it. Its own inverse, so one expression
    /// serves reading and writing.</summary>
    private const string Unmasked = "fields.payload.value |> xor(fields.maskingKey.value)";

    // ── The frame ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One frame. Short-form only: the seven-bit length escapes to sixteen or sixty-four bits at 126 and
    /// 127, which is the same widening this corpus already has three instances of.
    /// </summary>
    /// <summary>
    /// One instance, not one per call.
    /// </summary>
    /// <remarks>
    /// A transition says which message it is about by pointing at it, and identity here is the object. A
    /// factory handing out a fresh document to every caller means the one a transition points at is never
    /// the one that arrives, and every move silently fails to match — the refusal reads "nothing describes
    /// a message like this", which is true and says nothing about why.
    /// </remarks>
    private static readonly MessageDef TheFrame = new()
    {
        Id = "frame",
        Context = Context.Given.These("fin", "opcode", "maskingKey", "payload"),
        Fields =
        [
            new Field
            {
                Id = "first",
                // Three runs from three places: one the caller sets, one the protocol reserves, one the
                // caller sets. There is no single value this could have been written from.
                Pattern = new Pattern.Bits(
                [
                    new BitSlice("fin", 1, Expr.Parse("inputs.fin")),
                    new BitSlice("reserved", 3, Expr.Parse("0")),
                    new BitSlice("opcode", 4, Expr.Parse("inputs.opcode")),
                ]),
            },

            // And two runs from two more: a bit this protocol fixes, and a length measured off a field
            // further down that has not been written yet when this is settled.
            new Field
            {
                Id = "second",
                Pattern = new Pattern.Bits(
                [
                    new BitSlice("masked", 1, Expr.Parse("1")),
                    new BitSlice("length", 7, Expr.Parse("fields.payload.extent")),
                ]),
            },

            new Field
            {
                Id = "maskingKey",
                Pattern = new Pattern.Opaque(4),
                Value = Expr.Parse("inputs.maskingKey"),
            },

            // Carried as it arrived — masked. What it says is a computation over it and the key, which is
            // the same expression in both directions because the mask is its own inverse.
            new Field
            {
                Id = "payload",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.second.value.length")),
                Value = Expr.Parse("inputs.payload |> xor(inputs.maskingKey)"),
            },
        ],
    };

    private static MessageDef Frame() => TheFrame;

    // ── The state ─────────────────────────────────────────────────────────────

    private static readonly Party Us = new() { Id = "us" };

    private static readonly Phase Whole = new() { Id = "whole", About = "nothing is half-said." };
    private static readonly Phase Partway = new() { Id = "partway", About = "a message is unfinished." };

    private static readonly Recall Assembling = new()
    {
        Id = "assembling",
        About = "the octets of a message that is still arriving.",
    };

    private static Transition Move(string name, Phase? from, Phase to, string when, string keeps,
                                   bool ends, string because) => new()
    {
        Whose = Us,
        On = Frame(),
        Way = Bearing.Received,
        From = from,
        To = to,
        When = Expr.Parse(when),
        Records = [new Recording(Expr.Parse(keeps), Assembling)],
        Ends = ends,
        Because = because,
    };

    /// <summary>
    /// What a run of frames adds up to, said entirely by the document.
    ///
    /// <para>
    /// Four moves and the engine understands none of them. It does not know that a zero opcode means
    /// continuation, that the final bit is final, or that octets concatenate — it evaluates what the
    /// document wrote, puts the answer where the document said, and forgets the instance on the one move
    /// the document marked as ending. Which is the whole point: reassembly is a protocol's business and
    /// there is nothing about it in the engine.
    /// </para>
    /// </summary>
    private static Subject Reassembly() => new()
    {
        Id = "message",
        Start = Whole,
        Parties = [Us],
        Transitions =
        [
            // Whole in one frame. Recorded anyway, because what a caller wants is the message and it
            // should not have to know whether it arrived in pieces.
            Move("entire", Whole, Whole, "fields.fin.value == 1 && fields.opcode.value != 0",
                 Unmasked, ends: false,
                 "an unfragmented message is a frame that is final and is not a continuation."),

            Move("opens", Whole, Partway, "fields.fin.value == 0 && fields.opcode.value != 0",
                 Unmasked, ends: false,
                 "a fragmented message begins with a frame that is not final and says what kind it is."),

            // The sentence that could not be written before: what is there already, and then this.
            Move("continues", Partway, Partway, "fields.fin.value == 0 && fields.opcode.value == 0",
                 $"kept.assembling |> concat({Unmasked})", ends: false,
                 "a continuation that is not final adds to what is being assembled."),

            // And the one that says when to let go. The engine forgets because the document said so here,
            // not because it worked out that the message was over.
            Move("closes", Partway, Whole, "fields.fin.value == 1 && fields.opcode.value == 0",
                 $"kept.assembling |> concat({Unmasked})", ends: true,
                 "a final continuation completes the message it was adding to."),
        ],
    };

    // ── Octets ────────────────────────────────────────────────────────────────

    private static readonly byte[] Key = [0x37, 0xfa, 0x21, 0x3d];

    private static byte[] FrameOf(long fin, long opcode, string text)
        => new MessageCodec(Frame()).Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("fin", ProtoValue.Of(fin)),
            ("opcode", ProtoValue.Of(opcode)),
            ("maskingKey", ProtoValue.Of(Key)),
            ("payload", ProtoValue.Of(System.Text.Encoding.ASCII.GetBytes(text))))));

    private static DecodeResult Read(byte[] octets) => new MessageCodec(Frame()).Decode(octets);

    // ── The frame itself ──────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Frame()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));

        var subject = Reassembly();
        Assert.AreEqual(0, subject.Validate().Count, string.Join("\n", subject.Validate()));
    }

    [TestMethod]
    public void A_frame_is_masked_on_the_wire_and_says_something_else()
    {
        var octets = FrameOf(1, 1, "Hello");

        // The classic example from the specification: this text under this key is these octets.
        CollectionAssert.AreEqual(
            new byte[] { 0x81, 0x85, 0x37, 0xfa, 0x21, 0x3d, 0x7f, 0x9f, 0x4d, 0x51, 0x58 }, octets);

        // The field carries what arrived. What it means is a computation over it and the key — and the
        // same computation both ways, because the mask is its own inverse.
        var decoded = Read(octets);
        CollectionAssert.AreEqual(new byte[] { 0x7f, 0x9f, 0x4d, 0x51, 0x58 },
            decoded["payload"].AsBytes(), "masked, as it lay on the wire");

        CollectionAssert.AreEqual(octets, new MessageCodec(Frame()).Encode(
            new EvalScope().Set("inputs", EvalScope.Record(
                ("fin", decoded["fin"]), ("opcode", decoded["opcode"]),
                ("maskingKey", decoded["maskingKey"]),
                ("payload", ProtoValue.Of(Unmask(decoded)))))));
    }

    private static byte[] Unmask(DecodeResult decoded)
    {
        var payload = (byte[])decoded["payload"].AsBytes().Clone();
        var key = decoded["maskingKey"].AsBytes();

        for (int i = 0; i < payload.Length; i++) payload[i] ^= key[i % key.Length];

        return payload;
    }

    // ── Putting it back together ──────────────────────────────────────────────

    /// <summary>What one frame does to the state, and what is in the slot afterwards.</summary>
    private static (Progress Progress, string Assembled) Deliver(
        Standing standing, long fin, long opcode, string text)
    {
        var progress = standing.Observe(Frame(), Read(FrameOf(fin, opcode, text)), Bearing.Received);

        return (progress, System.Text.Encoding.ASCII.GetString(
            standing.Recalled(Assembling) is ProtoValue.Bytes octets ? octets.Value : []));
    }

    [TestMethod]
    public void A_message_no_single_frame_carries_is_assembled_by_the_document()
    {
        var standing = new Standing(Reassembly());

        var opened = Deliver(standing, 0, 1, "Hel");
        Assert.AreEqual("Hel", opened.Assembled);
        Assert.AreEqual(Partway, standing.PhaseOf(Us));

        var middle = Deliver(standing, 0, 0, "lo, ");
        Assert.AreEqual("Hello, ", middle.Assembled, "what was there already, and then this");
        Assert.AreEqual(Partway, standing.PhaseOf(Us));

        // The frame that finishes it. Nothing in the engine worked out that the message was over — the
        // document said this move ends the instance, so the slot goes with it.
        var last = Deliver(standing, 1, 0, "world");

        Assert.AreEqual("Hello, world",
            System.Text.Encoding.ASCII.GetString(last.Progress.Kept.Single().What.AsBytes()));

        Assert.IsTrue(last.Progress.Ended);
        Assert.AreEqual(Whole, standing.PhaseOf(Us));
        Assert.AreEqual("", last.Assembled, "and nothing is left holding the message that finished");
    }

    [TestMethod]
    public void A_continuation_with_nothing_to_continue_is_refused()
    {
        // Not a shape error and not a value error: the frame is perfectly well formed and every field in
        // it is legal. It is wrong because of what has and has not already happened, which is the one kind
        // of illegal only a state can name.
        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => Deliver(new Standing(Reassembly()), 1, 0, "world"));

        StringAssert.Contains(ex.Message, "legal in itself and not here");
        StringAssert.Contains(ex.Message, "'partway'");
    }

    [TestMethod]
    public void A_whole_message_never_touches_the_slot_it_would_have_accumulated_into()
    {
        // The unfragmented case is a move of its own rather than a special case of the others, so a
        // message that arrives in one frame leaves nothing behind to be added to by the next one.
        var standing = new Standing(Reassembly());

        Assert.AreEqual("Hello", Deliver(standing, 1, 1, "Hello").Assembled);
        Assert.AreEqual(Whole, standing.PhaseOf(Us));

        Assert.AreEqual("Again", Deliver(standing, 1, 1, "Again").Assembled,
            "the second replaces the first rather than being appended to it");
    }

    // ── A fold that leaves the walk ───────────────────────────────────────────

    /// <summary>
    /// A run whose answer is the run itself, and a state that keeps it.
    ///
    /// <para>
    /// The other half of the same problem. <c>kept</c> let a move accumulate across messages; this is
    /// accumulation <i>within</i> one — a value every structure contributes to and none of them carries.
    /// The chain already threaded it and then dropped it on the floor when the walk ended, so the fold was
    /// computed and unreachable.
    /// </para>
    ///
    /// <para>
    /// Naming it makes it a node, and the state layer then needs nothing new at all: an ordinary recording
    /// reads it as it reads any other part of the message. Which is the shape HPACK wants — a table built
    /// across a header block and handed to the next one — with the table being an ordinary value rather
    /// than a thing the engine knows about.
    /// </para>
    /// </summary>
    private static readonly MessageDef Ledger = new()
    {
        Id = "ledger",
        Context = Context.Given.These("entries"),
        Fields =
        [
            new Field
            {
                Id = "entries",
                Value = Expr.Parse("inputs.entries"),
                Pattern = new Pattern.Chain(
                    new Field { Id = "amount", Pattern = U8, Value = Expr.Parse("item") },
                    Expr.Parse("room > 0"),
                    Seed: Expr.Parse("0"),
                    Carry: Expr.Parse("carried + fields.amount.value"),

                    // Without this the total is worked out and then thrown away.
                    Thread: "total"),
            },
        ],
    };

    private static readonly Recall Running = new()
    {
        Id = "running", About = "everything counted so far, across every message.",
    };

    private static Subject Counting() => new()
    {
        Id = "counting",
        Start = Whole,
        Parties = [Us],
        Transitions =
        [
            new Transition
            {
                Whose = Us, On = Ledger, Way = Bearing.Received, From = Whole, To = Whole,
                When = Expr.Parse("true"),

                // Reads the fold as it would read any other part of the message, because that is what it
                // now is. Nothing here knows the value came from a walk rather than off the wire.
                // A slot nothing has written yet holds nothing, and nothing is not zero — so the document
                // says what an empty ledger starts from rather than the engine guessing on its behalf.
                Records = [new Recording(Expr.Parse("(kept.running ?? 0) + fields.total.value"), Running)],
                Because = "a ledger adds to the running total.",
            },
        ],
    };

    [TestMethod]
    public void A_value_every_structure_contributes_to_can_leave_the_walk()
    {
        var codec = new MessageCodec(Ledger);
        Assert.AreEqual(0, codec.Validate().Count, string.Join(" / ", codec.Validate()));

        var decoded = codec.Decode([3, 4, 5]);

        // No structure carries it and the wire never states it.
        Assert.AreEqual(12L, decoded["total"].AsInt());
        Assert.AreEqual(3, decoded["entries"].AsList().Count);
    }

    [TestMethod]
    public void And_a_move_can_keep_it_without_the_state_layer_learning_anything()
    {
        var standing = new Standing(Counting());
        var codec = new MessageCodec(Ledger);

        standing.Observe(Ledger, codec.Decode([3, 4, 5]), Bearing.Received);
        Assert.AreEqual(12L, standing.Recalled(Running).AsInt());

        // And across messages, which is the pair of accumulations meeting: one within a message, one
        // between them, and neither of them a mechanism the engine provides.
        standing.Observe(Ledger, codec.Decode([10, 20]), Bearing.Received);
        Assert.AreEqual(42L, standing.Recalled(Running).AsInt());
    }

    [TestMethod]
    public void Naming_a_fold_that_is_never_computed_is_refused()
    {
        var idle = Ledger with
        {
            Id = "idle",
            Fields =
            [
                new Field
                {
                    Id = "entries",
                    Value = Expr.Parse("inputs.entries"),
                    Pattern = new Pattern.Chain(
                        new Field { Id = "amount", Pattern = U8, Value = Expr.Parse("item") },
                        Expr.Parse("room > 0"), Thread: "total"),
                },
            ],
        };

        Assert.IsTrue(new MessageCodec(idle).Validate()
            .Any(i => i.Contains("names what it threads and threads nothing")));
    }

    /// <summary>
    /// The engine holds no opinion about any of this.
    /// </summary>
    /// <remarks>
    /// Worth asserting rather than merely believing. Every fact about reassembly — that a zero opcode
    /// continues, that the final bit is final, that octets join end to end, that the run is over and the
    /// slot may go — is in the document's four moves. Nothing in the state layer mentions fragments, and
    /// the slot's own description is a sentence about a protocol, not a mechanism the engine provides.
    /// </remarks>
    [TestMethod]
    public void Nothing_about_fragments_is_known_to_the_engine()
    {
        var subject = Reassembly();

        Assert.AreEqual(4, subject.Transitions.Count);
        Assert.AreEqual(1, subject.Transitions.Count(t => t.Ends),
            "exactly one move says when to let go, and the document chose it");

        // The accumulation is an expression the document wrote, over a root the engine only binds.
        Assert.AreEqual(2, subject.Transitions.Count(
            t => t.Records.Any(r => Vocabulary.RootsOf(r.From).Contains(Vocabulary.Kept))));
    }
}
