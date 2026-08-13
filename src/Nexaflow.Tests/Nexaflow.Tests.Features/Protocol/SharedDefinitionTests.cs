using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// One definition, several messages.
///
/// <para>
/// Three DHCP messages share most of what they carry and differ in the rest; the same option definitions
/// appear in all of them. Declaring those once and pointing at them from each message is the whole reason
/// a definition wants to be a node rather than a list nested inside one message's declaration.
/// </para>
///
/// <para>
/// The question this answers is whether the model already allows it, because identity is the object here:
/// a shape shared between two messages is <b>the same nodes</b> in both graphs, and anything keyed on
/// those nodes has to cope. It does — and the reason it does is that two packings never coexist, so a
/// node shared between them is realised once in any given run.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol structure — definition sharing, no single product node")]
public class SharedDefinitionTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);

    /// <summary>Declared once, at module scope, and pointed at by everything below.</summary>
    private static readonly Pattern Options = new Pattern.Assorted(
        new Field { Id = "optionCode", Pattern = U8 },
        [
            Arm.On("pad", ProtoValue.Of(0L), [], repeats: true),

            Arm.Otherwise("other",
            [
                new Field
                {
                    Id = "optionLength", Pattern = U8, Value = Expr.Parse("fields.optionValue.extent"),
                },
                new Field
                {
                    Id = "optionValue",
                    Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.optionLength.value")),
                    Value = Expr.Parse("item.optionValue"),
                },
            ], repeats: true),
        ],
        Expr.Parse("room > 0"));

    private static readonly Field Run = new() { Id = "options", Pattern = Options,
                                                Value = Expr.Parse("inputs.options") };

    /// <summary>Two messages that agree about their options and disagree about everything else.</summary>
    private static MessageDef Message(string id, params Field[] before) => new()
    {
        Id = id,
        Context = [.. Context.Given.These([.. before.Select(f => f.Id), "options"])],
        Fields = [.. before, Run],
    };

    private static Field Octet(string id) => new() { Id = id, Pattern = U8,
                                                     Value = Expr.Parse($"inputs.{id}") };

    private static readonly Field Operation = Octet("operation");

    private static readonly MessageDef Discover = Message("discover", Operation, Octet("hops"));
    private static readonly MessageDef Offer = Message("offer", Operation, Octet("elapsed"), Octet("flags"));

    private static ProtoValue Option(long code, params byte[] value)
        => EvalScope.Record(("sort", ProtoValue.Of("other")), ("optionCode", ProtoValue.Of(code)),
                            ("optionValue", ProtoValue.Of(value)));

    private static EvalScope Inputs(MessageDef message, params ProtoValue[] options)
        => new EvalScope().Set("inputs", EvalScope.Record(
            [.. message.Context.Select(c => (c.Key,
                   c.Key == "options" ? new ProtoValue.List(options) : ProtoValue.Of(1L)))]));

    // ── That it is allowed at all ─────────────────────────────────────────────

    [TestMethod]
    public void Both_messages_validate_while_sharing_the_same_definition()
    {
        foreach (var message in new[] { Discover, Offer })
        {
            var issues = new MessageCodec(message).Validate();
            Assert.AreEqual(0, issues.Count, $"{message.Id}: " + string.Join("\n", issues));
        }
    }

    [TestMethod]
    public void The_shared_nodes_are_the_same_objects_in_both_graphs()
    {
        // Identity is the object, so this is the real question: not "do both have an options field" but
        // "is it the same one". A copy in each would be two things that can drift.
        Assert.IsTrue(Discover.Graph.Nodes.Contains(Run));
        Assert.IsTrue(Offer.Graph.Nodes.Contains(Run));

        var declared = ((Pattern.Assorted)Options).Sorts.SelectMany(s => s.Fields);

        foreach (var part in declared)
        {
            Assert.IsTrue(Discover.Graph.Nodes.Contains(part), $"{part.Id} missing from discover");
            Assert.IsTrue(Offer.Graph.Nodes.Contains(part), $"{part.Id} missing from offer");
        }
    }

    [TestMethod]
    public void And_each_message_lays_them_out_in_its_own_place()
    {
        // The packing path differs, which is the point: the same definition sits third in one message and
        // fourth in the other, and neither has to know about the other.
        var laidOut = Discover.Walk(Discover.Arrangements.Single()).Select(n => n.Name).ToList();
        var other = Offer.Walk(Offer.Arrangements.Single()).Select(n => n.Name).ToList();

        Assert.AreEqual("operation, hops, optionCode", string.Join(", ", laidOut));
        Assert.AreEqual("operation, elapsed, flags, optionCode", string.Join(", ", other));

        // The shared run sits third in one and fourth in the other, and neither had to know.
        Assert.AreSame(Operation, Discover.Graph.From<Then>(Discover.Arrangements.Single()).Single().To);
        Assert.AreSame(Operation, Offer.Graph.From<Then>(Offer.Arrangements.Single()).Single().To);
    }

    // ── That it works ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Both_messages_round_trip_through_the_shared_definition()
    {
        byte[] discovered = new MessageCodec(Discover).Encode(
            Inputs(Discover, Option(53, 0x01), Option(55, 0x01, 0x03)));

        CollectionAssert.AreEqual(
            new byte[] { 0x01, 0x01, 53, 1, 0x01, 55, 2, 0x01, 0x03 }, discovered);

        byte[] offered = new MessageCodec(Offer).Encode(Inputs(Offer, Option(53, 0x02)));

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x01, 0x01, 53, 1, 0x02 }, offered,
            "three octets of its own, then the very same option run");

        // And back, through each message's own reading of the same nodes.
        Assert.AreEqual(2, new MessageCodec(Discover).Decode(discovered)["options"].AsList().Count);
        Assert.AreEqual(1, new MessageCodec(Offer).Decode(offered)["options"].AsList().Count);
    }
}
