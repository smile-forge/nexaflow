using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The second real capture set through the whole stack, chosen for the three things the first one could
/// not reach: a <b>length that measures a region declared after it</b>, a <b>discriminated packing</b>, and
/// a <b>repetition whose count comes from the data</b>.
///
/// <para>
/// The protocol is Modbus TCP. Its request and both of its responses share a six-octet header whose third
/// field counts everything that follows, and one request has two structurally different answers: a success
/// carrying a byte count and a register block, or an exception carrying a single code, distinguished only
/// by the top bit of the function code. Nothing about that is Modbus-specific as a <i>shape</i> — which is
/// the claim under test.
/// </para>
///
/// <para>
/// Three properties are asserted that the previous capture set could not distinguish: that the length is
/// <b>derived</b> rather than echoed (it is not supplied on encode at all), that the two response shapes
/// come from <b>one declaration</b>, and that the arms are proved exhaustive at validate time rather than
/// trusted.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class FramedChoiceCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);

    /// <summary>
    /// The shared header, with the framer.
    ///
    /// <para>
    /// <c>length</c> reads <c>fields.body.extent</c> — a region declared three fields later, whose own size
    /// depends on a choice that has not been made yet and a repetition that has not expanded yet. There is
    /// no placeholder and no second pass: the resolver schedules it once everything it transitively needs
    /// has settled.
    /// </para>
    ///
    /// <para>
    /// Note there is no separate framing declaration beside the field list. A frame prefix <i>is</i> a
    /// length over a region; giving it a second home is what leaves two descriptions of the same two octets
    /// with nothing to say which one owns them.
    /// </para>
    /// </summary>
    private static MessageDef Framed(string id, params Field[] body) => new()
    {
        Id = id,
        Fields =
        [
            new Field { Id = "transactionId", Pattern = U16, Value = Expr.Parse("inputs.transactionId") },
            new Field { Id = "protocolId",    Pattern = U16, Value = Expr.Parse("inputs.protocolId") },
            new Field { Id = "length",        Pattern = U16, Value = Expr.Parse("fields.body.extent") },

            new Field
            {
                Id = "body",
                Pattern = new Pattern.Group(
                [
                    new Field { Id = "unitId", Pattern = U8, Value = Expr.Parse("inputs.unitId") },
                    .. body,
                ]),
            },
        ],
    };

    internal static MessageDef Request() => Framed("readRequest",
        new Field { Id = "functionCode",    Pattern = U8,  Value = Expr.Parse("inputs.functionCode") },
        new Field { Id = "startingAddress", Pattern = U16, Value = Expr.Parse("inputs.startingAddress") },
        new Field { Id = "quantity",        Pattern = U16, Value = Expr.Parse("inputs.quantity") });

    /// <summary>
    /// One declaration covering both answers.
    ///
    /// <para>
    /// The discriminator is a masked bit, and it is written once: on decode it is the octet just read, on
    /// encode the octet about to be written. Both directions evaluate the same expression, so there is no
    /// decode guard paired with a separate encode-side variant selector to keep in agreement.
    /// </para>
    ///
    /// <para>
    /// The register block shows the one asymmetry that is real rather than incidental. On encode the count
    /// is the length of the collection; on decode it has to be recovered, and <c>byteCount</c> is where it
    /// was written down. So <c>byteCount</c> reads <c>fields.registers.extent</c> and the repetition reads
    /// <c>fields.byteCount.value</c> — pointing at each other in opposite directions, which is a cycle only
    /// if you insist both directions derive the same fact the same way.
    /// </para>
    /// </summary>
    internal static MessageDef Response() => Framed("readResponse",
        new Field { Id = "functionCode", Pattern = U8, Value = Expr.Parse("inputs.functionCode") },
        new Field
        {
            Id = "pdu",
            Pattern = new Pattern.Choice(Expr.Parse("fields.functionCode.value band 0x80"),
            [
                new Arm("ok", 0x00,
                [
                    new Field { Id = "byteCount", Pattern = U8, Value = Expr.Parse("fields.registers.extent") },
                    new Field
                    {
                        Id = "registers",
                        Pattern = new Pattern.Chain(
                            new Field { Id = "register", Pattern = U16, Value = Expr.Parse("item") },
                            Expr.Parse("ordinal < fields.byteCount.value / 2")),
                        Value = Expr.Parse("inputs.registers"),
                    },
                ]),

                new Arm("exception", 0x80,
                [
                    new Field { Id = "exceptionCode", Pattern = U8, Value = Expr.Parse("inputs.exceptionCode") },
                ]),
            ]),
        });

    private static byte[] Capture(int index) => ProtocolCorpus.Get("modbus").Captures[index].Bytes;

    // ── The documents hold up ──────────────────────────────────────────────────

    [TestMethod]
    public void Both_documents_validate()
    {
        foreach (var message in (MessageDef[])[Request(), Response()])
        {
            var issues = new MessageCodec(message).Validate();
            Assert.AreEqual(0, issues.Count, $"{message.Id}:\n" + string.Join("\n", issues));
        }
    }

    // ── Decode ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_request_decodes_to_the_fields_the_corpus_lists()
    {
        var decoded = new MessageCodec(Request()).Decode(Capture(0));

        Assert.AreEqual(1, decoded["transactionId"].AsInt());
        Assert.AreEqual(0, decoded["protocolId"].AsInt());
        Assert.AreEqual(6, decoded["length"].AsInt(), "counts the octets after itself");
        Assert.AreEqual(17, decoded["unitId"].AsInt());
        Assert.AreEqual(3, decoded["functionCode"].AsInt());
        Assert.AreEqual(107, decoded["startingAddress"].AsInt());
        Assert.AreEqual(3, decoded["quantity"].AsInt());
    }

    [TestMethod]
    public void The_success_response_takes_the_first_arm_and_its_repetition_is_sized_by_the_data()
    {
        var decoded = new MessageCodec(Response()).Decode(Capture(1));

        // The chosen arm is the value of the choice, so a later step branches on which SHAPE arrived
        // rather than re-testing the raw discriminator and hoping the two rules stay in step.
        Assert.AreEqual("ok", decoded["pdu"].AsText());
        Assert.AreEqual(6, decoded["byteCount"].AsInt());

        var registers = decoded["registers"].AsList();
        Assert.AreEqual(3, registers.Count, "byteCount 6 over two-octet elements");
        CollectionAssert.AreEqual(new long[] { 0xae41, 0x5652, 0x4340 },
                                  registers.Select(r => r.AsInt()).ToArray());
    }

    [TestMethod]
    public void The_exception_response_takes_the_other_arm_of_the_same_declaration()
    {
        var decoded = new MessageCodec(Response()).Decode(Capture(2));

        Assert.AreEqual(0x83, decoded["functionCode"].AsInt());
        Assert.AreEqual("exception", decoded["pdu"].AsText());
        Assert.AreEqual(2, decoded["exceptionCode"].AsInt());

        // The success arm's fields are absent rather than zero — a distinction the paired-guard emulation
        // cannot make, because nothing there says the arms are alternatives.
        Assert.IsTrue(decoded["byteCount"].IsNull, "byteCount belongs to the other arm");
        Assert.IsTrue(decoded["registers"].IsNull, "registers belong to the other arm");
    }

    [TestMethod]
    public void Every_octet_of_every_capture_is_accounted_for()
    {
        foreach (var (index, message, total) in (( int, MessageDef, int )[])
                 [(0, Request(), 12), (1, Response(), 15), (2, Response(), 9)])
        {
            var decoded = new MessageCodec(message).Decode(Capture(index));

            var covered = decoded.Spans.Select(s => (s.Offset, s.Length)).Distinct()
                                       .OrderBy(s => s.Offset).ToList();
            int at = 0;
            foreach (var (offset, length) in covered)
            {
                Assert.AreEqual(at, offset, $"capture {index}: spans must be contiguous with no gap");
                at += length;
            }

            Assert.AreEqual(total, at, $"capture {index}: every octet accounted for");
        }
    }

    [TestMethod]
    public void Each_chained_structure_is_named_separately_in_the_breakdown()
    {
        // `register` three times would be three rows nobody can check against a capture, and worse, it
        // would read as one field that moved. These are three distinct entries, and the breakdown says so.
        var decoded = new MessageCodec(Response()).Decode(Capture(1));
        var names = decoded.Spans.Select(s => s.Name).ToList();

        CollectionAssert.IsSubsetOf(new[] { "registers[0]", "registers[1]", "registers[2]" }, names);
    }

    // ── Encode ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void All_three_captures_re_encode_to_the_exact_original_octets()
    {
        // THE test. And the inputs deliberately omit `length` and `byteCount`: if either were echoed from
        // the capture rather than derived, this fails.
        foreach (var (index, message) in ((int, MessageDef)[])[(0, Request()), (1, Response()), (2, Response())])
        {
            var original = Capture(index);
            var codec = new MessageCodec(message);

            var reEncoded = codec.Encode(InputsFrom(codec.Decode(original), except: ["length", "byteCount"]));

            Assert.AreEqual(original.Length, reEncoded.Length, $"capture {index}: length");
            CollectionAssert.AreEqual(original, reEncoded,
                $"capture {index} did not round-trip.\nexpected {Convert.ToHexString(original).ToLowerInvariant()}"
              + $"\nactual   {Convert.ToHexString(reEncoded).ToLowerInvariant()}");
        }
    }

    [TestMethod]
    public void Both_lengths_follow_the_data_rather_than_the_capture_they_came_from()
    {
        // Encode a response the corpus does not contain: two registers instead of three. If either length
        // were carried through rather than measured, the octets below would be the three-register ones.
        var codec = new MessageCodec(Response());
        var inputs = InputsFrom(codec.Decode(Capture(1)), except: ["length", "byteCount"]);

        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("registers", new ProtoValue.List([ProtoValue.Of(0xae41L), ProtoValue.Of(0x5652L)]))));

        var encoded = codec.Encode(inputs);
        var decoded = codec.Decode(encoded);

        Assert.AreEqual(13, encoded.Length);
        Assert.AreEqual(7, decoded["length"].AsInt(), "unitId + functionCode + byteCount + 4 octets of data");
        Assert.AreEqual(4, decoded["byteCount"].AsInt());
        Assert.AreEqual(2, decoded["registers"].AsList().Count);
    }

    [TestMethod]
    public void Encoding_selects_the_arm_from_the_same_expression_that_decoding_reads()
    {
        // Nothing names the arm on the way in. The function code alone decides the shape, in both
        // directions, from one declaration.
        var codec = new MessageCodec(Response());
        var inputs = InputsFrom(codec.Decode(Capture(1)), except: ["length", "byteCount"]);

        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("functionCode", ProtoValue.Of(0x83L)),
            ("exceptionCode", ProtoValue.Of(0x02L))));

        var encoded = codec.Encode(inputs);

        CollectionAssert.AreEqual(Capture(2), encoded,
            "flipping the top bit of the function code must select the other packing: "
          + Convert.ToHexString(encoded).ToLowerInvariant());
    }

    // ── What the engine refuses ───────────────────────────────────────────────

    [TestMethod]
    public void Arms_that_do_not_cover_the_discriminator_are_rejected_before_anything_runs()
    {
        // The defect the paired-guard emulation has and cannot fix: with complementary `when`s, a message
        // carrying an unanticipated discriminator binds no fields and reports no error. Here the mask says
        // the key is 0 or 0x80, so "you have not handled 0x80" is decidable.
        var issues = new MessageCodec(Choosing(new Arm("ok", 0x00, [Leaf("payload")]))).Validate();

        StringAssert.Contains(string.Join("\n", issues), "not exhaustive");
        StringAssert.Contains(string.Join("\n", issues), "0x80");
    }

    [TestMethod]
    public void An_arm_the_discriminator_can_never_select_is_rejected()
    {
        var issues = new MessageCodec(Choosing(
            new Arm("ok", 0x00, [Leaf("a")]),
            new Arm("odd", 0x40, [Leaf("b")]),
            new Arm("exception", 0x80, [Leaf("c")]))).Validate();

        StringAssert.Contains(string.Join("\n", issues), "can never produce");
    }

    [TestMethod]
    public void A_fallback_that_can_never_be_reached_is_rejected()
    {
        // Harmless-looking, and it is how a document comes to carry a branch its author believes is live.
        var issues = new MessageCodec(Choosing(
            new Arm("ok", 0x00, [Leaf("a")]),
            new Arm("exception", 0x80, [Leaf("b")]),
            new Arm("other", null, [Leaf("c")]))).Validate();

        StringAssert.Contains(string.Join("\n", issues), "can never be");
    }

    [TestMethod]
    public void A_discriminator_whose_range_cannot_be_computed_must_declare_a_fallback()
    {
        // No mask, so the engine cannot prove anything about coverage. It says so instead of assuming the
        // author thought of every value.
        var open = new MessageDef
        {
            Id = "open",
            Fields =
            [
                Leaf("tag"),
                new Field
                {
                    Id = "chosen",
                    Pattern = new Pattern.Choice(Expr.Parse("fields.tag.value"),
                        [new Arm("first", 1, [Leaf("payload")])]),
                },
            ],
        };

        StringAssert.Contains(string.Join("\n", new MessageCodec(open).Validate()), "cannot prove");

        // …and with a fallback it validates, decoding an unknown tag into the fallback shape.
        var closed = open with
        {
            Fields =
            [
                Leaf("tag"),
                new Field
                {
                    Id = "chosen",
                    Pattern = new Pattern.Choice(Expr.Parse("fields.tag.value"),
                    [
                        new Arm("first", 1, [Leaf("payload")]),
                        new Arm("unrecognised", null, [Leaf("payload2")]),
                    ]),
                },
            ],
        };

        var codec = new MessageCodec(closed);
        Assert.AreEqual(0, codec.Validate().Count, string.Join("\n", codec.Validate()));
        Assert.AreEqual("unrecognised", codec.Decode(new byte[] { 0x09, 0xff })["chosen"].AsText());
    }

    [TestMethod]
    public void A_count_that_runs_past_the_end_of_the_message_is_an_error_rather_than_a_read_into_whatever_follows()
    {
        // A device with a broken stack sends byteCount 0x08 in a frame holding six octets of data. Read
        // credulously that is four registers, the last two octets of which are the next frame's header.
        var corrupt = (byte[])[.. Capture(1)];
        corrupt[8] = 0x08;

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Response()).Decode(corrupt));
        StringAssert.Contains(ex.Message, "disagree");
    }

    [TestMethod]
    public void A_field_reaching_into_an_arm_that_was_not_selected_fails_as_under_expansion()
    {
        // Ids being flat across arms buys expression convenience and costs this: a reference into a
        // sibling arm passes the document check, because the id genuinely exists. It cannot be caught
        // statically without a scoping rule that would also forbid the useful cases, so what matters is
        // that it fails LOUDLY at the right moment rather than emitting a short, structurally valid,
        // wrong message — the exact defect `Realised` was added for.
        var message = new MessageDef
        {
            Id = "crossArm",
            Fields =
            [
                Leaf("discriminator"),
                new Field
                {
                    Id = "chosen",
                    Pattern = new Pattern.Choice(Expr.Parse("fields.discriminator.value band 0x80"),
                    [
                        new Arm("low", 0x00,
                        [
                            new Field
                            {
                                Id = "borrowed",
                                Pattern = new Pattern.Scalar(1, BigEndian: true),
                                Value = Expr.Parse("fields.onlyInTheOtherArm.value"),
                            },
                        ]),
                        new Arm("high", 0x80, [Leaf("onlyInTheOtherArm")]),
                    ]),
                },
            ],
        };

        var codec = new MessageCodec(message);
        Assert.AreEqual(0, codec.Validate().Count, "the id exists, so the document check cannot see this");

        var scope = new EvalScope().Set("inputs", EvalScope.Record(("discriminator", ProtoValue.Of(0x01L))));
        var ex = Assert.ThrowsExactly<ResolutionException>(() => codec.Encode(scope));

        Assert.AreEqual(ResolutionFailure.Unrealised, ex.Diagnostic.Failure,
            "a reference to a node that never came into existence is under-expansion, not a cycle — "
          + "sending an author to look for a loop that does not exist wastes the diagnostic");
        StringAssert.Contains(ex.Message, "onlyInTheOtherArm");
    }

    [TestMethod]
    public void An_id_reused_inside_an_arm_is_rejected()
    {
        // Ids are flat across regions and arms, because that is what lets an expression name a field from
        // anywhere in the message. It only stays safe while this fires.
        var issues = new MessageCodec(Choosing(
            new Arm("ok", 0x00, [Leaf("discriminator")]),
            new Arm("exception", 0x80, [Leaf("other")]))).Validate();

        StringAssert.Contains(string.Join("\n", issues), "duplicate field id 'discriminator'");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Field Leaf(string id) => new()
    {
        Id = id,
        Pattern = new Pattern.Scalar(1, BigEndian: true),
        Value = Expr.Parse($"inputs.{id}"),
    };

    /// <summary>A one-octet discriminator masked to its top bit, plus whatever arms the case is about.</summary>
    private static MessageDef Choosing(params Arm[] arms) => new()
    {
        Id = "choosing",
        Fields =
        [
            Leaf("discriminator"),
            new Field
            {
                Id = "chosen",
                Pattern = new Pattern.Choice(Expr.Parse("fields.discriminator.value band 0x80"), arms),
            },
        ],
    };

    /// <summary>
    /// Feeds decoded captures back in as <c>inputs.*</c>, minus the ones the document is supposed to
    /// derive. Omitting them is what makes the round trip evidence rather than a restatement.
    /// </summary>
    private static EvalScope InputsFrom(DecodeResult decoded, params string[] except)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        foreach (var (name, value) in decoded.Captures)
            if (!except.Contains(name, StringComparer.Ordinal)) inputs[name] = value;

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }

    private static ProtoValue With(ProtoValue record, params (string Name, ProtoValue Value)[] overrides)
    {
        var members = record is ProtoValue.Rec r
            ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
            : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

        foreach (var (name, value) in overrides) members[name] = value;
        return new ProtoValue.Rec(members);
    }
}
