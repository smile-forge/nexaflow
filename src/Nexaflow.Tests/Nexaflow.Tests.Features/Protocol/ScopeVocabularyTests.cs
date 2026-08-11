using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Which of the walk's names can answer a question, and where.
///
/// <para>
/// Five defects in a row were one defect: a name bound at one scope-construction site and not at another.
/// <c>carried</c> answered a discriminator and not a field expression. <c>room</c> was documented as usable
/// on the way out and is only ever bound on the way in. A field's value settled as the octets when writing
/// and as the value when reading. Bit runs were flat names via captures on one side and via a record
/// expansion on the other, and neither carried an extent. A frame-blind lookup could hand one chain
/// instance another instance's length.
/// </para>
///
/// <para>
/// None of the five failed loudly. An unbound root reads as nothing and every comparison against it goes
/// quietly false, so each was found by whichever capture first happened to need it. So availability is now
/// <b>data</b> — see <see cref="Vocabulary"/> — and it is checked at document time, which is what these
/// tests are about. The engine having one builder per direction is what makes the table true; the table
/// being checked is what stops the sixth.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol expression-scope rules — tree nodes land with the engine")]
public class ScopeVocabularyTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);

    private static MessageDef Probe(IReadOnlyList<Field> fields,
                                    IReadOnlyList<string>? context = null,
                                    IReadOnlyList<Rule>? rules = null) => new()
    {
        Id = "probe",
        Context = context is null ? [] : Context.Given.These([.. context]),
        Fields = fields,
        Rules = rules ?? [],
    };

    private static string Issues(MessageDef message) => string.Join("\n", message.Validate());

    // ── The table, checked ────────────────────────────────────────────────────

    [TestMethod]
    public void A_value_may_not_ask_how_much_room_is_left()
    {
        // The one the engine's own documentation got wrong: `room` reads as an answerable question because
        // a comparison's cover is provable, and it is not answerable at all while encoding.
        var issues = Issues(Probe([new Field { Id = "x", Pattern = U8, Value = Expr.Parse("room") }]));

        StringAssert.Contains(issues, "names `room`");
        StringAssert.Contains(issues, "octets that have not been written");
    }

    [TestMethod]
    public void A_length_may_not_ask_what_the_caller_supplied()
    {
        // The mirror image, and the reason the asymmetry is a property of the site: a span's length is
        // recovered on the way in, where there is no caller to have supplied anything.
        var issues = Issues(Probe(
        [
            new Field
            {
                Id = "body",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("inputs.width")),
                Value = Expr.Parse("inputs.body"),
            },
        ], ["width", "body"]));

        StringAssert.Contains(issues, "names `inputs`");
        StringAssert.Contains(issues, "only a packet");
    }

    [TestMethod]
    public void A_discriminator_may_use_only_what_both_directions_can_answer()
    {
        // A choice key runs in both directions, so it may name neither the reader's vocabulary nor the
        // writer's — and the diagnostic says what to reach for instead rather than only refusing.
        var issues = Issues(Probe(
        [
            new Field
            {
                Id = "pick",
                Pattern = new Pattern.Choice(Expr.Parse("peek band 0x80"),
                [
                    new Arm("low", 0, []),
                    new Arm("high", 0x80, []),
                ]),
            },
        ]));

        StringAssert.Contains(issues, "names `peek`");
        StringAssert.Contains(issues, "evaluated in both directions");
    }

    [TestMethod]
    public void A_root_the_walk_never_binds_is_refused_rather_than_read_as_nothing()
    {
        // The failure mode the whole table exists for: a misspelling evaluates to nothing, and nothing
        // compares false against everything.
        var issues = Issues(Probe(
            [new Field { Id = "x", Pattern = U8, Value = Expr.Parse("feilds.y.value") }]));

        StringAssert.Contains(issues, "not part of the walk's vocabulary");
        StringAssert.Contains(issues, "makes every comparison against it false");
    }

    [TestMethod]
    public void A_carry_is_read_in_the_structure_it_runs_in_and_not_the_one_it_is_written_beside()
    {
        // A carry is declared on the chain and evaluated inside the element, so it names the element's
        // fields. Until every expression was enumerated for the vocabulary check, a carry was the one
        // expression in the language that was never scope-checked at all.
        var element = new Field { Id = "entry", Pattern = U8, Value = Expr.Parse("item") };

        var good = Probe(
        [
            new Field
            {
                Id = "entries",
                Value = Expr.Parse("inputs.entries"),
                Pattern = new Pattern.Chain(element, Expr.Parse("room > 0"),
                                            Seed: Expr.Parse("0"),
                                            Carry: Expr.Parse("carried + fields.entry.value")),
            },
        ], ["entries"]);

        Assert.AreEqual(0, good.Validate().Count, string.Join("\n", good.Validate()));

        var bad = Probe(
        [
            new Field
            {
                Id = "entries",
                Value = Expr.Parse("inputs.entries"),
                Pattern = new Pattern.Chain(element, Expr.Parse("room > 0"),
                                            Seed: Expr.Parse("0"),
                                            Carry: Expr.Parse("carried + fields.absent.value")),
            },
        ], ["entries"]);

        StringAssert.Contains(Issues(bad), "not a field of the structure it runs in");
    }

    // ── The bindings the table promises ───────────────────────────────────────

    [TestMethod]
    public void A_message_rule_can_read_an_extent_on_the_way_in_as_well_as_out()
    {
        // Message rules were handed raw captures when decoding and settled facet records when encoding,
        // through one signature that took both — so `.extent` answered on the way out and was nothing on
        // the way in, and a rule about a span's size passed every packet.
        Field Body() => new()
        {
            Id = "body",
            Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.count.value")),
            Value = Expr.Parse("inputs.body"),
        };

        MessageDef Sized(int expected)
        {
            var body = Body();

            return new MessageDef
            {
                Id = "probe",
                Context = Context.Given.These("count", "body"),
                Fields = [new Field { Id = "count", Pattern = U8, Value = Expr.Parse("inputs.count") }, body],
                Rules =
                [
                    new Rule.Requires
                    {
                        Within = body,
                        When = Expr.Parse("fields.count.value > 0"),
                        Then = Expr.Parse($"fields.body.extent == {expected}"),
                        Because = "the count and the span have to agree.",
                    },
                ],
            };
        }

        byte[] capture = [0x03, 0xaa, 0xbb, 0xcc];

        Assert.AreEqual(3, new MessageCodec(Sized(3)).Decode(capture)["body"].AsBytes().Length);

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Sized(4)).Decode(capture));
        StringAssert.Contains(ex.Message, "the count and the span have to agree");
    }

    [TestMethod]
    public void A_rule_inside_a_structure_knows_which_structure_it_is_on_the_way_in()
    {
        // `ordinal` was bound for the continuation that decides whether to start a structure and not for
        // the structure itself, so "the first one is different" was sayable while encoding and not while
        // decoding. It is bound for the whole structure now, in both directions.
        var element = new Field { Id = "entry", Pattern = U8, Value = Expr.Parse("item") };

        var message = new MessageDef
        {
            Id = "probe",
            Context = Context.Given.These("entries"),
            Fields =
            [
                new Field
                {
                    Id = "entries",
                    Value = Expr.Parse("inputs.entries"),
                    Pattern = new Pattern.Chain(element, Expr.Parse("room > 0")),
                },
            ],
            Rules =
            [
                new Rule.Invariant
                {
                    Within = element,
                    Must = Expr.Parse("ordinal == 0"),
                    Because = "this document accepts exactly one entry.",
                },
            ],
        };

        Assert.AreEqual(0, message.Validate().Count, string.Join("\n", message.Validate()));

        var codec = new MessageCodec(message);
        Assert.AreEqual(1, codec.Decode([0x07])["entries"].AsList().Count);

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Decode([0x07, 0x08]));
        StringAssert.Contains(ex.Message, "accepts exactly one entry");
    }
}
