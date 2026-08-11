using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// One protocol carried inside another, through a node that sits between them.
///
/// <para>
/// The tempting shape is a region naming an inner document directly. Three things have nowhere to live
/// under it. A protocol can carry the same inner one in <b>more than one place</b> — here a header and two
/// separate parts of a body — and those are different carriers with different rules, which one shared
/// reference cannot tell apart. The octets may not go in verbatim, and the transform belongs on the seam
/// rather than inside either protocol. And the seam is where a <b>known-good implementation</b> can stand
/// in for the description, which is only possible because there is something in between to swap.
/// </para>
///
/// <para>
/// That last one is the point of the whole arrangement. Almost none of what a device conversation sits on
/// is interesting — nobody wants a drafted description of TLS, and a wrong one is worse than none. A
/// document should describe what is actually novel and stack it on things already trusted.
/// </para>
///
/// <para>
/// It also settles what an offset means across a layer, without a rule saying so: an inner message is
/// built whole and then embedded, so positions inside it are relative to its own beginning because there
/// is nothing else for them to be relative to.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol layering — tree nodes land with the engine")]
public class LayerTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);

    /// <summary>The inner protocol: a tag and a counted run of text. Small on purpose — what matters is
    /// that it knows nothing about being carried.</summary>
    private static MessageDef Note() => new()
    {
        Id = "note",
        Context = Context.Given.These("tag", "text"),
        Fields =
        [
            new Field { Id = "tag", Pattern = U8, Value = Expr.Parse("inputs.tag") },
            new Field { Id = "textLength", Pattern = U8, Value = Expr.Parse("fields.text.extent") },
            new Field
            {
                Id = "text",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.textLength.value")),
                Value = Expr.Parse("inputs.text"),
                Via = "unascii",
            },
        ],
    };

    /// <summary>A span that carries something, measured by the octet before it.</summary>
    private static Field[] Carrying(string id, Subprotocol layer, string source) =>
    [
        new Field { Id = $"{id}Length", Pattern = U8, Value = Expr.Parse($"fields.{id}.extent") },
        new Field
        {
            Id = id,
            Pattern = Pattern.Opaque.Measured(Expr.Parse($"fields.{id}Length.value")),
            Value = Expr.Parse(source),
            Carries = layer,
        },
    ];

    /// <summary>
    /// An outer protocol carrying the same inner one three times: once in the header and twice in the
    /// body, with the second body part wrapped on the way in. Three carriers, one description.
    /// </summary>
    private static MessageDef Envelope()
    {
        var note = Note();

        return new MessageDef
        {
            Id = "envelope",
            Context = Context.Given.These("version", "greeting", "first", "second"),
            Fields =
            [
                new Field { Id = "version", Pattern = U16, Value = Expr.Parse("inputs.version") },

                .. Carrying("greeting",
                    new Subprotocol { Id = "theGreeting", Carries = new Carriage.Described(note),
                                      About = "the note that introduces the envelope." },
                    "inputs.greeting"),

                .. Carrying("first",
                    new Subprotocol { Id = "theFirstPart", Carries = new Carriage.Described(note),
                                      About = "the first note of the body." },
                    "inputs.first"),

                // The same inner protocol again, and this one is obfuscated on the way in. The transform
                // is on the seam because it belongs to neither protocol: the note does not know it is
                // being hidden, and the envelope does not know what it is hiding.
                .. Carrying("second",
                    new Subprotocol
                    {
                        Id = "theSecondPart",
                        Carries = new Carriage.Described(note),
                        Via = [new Conversion("xor", [ProtoValue.Of(new byte[] { 0x5a })])],
                        About = "the second note of the body, masked so a scanner does not read it.",
                    },
                    "inputs.second"),
            ],
        };
    }

    private static ProtoValue Noted(long tag, string text)
        => EvalScope.Record(("tag", ProtoValue.Of(tag)), ("text", ProtoValue.Of(text)));

    private static EvalScope Inputs() => new EvalScope().Set("inputs", EvalScope.Record(
        ("version", ProtoValue.Of(1L)),
        ("greeting", Noted(1, "hello")),
        ("first", Noted(2, "one")),
        ("second", Noted(3, "two"))));

    // ── Carrying ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Both_documents_validate()
    {
        foreach (var message in (MessageDef[])[Note(), Envelope()])
        {
            var issues = new MessageCodec(message).Validate();
            Assert.AreEqual(0, issues.Count, $"{message.Id}:\n" + string.Join("\n", issues));
        }
    }

    [TestMethod]
    public void One_inner_protocol_is_carried_in_three_places_and_they_stay_apart()
    {
        var codec = new MessageCodec(Envelope());
        var octets = codec.Encode(Inputs());

        // version(2) + each note as length(1) + tag(1) + textLength(1) + text
        Assert.AreEqual(2 + (1 + 7) + (1 + 5) + (1 + 5), octets.Length);

        var decoded = codec.Decode(octets);

        Assert.AreEqual("hello", ((ProtoValue.Rec)decoded["greeting"]).Members["text"].AsText());
        Assert.AreEqual("one", ((ProtoValue.Rec)decoded["first"]).Members["text"].AsText());
        Assert.AreEqual("two", ((ProtoValue.Rec)decoded["second"]).Members["text"].AsText());

        // Three carriers of one description — which a region naming the document directly could not
        // distinguish, and which the graph answers because each is its own node.
        CollectionAssert.AreEqual(new[] { "theGreeting", "theFirstPart", "theSecondPart" },
            Envelope().Layers.Select(l => l.Id).ToArray());
    }

    [TestMethod]
    public void A_carried_payload_can_be_changed_on_its_way_in_and_back_on_its_way_out()
    {
        var codec = new MessageCodec(Envelope());
        var octets = codec.Encode(Inputs());

        // The first body part goes in as it is; the second is masked. Same protocol, same values but for
        // the tag, and the octets differ by more than the tag does.
        int firstAt = 2 + 8 + 1;
        int secondAt = firstAt + 6 + 1;

        Assert.AreEqual(0x02, octets[firstAt], "the first note's tag, in the clear");
        Assert.AreEqual(0x03 ^ 0x5a, octets[secondAt], "and the second's, masked");

        Assert.AreEqual("two", ((ProtoValue.Rec)codec.Decode(octets)["second"]).Members["text"].AsText(),
            "and it comes back, because the wrapping is undone in reverse");
    }

    [TestMethod]
    public void The_whole_thing_round_trips()
    {
        var codec = new MessageCodec(Envelope());
        var original = codec.Encode(Inputs());

        var decoded = codec.Decode(original);

        ProtoValue Part(string id)
        {
            var members = ((ProtoValue.Rec)decoded[id]).Members;
            return EvalScope.Record(("tag", members["tag"]), ("text", members["text"]));
        }

        var again = codec.Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("version", decoded["version"]),
            ("greeting", Part("greeting")),
            ("first", Part("first")),
            ("second", Part("second")))));

        CollectionAssert.AreEqual(original, again);
    }

    // ── The seam ──────────────────────────────────────────────────────────────

    /// <summary>Something the host stands behind. Trivial here; the point is that the engine does not know
    /// or care what is inside it.</summary>
    private sealed class Sealed : ISubprotocol
    {
        public string Name => "sealedNote";

        public byte[] Encode(ProtoValue inputs)
        {
            var text = ((ProtoValue.Rec)inputs).Members["text"].AsText();
            return [(byte)text.Length, .. text.Select(c => (byte)(c + 1))];
        }

        public ProtoValue Decode(ReadOnlySpan<byte> octets)
            => EvalScope.Record(("text",
                ProtoValue.Of(new string([.. octets[1..].ToArray().Select(b => (char)(b - 1))]))));
    }

    private static MessageDef Stacked() => new()
    {
        Id = "stacked",
        Context = Context.Given.These("version", "payload"),
        Fields =
        [
            new Field { Id = "version", Pattern = U16, Value = Expr.Parse("inputs.version") },

            .. Carrying("payload",
                new Subprotocol
                {
                    Id = "theSealedPart",
                    Carries = new Carriage.Provided("sealedNote"),
                    About = "handled by something already trusted, not described here.",
                },
                "inputs.payload"),
        ],
    };

    [TestMethod]
    public void A_layer_can_be_something_the_host_provides_rather_than_something_described()
    {
        // The reason the carrier is a node. A document describes the part that is novel and stacks it on
        // an implementation somebody already trusts, and nothing about the outer protocol changes.
        var codec = new MessageCodec(Stacked(), provided: new Implementations().Offer(new Sealed()));

        var octets = codec.Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("version", ProtoValue.Of(7L)),
            ("payload", EvalScope.Record(("text", ProtoValue.Of("abc")))))));

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x07, 0x04, 0x03, (byte)'b', (byte)'c', (byte)'d' }, octets);

        Assert.AreEqual("abc", ((ProtoValue.Rec)codec.Decode(octets)["payload"]).Members["text"].AsText());

        // And the graph says it is a layer without saying it is a document, because it is not one.
        var layer = Stacked().Layers.Single();
        Assert.IsInstanceOfType<Carriage.Provided>(layer.Carries);
        Assert.AreEqual(0, Stacked().Graph.From<Speaks>(layer).Count());
    }

    [TestMethod]
    public void An_implementation_the_host_did_not_offer_is_refused_rather_than_found()
    {
        // A document sitting under an unreviewed draft is the last place for an engine that resolves a
        // name to whatever it can find.
        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => new MessageCodec(Stacked()).Encode(new EvalScope().Set("inputs", EvalScope.Record(
                ("version", ProtoValue.Of(7L)),
                ("payload", EvalScope.Record(("text", ProtoValue.Of("abc"))))))));

        StringAssert.Contains(ex.Message, "which this host does not provide");
        StringAssert.Contains(ex.Message, "Offered: nothing");
    }
}
