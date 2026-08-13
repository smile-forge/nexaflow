using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// How one value becomes octets, as one thing the codec looks up rather than arms of a switch it has.
///
/// <para>
/// The three integer encodings whose width comes from their value — a continuation chain, an escaping
/// marker, a marked integer — were three arms of the codec's writer and three more of its reader, plus a
/// hand-kept list naming which arms existed. Here they are three forms behind one contract, and the walk
/// does not learn which one it got.
/// </para>
///
/// <para>
/// The bug that motivated it: the list and the switch disagreed, in both directions at once. It named a
/// shape the writer would have thrown on, and it omitted one the writer could not write — and the second
/// kind of disagreement <b>encoded a message silently without that field</b>, which is the worst way for
/// two descriptions of one thing to drift.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol wire forms — engine structure, no single product node")]
public class WireFormTests
{
    private static MessageDef Around(Pattern middle) => new()
    {
        Id = "around",
        Context = Context.Given.These("edge", "held"),
        Fields =
        [
            new Field { Id = "before", Pattern = new Pattern.Scalar(1, true), Value = Expr.Parse("inputs.edge") },
            new Field { Id = "middle", Pattern = middle, Value = Expr.Parse("inputs.held") },
            new Field { Id = "after", Pattern = new Pattern.Scalar(1, true), Value = Expr.Parse("inputs.edge") },
        ],
    };

    private static byte[] Encode(MessageDef message, long held)
        => new GraphCodec(message).Encode(new Dictionary<string, ProtoValue>
            { ["edge"] = ProtoValue.Of(0xAAL), ["held"] = ProtoValue.Of(held) });

    private static ProtoValue Decoded(MessageDef message, byte[] octets)
        => new GraphCodec(message).Decode(octets)
               .Nodes.Single(n => n.Of is Field { Id: "middle" }).Value;

    // ── The disagreement that motivated this ──────────────────────────────────

    [TestMethod]
    public void A_field_whose_value_the_walk_cannot_lay_down_is_not_quietly_left_out()
    {
        // The old shape of this: Handles() listed the writer's arms by hand, a varint was not among them,
        // and a field that is not "handled" was skipped rather than refused — so this encoded to AA AA and
        // reported success. Nothing announced that a third of the message was missing.
        var written = Encode(Around(new Pattern.Varint(GroupOrder.LeastSignificantFirst, 5)), 300);

        Assert.AreNotEqual(2, written.Length, "the middle field is not silently absent");
        Assert.AreEqual(0xAA, written[0]);
        Assert.AreEqual(0xAA, written[^1]);
    }

    [TestMethod]
    public void What_the_walk_says_it_handles_is_what_the_writer_can_write()
    {
        // One question asked once. It used to be asked twice — a list, and a switch — and they drifted.
        foreach (var pattern in new Pattern[]
                 {
                     new Pattern.Scalar(2, true),
                     new Pattern.Opaque(4),
                     new Pattern.Varint(GroupOrder.LeastSignificantFirst, 5),
                     new Pattern.EscapedInline(128, 4),
                     new Pattern.Prefixed(2, [1, 2, 4]),
                 })
            Assert.IsTrue(GraphCodec.Handles(new Field { Id = "f", Pattern = pattern }),
                          $"{pattern.GetType().Name} has a form, so the walk handles it");
    }

    // ── The three widths that come from a value ───────────────────────────────

    [TestMethod]
    [DataRow(0L, 1)]
    [DataRow(127L, 1)]
    [DataRow(128L, 2)]
    [DataRow(300L, 2)]
    [DataRow(16384L, 3)]
    public void A_continuation_chain_is_as_wide_as_its_value_needs(long held, int octets)
    {
        var message = Around(new Pattern.Varint(GroupOrder.LeastSignificantFirst, 5));
        var written = Encode(message, held);

        Assert.AreEqual(2 + octets, written.Length, "one octet either side, and the chain between");
        Assert.AreEqual(held, Decoded(message, written).AsInt());
    }

    [TestMethod]
    [DataRow(0L, 1)]
    [DataRow(127L, 1)]
    [DataRow(128L, 2)]
    [DataRow(70000L, 4)]
    public void An_escaping_marker_is_as_wide_as_its_value_needs(long held, int octets)
    {
        var message = Around(new Pattern.EscapedInline(128, 4));
        var written = Encode(message, held);

        Assert.AreEqual(2 + octets, written.Length);
        Assert.AreEqual(held, Decoded(message, written).AsInt());
    }

    [TestMethod]
    [DataRow(0L, 1)]
    [DataRow(63L, 1)]
    [DataRow(64L, 2)]
    [DataRow(16383L, 2)]
    [DataRow(16384L, 4)]
    public void A_marked_integer_is_as_wide_as_its_value_needs(long held, int octets)
    {
        // Two leading bits select one of three widths, and the rest of that first octet is the value's top.
        var message = Around(new Pattern.Prefixed(2, [1, 2, 4]));
        var written = Encode(message, held);

        Assert.AreEqual(2 + octets, written.Length);
        Assert.AreEqual(held, Decoded(message, written).AsInt());
    }

