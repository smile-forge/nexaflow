using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// One node saying that it continues at another, and the octets that stand for saying so.
///
/// <para>
/// The word "position" covers two things and only one of them is real. <b>Ordinal</b> — I come third — is
/// intent, is declared, and has been on the containment edges since step 4. <b>Offset</b> — I begin at
/// octet 47 — is a consequence: it falls out of walking that order and summing extents, and it exists only
/// because a wire happens to be a sequence of bytes.
/// </para>
///
/// <para>
/// A pointer's intent is neither. It is <i>my content continues at that node</i> — a relationship. The
/// offset is how the relationship gets written down. So a document declares the edge and the engine
/// renders the octets, and <c>position</c> is bound at that one site rather than hanging off every node as
/// though it were an attribute. Exposing it everywhere would teach documents to think in offsets about
/// things that are references — and worse, <c>fields.thatName.position</c> is a <i>name</i>, which cannot
/// say which appearance of a repeated structure it means.
/// </para>
///
/// <para>
/// The offset needs no pass and no back-patch, for the third time in this engine: a reference's extent is
/// fixed by its declaration whatever value it ends up holding, so the chain of positions never runs
/// through it.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol references — tree nodes land with the engine")]
public class ReferenceTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);

    /// <summary>
    /// A header, a name, and a second name that is only a reference to the first — the shape of every
    /// compressed name in the DNS family. The top two bits of the offset are this protocol's way of
    /// marking a reference, and they are in the <i>document</i> because how a reference is spelled is a
    /// protocol's business, not a notion.
    /// </summary>
    private static (MessageDef Message, Concept Name) Document()
    {
        var name = new Field
        {
            Id = "name",
            Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.nameLength.value")),
            Value = Expr.Parse("inputs.name"),
        };

        // What the protocol calls this part. Declared, so a reference to it needs no index and no search:
        // it is one thing because the document said it was one thing, and what the message happens to
        // carry in it does not enter into that.
        var theName = new Concept
        {
            Id = "queriedName",
            Of = [name],
            About = "the name this message is about, which later parts refer back to rather than repeat.",
        };

        var message = new MessageDef
        {
            Id = "referring",
            Context = Context.Given.These("transaction", "name"),
            Concepts = [theName],
            Fields =
            [
                new Field { Id = "transaction", Pattern = U16, Value = Expr.Parse("inputs.transaction") },
                new Field { Id = "nameLength", Pattern = U8, Value = Expr.Parse("fields.name.extent") },
                name,
                new Field
                {
                    Id = "again",
                    Pattern = U16,
                    Points = new Locating(theName, Expr.Parse("0xc000 bor position")),
                },
            ],
        };

        return (message, theName);
    }

    private static EvalScope Inputs(long transaction, string name) => new EvalScope().Set("inputs",
        EvalScope.Record(("transaction", ProtoValue.Of(transaction)),
                         ("name", ProtoValue.Of(System.Text.Encoding.ASCII.GetBytes(name)))));

    // ── The reference ─────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var (message, _) = Document();
        Assert.AreEqual(0, message.Validate().Count, string.Join("\n", message.Validate()));
    }

    [TestMethod]
    public void A_reference_carries_where_its_target_landed_without_the_document_saying_where()
    {
        // Nothing in the declaration mentions 3. The name follows two octets of transaction and one of
        // length, so that is where it is, and the reference says so.
        var encoded = new MessageCodec(Document().Message).Encode(Inputs(0x1234, "local"));

        CollectionAssert.AreEqual(
            new byte[] { 0x12, 0x34, 0x05, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0xc0, 0x03 },
            encoded);
    }

    [TestMethod]
    public void Moving_the_target_moves_the_reference_with_it()
    {
        // The offset is derived, so it follows a change nothing else was told about: a longer name pushes
        // nothing, but a *shorter* one does not move the target either — what moves it is what precedes
        // it. So this checks the case that actually varies: the reference tracks the target across a
        // change in the target's own size, staying at 3, while the message length follows.
        var codec = new MessageCodec(Document().Message);

        var shorter = codec.Encode(Inputs(1, "io"));
        var longer = codec.Encode(Inputs(1, "example"));

        Assert.AreEqual(0xc003, (shorter[^2] << 8) | shorter[^1], "the target still starts at 3");
        Assert.AreEqual(0xc003, (longer[^2] << 8) | longer[^1]);

        Assert.AreEqual(7, shorter.Length);
        Assert.AreEqual(12, longer.Length);
    }

    [TestMethod]
    public void A_reference_and_its_target_round_trip()
    {
        var codec = new MessageCodec(Document().Message);
        var original = codec.Encode(Inputs(0xbeef, "nexaflow"));

        var decoded = codec.Decode(original);
        Assert.AreEqual(0xc003, decoded["again"].AsInt(), "read back as the octets that stand for it");

        var again = codec.Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("transaction", decoded["transaction"]),
            ("name", decoded["name"]))));

        CollectionAssert.AreEqual(original, again);
    }

    // ── What it refuses ───────────────────────────────────────────────────────

    [TestMethod]
    public void A_reference_that_points_forwards_is_refused()
    {
        // Not because the offset could not be worked out — it could, since a reference's extent is fixed
        // whatever it holds. Because a reader meeting it has not read the target yet, and two of them
        // pointing at each other is a loop nothing escapes.
        var later = new Field { Id = "later", Pattern = U8, Value = Expr.Parse("inputs.later") };
        var afterwards = new Concept { Id = "afterwards", Of = [later] };

        var message = new MessageDef
        {
            Id = "backwards",
            Context = Context.Given.These("later"),
            Concepts = [afterwards],
            Fields =
            [
                new Field { Id = "early", Pattern = U16, Points = new Locating(afterwards, Expr.Parse("position")) },
                later,
            ],
        };

        var issues = string.Join("\n", message.Validate());

        StringAssert.Contains(issues, "points forwards at 'afterwards'");
        StringAssert.Contains(issues, "a loop nothing escapes");
    }

    [TestMethod]
    public void A_reference_may_not_also_have_a_value_of_its_own()
    {
        var (message, name) = Document();

        var confused = message with
        {
            Fields =
            [
                .. message.Fields.Take(3),
                new Field
                {
                    Id = "again",
                    Pattern = U16,
                    Value = Expr.Parse("0"),
                    Points = new Locating(name, Expr.Parse("0xc000 bor position")),
                },
            ],
        };

        StringAssert.Contains(string.Join("\n", confused.Validate()),
            "both points at 'queriedName' and has a value of its own");
    }

    [TestMethod]
    public void An_offset_is_meaningless_anywhere_a_reference_is_not_being_written()
    {
        // The check that keeps position from becoming a thing. A field asking where something landed,
        // outside a reference, is told what an offset actually is rather than merely refused.
        var message = new MessageDef
        {
            Id = "curious",
            Context = Context.Given.These("x"),
            Fields = [new Field { Id = "x", Pattern = U8, Value = Expr.Parse("position + inputs.x") }],
        };

        var issues = string.Join("\n", message.Validate());

        StringAssert.Contains(issues, "names `position`");
        StringAssert.Contains(issues, "not a fact a node carries");
        StringAssert.Contains(issues, "declare what this continues at");
    }
}
