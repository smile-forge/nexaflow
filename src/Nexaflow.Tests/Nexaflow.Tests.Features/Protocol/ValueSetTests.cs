using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A set of values is three questions, not one.
///
/// <para>
/// <b>Membership</b> is what is in it. <b>Bounding</b> is whether that is the whole story. <b>Exclusivity</b>
/// is whether a value settles which member is meant. The engine had only the first, spelled as "the legal
/// values are…", and that one answer was made to stand for all three — so an unlisted value was always
/// malformed and a value always meant exactly what the list said.
/// </para>
///
/// <para>
/// IP protocol numbers are the case that forces the split: <b>open</b>, because the registry keeps growing
/// and a number nobody has heard of is a newer peer rather than a corrupt packet; and <b>indicative</b>,
/// because the number points at a protocol without settling it. Neither is expressible as a list of legal
/// values, and the two are independent — a TLS extension type is open <i>and</i> definitive.
/// </para>
///
/// <para>
/// The parts are not labels. Each changes what the engine does, and the sharpest consequence is that
/// exclusivity decides whether asking for two structures to carry different values is even a coherent
/// request.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol value-set model — tree nodes land with the engine")]
public class ValueSetTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);

    /// <summary>Open, and indicative: 6 suggests TCP, and a number outside the list is one this document
    /// has not caught up with.</summary>
    private static ValueSet ProtocolNumbers() => new()
    {
        Id = "ipProtocolNumbers",
        Bounding = Bounding.Open,
        Exclusivity = Exclusivity.Indicative,
        Members = [SetMember.Of(1, "icmp"), SetMember.Of(6, "tcp"), SetMember.Of(17, "udp")],
        Because = "the registry keeps growing, and the number is a hint about what follows rather than a "
                + "definition of it.",
    };

    /// <summary>Open, and definitive: the registry grows, but each number denotes exactly one extension.
    /// The two axes are independent, and this is the combination that shows it.</summary>
    private static ValueSet ExtensionTypes() => new()
    {
        Id = "extensionTypes",
        Bounding = Bounding.Open,
        Exclusivity = Exclusivity.Definitive,
        Members = [SetMember.Of(0, "serverName"), SetMember.Of(10, "supportedGroups"), SetMember.Of(13, "signatureAlgorithms")],
        Because = "IANA keeps assigning them, but an assigned number means one extension and only that one.",
    };

    /// <summary>Closed, and definitive: two bits, four values, all of them spoken for.</summary>
    private static ValueSet Priorities() => new()
    {
        Id = "npduPriorities",
        Bounding = Bounding.Closed,
        Exclusivity = Exclusivity.Definitive,
        Members = [SetMember.Of(0, "normal"), SetMember.Of(1, "urgent"),
                   SetMember.Of(2, "criticalEquipment"), SetMember.Of(3, "lifeSafety")],
        Because = "the field is two bits wide and the standard names all four.",
    };

    private static MessageDef OneField(ValueSet set) => new()
    {
        Id = "probe",
        Context = Context.Given.These("code"),
        Fields = [new Field { Id = "code", Pattern = U8, Value = Expr.Parse("inputs.code"), Draws = (set, null) }],
    };

    // ── Bounding: what an unlisted value means ────────────────────────────────

    [TestMethod]
    public void A_value_outside_a_closed_set_is_refused()
    {
        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => new MessageCodec(OneField(Priorities())).Decode([0x07]));

        StringAssert.Contains(ex.Message, "not in 'npduPriorities'");
        StringAssert.Contains(ex.Message, "the standard names all four");
    }

    [TestMethod]
    public void A_value_outside_an_open_set_is_read_rather_than_refused()
    {
        // The distinction the old model could not draw. 132 is SCTP, assigned after plenty of software
        // that reads IP headers was written; that software is out of date, not looking at a bad packet.
        var decoded = new MessageCodec(OneField(ProtocolNumbers())).Decode([132]);

        Assert.AreEqual(132, decoded["code"].AsInt());
    }

    // ── Exclusivity: whether a value settles what is meant ────────────────────

    [TestMethod]
    public void A_definitive_set_says_what_a_value_denotes_and_an_indicative_one_declines_to()
    {
        Assert.AreEqual("urgent", Priorities().Denotation(ProtoValue.Of(1L)));

        // 6 appears in the list beside "tcp" and the set still refuses to answer, because answering would
        // be the engine inventing a meaning the protocol does not guarantee.
        Assert.IsNull(ProtocolNumbers().Denotation(ProtoValue.Of(6L)),
            "an indicative value points at a member without settling it");

        Assert.IsNull(Priorities().Denotation(ProtoValue.Of(9L)), "and an unlisted value denotes nothing");
    }

    // ── Where the three parts meet: distinctness ──────────────────────────────

    private static (MessageDef Message, Field Chain) Repeated(ValueSet set)
    {
        var code = new Field
        {
            Id = "code",
            Pattern = U8,
            Value = Expr.Parse("item.code"),
            Draws = (set, null),
        };

        var chain = new Field
        {
            Id = "codes",
            Value = Expr.Parse("inputs.codes"),
            Pattern = new Pattern.Chain(new Field { Id = "entry", Pattern = new Pattern.Group([code]) },
                                        Expr.Parse("room > 0")),
        };

        return (new MessageDef
        {
            Id = "probe",
            Context = Context.Given.These("codes"),
            Fields = [chain],
            Rules =
            [
                new Rule.Distinct
                {
                    Chain = chain,
                    Of = code,
                    Because = "each entry speaks about one member, so naming it twice is two answers to "
                            + "one question.",
                },
            ],
        }, chain);
    }

    [TestMethod]
    public void Distinctness_over_a_definitive_set_refuses_a_repeat_however_far_back_it_was()
    {
        // Not a pairwise rule: the duplicate here is three structures back, which is exactly what an
        // arrangement rule — reaching only `previous` — cannot see.
        var (message, _) = Repeated(ExtensionTypes());
        Assert.AreEqual(0, message.Validate().Count, string.Join("\n", message.Validate()));

        var codec = new MessageCodec(message);
        Assert.AreEqual(3, codec.Decode([0, 10, 13])["codes"].AsList().Count);

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Decode([0, 10, 13, 0]));

        StringAssert.Contains(ex.Message, "structure 3");
        StringAssert.Contains(ex.Message, "structure 0 already carries");
        StringAssert.Contains(ex.Message, "two answers to one question");
    }

    [TestMethod]
    public void Distinctness_over_an_indicative_set_is_refused_as_a_rule_rather_than_enforced()
    {
        // The headline. Over an indicative set the same value twice need not be about the same thing, so
        // the rule is not true of the protocol and enforcing it would refuse well-formed messages. The
        // engine says so before any packet arrives, and says which of the three answers made it incoherent.
        var (message, _) = Repeated(ProtocolNumbers());

        var issues = string.Join("\n", message.Validate());

        StringAssert.Contains(issues, "'ipProtocolNumbers' is indicative");
        StringAssert.Contains(issues, "would refuse messages that are well formed");
        StringAssert.Contains(issues, "Either the set is definitive and should say so");
    }

    [TestMethod]
    public void Distinctness_over_a_field_with_no_set_is_refused_because_the_set_is_what_decides()
    {
        var code = new Field { Id = "code", Pattern = U8, Value = Expr.Parse("item.code") };

        var chain = new Field
        {
            Id = "codes",
            Value = Expr.Parse("inputs.codes"),
            Pattern = new Pattern.Chain(new Field { Id = "entry", Pattern = new Pattern.Group([code]) },
                                        Expr.Parse("room > 0")),
        };

        var message = new MessageDef
        {
            Id = "probe",
            Context = Context.Given.These("codes"),
            Fields = [chain],
            Rules = [new Rule.Distinct { Chain = chain, Of = code, Because = "no." }],
        };

        StringAssert.Contains(string.Join("\n", message.Validate()),
            "does not say what set its values come from");
    }

    [TestMethod]
    public void Distinctness_must_be_about_a_field_of_the_structure_that_repeats()
    {
        var elsewhere = new Field { Id = "elsewhere", Pattern = U8, Value = Expr.Parse("inputs.elsewhere"),
                                    Draws = (ExtensionTypes(), null) };

        var (message, chain) = Repeated(ExtensionTypes());

        var confused = message with
        {
            Context = Context.Given.These("codes", "elsewhere"),
            Fields = [elsewhere, .. message.Fields],
            Rules = [new Rule.Distinct { Chain = chain, Of = elsewhere, Because = "no." }],
        };

        StringAssert.Contains(string.Join("\n", confused.Validate()),
            "is not a field of the structure 'codes' repeats");
    }

    // ── The set as a node, not a list on a field ──────────────────────────────

    [TestMethod]
    public void One_set_serves_several_fields_and_the_graph_says_which()
    {
        // The reason it is a node with edges rather than values inlined at each site: two fields drawing
        // from one registry is one fact, and a copy at each of them is two facts that can disagree.
        var shared = Priorities();

        var message = new MessageDef
        {
            Id = "probe",
            Context = Context.Given.These("wanted", "granted"),
            Fields =
            [
                new Field { Id = "wanted", Pattern = U8, Value = Expr.Parse("inputs.wanted"), Draws = (shared, null) },
                new Field { Id = "granted", Pattern = U8, Value = Expr.Parse("inputs.granted"), Draws = (shared, null) },
            ],
        };

        Assert.AreEqual(0, message.Validate().Count, string.Join("\n", message.Validate()));

        // Through the checker, because a check points at what does the checking and the set is data that
        // checker was handed. Two fields confined to one registry are two checks over one set, not two
        // copies of it — which is the whole reason a set is a node.
        var drawing = message.Graph.Of<Checks>()
                             .Where(e => message.DrawnFrom(e.From)?.Set is { } set
                                      && ReferenceEquals(set, shared))
                             .ToList();

        CollectionAssert.AreEquivalent(new[] { "wanted", "granted" },
            drawing.Select(e => e.From.Name).ToArray());

        Assert.AreEqual(2, message.Graph.To<Requires>(shared).Count(), "one set, reached twice");
    }
}