    /// <summary>
    /// The reason none of this needed anything new from the resolver.
    /// </summary>
    /// <remarks>
    /// A width that comes from a value is not a special case and never was: the appearance declares that
    /// its extent waits on its value, and the worklist settles them in that order. That machinery was
    /// already there and already right — what was missing was only that laying a value down was a switch
    /// on shape instead of a lookup, so an encoding the switch had no arm for could not participate.
    /// </remarks>
    [TestMethod]
    public void A_width_that_comes_from_a_value_is_settled_by_the_ordinary_worklist()
    {
        var message = Around(new Pattern.Varint(GroupOrder.LeastSignificantFirst, 5));
        var run = new GraphCodec(message).Decode(Encode(message, 300));
        var middle = run.Nodes.Single(n => n.Of is Field { Id: "middle" });

        Assert.AreEqual(2, middle.Settled(Facet.Extent), "two octets, because 300 needed two");
        Assert.AreEqual(1, run.Nodes.Single(n => n.Of is Field { Id: "before" }).Settled(Facet.Extent));
    }

    [TestMethod]
    public void A_value_too_wide_for_the_form_that_carries_it_is_refused_by_name()
    {
        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => Encode(Around(new Pattern.Prefixed(2, [1, 2])), 1_000_000));

        StringAssert.Contains(refused.Message, "middle");
        StringAssert.Contains(refused.Message, "widest");
    }

    // ── One walk, so one answer ───────────────────────────────────────────────

    /// <summary>
    /// A field neither direction can handle is refused by both, in the same words.
    /// </summary>
    /// <remarks>
    /// There were two walks — a resolver-scheduled one going out, a recursive generator coming in — and
    /// the refusal lived only in the second. So reading said which field and why, and writing stepped over
    /// it and returned a short message that looked fine. Now the refusal is on the one path both take, and
    /// there is no arrangement of the code in which only one of them has it.
    /// </remarks>
    [TestMethod]
    public void Neither_direction_steps_over_a_field_it_cannot_handle()
    {
        var beyond = new MessageDef
        {
            Id = "beyond",
            Context = Context.Given.These("edge", "held"),
            Fields =
            [
                new Field { Id = "before", Pattern = new Pattern.Scalar(1, true), Value = Expr.Parse("inputs.edge") },
                new Field
                {
                    Id = "middle",
                    Pattern = new Pattern.Assorted(
                        new Field { Id = "tag", Pattern = new Pattern.Scalar(1, true) },
                        [new Arm("one", 1, [])],
                        Expr.Parse("true")),
                },
            ],
        };

        var writing = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(beyond).Encode(new Dictionary<string, ProtoValue>
                { ["edge"] = ProtoValue.Of(1L), ["held"] = ProtoValue.Of(1L) }));

        var reading = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(beyond).Decode(new byte[] { 0xAA, 0x01 }));

        Assert.AreEqual(writing.Message, reading.Message, "one walk, so one answer");
        StringAssert.Contains(writing.Message, "middle");
    }

    [TestMethod]
    public void Reading_settles_every_facet_of_a_field_as_the_walk_reaches_it()
    {
        // The asymmetry worth keeping rather than designing away: going out, a value can wait on a field
        // not laid down yet, so the facts are scheduled. Coming in nothing later can inform anything
        // earlier, so they all fall out of one read.
        var message = Around(new Pattern.Varint(GroupOrder.LeastSignificantFirst, 5));
        var run = new GraphCodec(message).Decode(Encode(message, 300));
        var middle = run.Nodes.Single(n => n.Of is Field { Id: "middle" });

        foreach (var facet in new[] { Facet.Position, Facet.Extent, Facet.Value, Facet.Emitted })
            Assert.IsTrue(middle.Has(facet), $"{facet} came off the wire with the rest");

        Assert.AreEqual(1, middle.Settled(Facet.Position), "after the one octet before it");
        CollectionAssert.AreEqual(new byte[] { 0xAC, 0x02 },
                                  ((ProtoValue.Bytes)(ProtoValue)middle.Settled(Facet.Emitted)!).Value);
    }

    // ── Reading at whatever alignment it finds ────────────────────────────────

    [TestMethod]
    public void The_cursor_reads_a_run_that_does_not_start_on_an_octet_boundary()
    {
        // Emission counts bits, so reading has to as well, or a field described mid-octet can be written
        // and not read back.
        var cursor = new BitCursor([0b1010_1101, 0b1001_1001]);

        Assert.AreEqual(0b10101L, cursor.Read(5));
        Assert.AreEqual(0b101_1001_1001L, cursor.Read(11));
        Assert.AreEqual(0, cursor.Remaining);
    }

    [TestMethod]
    public void Peeking_does_not_move_and_reading_does()
    {
        var cursor = new BitCursor([0xAB, 0xCD]);

        Assert.AreEqual(0xABL, cursor.Peek(8));
        Assert.AreEqual(0, cursor.At, "peeking is how a form finds its end before committing to it");
        Assert.AreEqual(0xCDL, cursor.Peek(8, skip: 8));
        Assert.AreEqual(0xABL, cursor.Read(8));
        Assert.AreEqual(8, cursor.At);
    }
}
