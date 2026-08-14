using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A carried protocol as a place on the path, walked like any other.
///
/// <para>
/// It used to dangle. A span stood on the path and the node saying what was inside it hung off that span
/// by an edge, so the walk reached an opaque run of octets and something else had to know to look in it.
/// A carrier now stands <i>where the inner protocol is included</i> — exactly as a set stands where its
/// members are — and it produces what the inner message produces.
/// </para>
///
/// <para>
/// What that buys is that nothing in the walk is about layering. The carrier has a position, an extent and
/// a value like anything else; a length measures it the way a length measures a span; and reading it is
/// being told how far it runs and then handing those octets to the protocol beneath. There is no layer
/// step, because a layer is not a step.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol layering through the graph walk — engine structure")]
public class CarriedLayerTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);

    /// <summary>The inner protocol. It knows nothing about being carried, which is the whole point — it
    /// asks for what it asks for, in its own words.</summary>
    private static MessageDef Note() => new()
    {
        Id = "note",
        Context = Context.Given.These("tag", "size"),
        Fields =
        [
            new Field { Id = "tag", Pattern = U8, Value = Expr.Parse("inputs.tag") },
            new Field { Id = "size", Pattern = U8, Value = Expr.Parse("inputs.size") },
        ],
    };

    /// <summary>The seam, saying what the protocol beneath is given in the outer document's terms.</summary>
    private static Subprotocol Inner => new()
    {
        Id = "payload",
        Carries = new Carriage.Described(Note()),
        Feeds = new Dictionary<string, Expr>
        {
            ["tag"] = Expr.Parse("fields.marker.value"),
            ["size"] = Expr.Parse("0x09"),
        },
    };

    /// <summary>An outer message: a marker, a length, the carried protocol, a trailer.</summary>
    private static MessageDef Outer() => new()
    {
        Id = "outer",
        Fields =
        [
            new Field { Id = "marker", Pattern = U8, Value = Expr.Parse("0x03") },
            new Field { Id = "length", Pattern = U8, Value = Expr.Parse("fields.body.extent") },
            new Field
            {
                Id = "body",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.length.value")),
                Carries = Inner,
            },
            new Field { Id = "trailer", Pattern = U8, Value = Expr.Parse("0x7f") },
        ],
    };

    private static byte[] Written() => new GraphCodec(Outer().Graph).Encode(new Dictionary<string, ProtoValue>());

    // ── Where it stands ───────────────────────────────────────────────────────

    [TestMethod]
    public void The_carrier_is_the_place_on_the_path_rather_than_something_hanging_off_one()
    {
        var outer = Outer();
        var path = outer.Walk(outer.Arrangements.Single()).ToList();

        Assert.IsTrue(path.OfType<Subprotocol>().Any(), "the carrier is somewhere on the walk");
        Assert.IsFalse(path.OfType<Field>().Any(f => f.Id == "body"),
                       "and the span it replaced is not also on it — one thing, one node");
    }

    // ── Both directions ───────────────────────────────────────────────────────

    [TestMethod]
    public void What_the_inner_protocol_makes_is_what_the_outer_message_carries()
    {
        CollectionAssert.AreEqual(new byte[] { 0x03, 0x02, 0x03, 0x09, 0x7f }, Written());
    }

    [TestMethod]
    public void A_length_measures_the_carrier_the_way_it_measures_anything()
    {
        // The reason the carrier had to replace the span rather than sit beside it: the extent lands on
        // whichever node the walk reached, and a length reaching for the other one would find nothing.
        Assert.AreEqual(0x02, Written()[1], "two octets of note");
    }

    [TestMethod]
    public void Reading_hands_the_octets_it_was_told_about_to_the_protocol_beneath()
    {
        var run = new GraphCodec(Outer().Graph).Decode(Written());
        var carried = run.Nodes.Single(n => n.Of is Subprotocol);

        Assert.AreEqual(2, carried.Settled(Facet.Position), "after the marker and the length");
        Assert.AreEqual(2, carried.Settled(Facet.Extent));

        var inside = (ProtoValue.Rec)carried.Value;

        Assert.AreEqual(3L, inside.Members["tag"].AsInt());
        Assert.AreEqual(9L, inside.Members["size"].AsInt());
    }

    [TestMethod]
    public void The_trailer_after_a_layer_lands_where_the_layer_left_off()
    {
        // The layer consumed exactly what it was told to, so what follows is not off by however much the
        // inner protocol happened to want.
        var run = new GraphCodec(Outer().Graph).Decode(Written());
        var trailer = run.Nodes.Single(n => n.Of is Field { Id: "trailer" });

        Assert.AreEqual(4, trailer.Settled(Facet.Position));
        Assert.AreEqual(0x7fL, trailer.Value.AsInt());
    }

    // ── The seam ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A carrier that does not say what feeds the protocol beneath is refused, naming what is unfed.
    /// </summary>
    /// <remarks>
    /// The shortcut this exists instead of: hand the inner run whatever outside values the outer run holds
    /// and match them by name. That works wherever two independently-written documents agree on a spelling
    /// and is silently wrong everywhere else — one protocol feeding another by coincidence, with no edge
    /// saying so and nothing able to check it. The inner document cannot know what the outer one calls
    /// things, so naming the correspondence is the carrier's job and there is nowhere else it could go.
    /// </remarks>
    [TestMethod]
    public void A_carrier_that_does_not_feed_what_it_carries_is_refused_by_name()
    {
        var hungry = new MessageDef
        {
            Id = "outer",
            Fields =
            [
                new Field
                {
                    Id = "body",
                    Pattern = new Pattern.Opaque(1),
                    Carries = new Subprotocol
                    {
                        Id = "payload",
                        Carries = new Carriage.Described(new MessageDef
                        {
                            Id = "asks",
                            Context = Context.Given.These("who"),
                            Fields = [new Field { Id = "w", Pattern = U8, Value = Expr.Parse("inputs.who") }],
                        }),
                    },
                },
            ],
        };

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(hungry.Graph).Encode(new Dictionary<string, ProtoValue>()));

        StringAssert.Contains(refused.Message, "who");
        StringAssert.Contains(refused.Message, "does not say what feeds that");
    }

    [TestMethod]
    public void An_implementation_the_host_did_not_offer_is_refused_by_name()
    {
        var unprovided = new MessageDef
        {
            Id = "stacked",
            Fields =
            [
                new Field
                {
                    Id = "body",
                    Pattern = new Pattern.Opaque(2),
                    Carries = new Subprotocol { Id = "tls", Carries = new Carriage.Provided("tls1.3") },
                },
            ],
        };

        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => new GraphCodec(unprovided.Graph).Encode(new Dictionary<string, ProtoValue>()));

        StringAssert.Contains(refused.Message, "tls1.3");
        StringAssert.Contains(refused.Message, "does not provide");
    }
}
