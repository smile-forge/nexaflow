using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A message that is mostly fixed-width padding, with a variable tail bolted on by a later specification.
///
/// <para>
/// The protocol is DHCP over BOOTP. Its first 236 octets are a rigid frame — two text fields of 64 and 128
/// octets that carry a few characters and NULs for the rest — followed by a four-octet constant, then a
/// list of tagged options, then a terminator, then whatever zero fill is needed to reach a minimum size.
/// </para>
///
/// <para>
/// What it exercises that nothing before it did: a field whose value the document <b>fixes outright</b>,
/// text that must be padded back out to a fixed width to round-trip, a chain that stops at a sentinel it
/// does not consume, and trailing fill that exists only to make the datagram long enough.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class PaddedFrameCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);
    private static Pattern U32 => new Pattern.Scalar(4, BigEndian: true);

    private static Field Address(string id) => new()
    {
        Id = id,
        Pattern = new Pattern.Opaque(4),
        Value = Expr.Parse($"inputs.{id}"),
    };

    /// <summary>Text in a fixed-width field, NUL-filled to the end. The padding is not decoration: it has
    /// to come back on the way out or the octets are different ones.</summary>
    private static Field Padded(string id, int width) => new()
    {
        Id = id,
        Pattern = new Pattern.Opaque(width),
        Value = Expr.Parse($"inputs.{id}"),
        Via = Conversion.Of("fit", width),
    };

    /// <summary>The one-octet value a message type carries, which is what makes it worth naming.</summary>
    private static readonly Field MessageTypeValue = new()
    {
        Id = "messageTypeValue", Pattern = U8, Value = Expr.Parse("item.messageTypeValue"),
    };

    /// <summary>
    /// The options, as what they are: components that say which kind they are.
    ///
    /// <para>
    /// This was a chain over one uniform code/length/value element, and the shape was wrong rather than
    /// merely coarse. <b>Pad is a single octet</b> — code 0, no length, no value — so a uniform element
    /// read the octet after it as a length and everything downstream moved. The capture in the corpus has
    /// no padding in it, which is why nothing ever said so.
    /// </para>
    ///
    /// <para>
    /// Declaring the kinds separately fixes that by construction, because a kind that carries nothing is
    /// simply a kind with no fields. It also buys the thing the uniform element could never offer: the
    /// message type is a <i>node</i>, so a rule can constrain it and a reader can name it, rather than it
    /// being whichever component happened to have code 53.
    /// </para>
    /// </summary>
    private static Pattern Options() => new Pattern.Assorted(
        new Field { Id = "optionCode", Pattern = U8 },
        [
            // No fields at all. One octet of padding is a whole component, and it may come as often as
            // an implementation likes.
            Arm.On("pad", ProtoValue.Of(0L), [], repeats: true),

            Arm.On("messageType", ProtoValue.Of(53L),
            [
                // Fixed at one by the specification rather than measured off the value: this option's
                // length is not a fact about what it carries, it is a constant the standard states.
                new Field { Id = "messageTypeLength", Pattern = U8, Value = Expr.Parse("1") },
                MessageTypeValue,
            ]),

            // Everything this document has not been taught. It has to be carried through unchanged — an
            // option nobody recognises is a newer peer, not a corrupt datagram.
            Arm.Otherwise("other",
            [
                new Field { Id = "optionLength", Pattern = U8, Value = Expr.Parse("fields.optionValue.extent") },
                new Field
                {
                    Id = "optionValue",
                    Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.optionLength.value")),
                    Value = Expr.Parse("item.optionValue"),
                },
            ], repeats: true),
        ],
        Expr.Parse("room > 0 && peek != 0xff"));

    internal static MessageDef Definition()
    {
        var operation = new Field { Id = "operation", Pattern = U8, Value = Expr.Parse("inputs.operation") };
        var hardwareType = new Field { Id = "hardwareType", Pattern = U8, Value = Expr.Parse("inputs.hardwareType") };
        var hardwareLength = new Field { Id = "hardwareLength", Pattern = U8, Value = Expr.Parse("inputs.hardwareLength") };
        var flags = new Field { Id = "flags", Pattern = U16, Value = Expr.Parse("inputs.flags") };

        var message = new MessageDef
        {
            Id = "lease",

            // A real mix, and the reason the kinds exist. The hardware address comes off the adapter and is
            // never asked for; the host name is a question; the transaction id advances on its own.
            Context =
            [
                .. Context.Given.These("operation", "hardwareType", "hardwareLength", "hops", "elapsed",
                    "flags", "clientAddress", "offeredAddress", "nextServer", "relay", "bootFile",
                    "options", "fill"),
                new Context.Ambient { Key = "hardwareAddress", Purpose = "This adapter's own address." },
                new Context.Counter { Key = "transactionId", Purpose = "Ties an offer back to the request that asked for it." },
                new Context.Prompted { Key = "serverName", Purpose = "The name this server answers to, if it says one." },
            ],

            Fields =
            [
                operation,
                hardwareType,
                hardwareLength,
                new Field { Id = "hops", Pattern = U8, Value = Expr.Parse("inputs.hops") },
                new Field { Id = "transactionId", Pattern = U32, Value = Expr.Parse("inputs.transactionId") },
                new Field { Id = "elapsed", Pattern = U16, Value = Expr.Parse("inputs.elapsed") },
                flags,

                Address("clientAddress"),
                Address("offeredAddress"),
                Address("nextServer"),
                Address("relay"),

                // Sixteen octets of which the hardware length says how many mean anything. Which ones
                // those are is interpretation; the wire has sixteen either way.
                new Field
                {
                    Id = "hardwareAddress",
                    Pattern = new Pattern.Opaque(16),
                    Value = Expr.Parse("inputs.hardwareAddress"),
                },

                Padded("serverName", 64),
                Padded("bootFile", 128),

                // Fixed by the document, so the wire has no say in it. Nothing needed for this beyond
                // saying what it is: a value with no free names is one the engine already checks.
                new Field
                {
                    Id = "marker",
                    Pattern = new Pattern.Opaque(4),
                    Value = Expr.Parse("'63825363' |> unhex()"),
                },

                // Stops at a sentinel it does not consume — the terminator is a field of its own, because
                // it is one, and swallowing it into the run would leave nothing to write it back out.
                new Field
                {
                    Id = "options",
                    Value = Expr.Parse("inputs.options"),
                    Pattern = Options(),
                },

                new Field { Id = "terminator", Pattern = U8, Value = Expr.Parse("0xff") },

                // Zero fill to a minimum datagram size. Not part of any option and not optional either:
                // the octets are there and have to come back.
                new Field
                {
                    Id = "fill",
                    Pattern = Pattern.Opaque.Measured(Expr.Parse("room")),
                    Value = Expr.Parse("inputs.fill"),
                },
            ],
        };

        return message with
        {
            Rules =
            [
                new Rule.Domain
                {
                    Field = operation,
                    Allowed = [ValueRange.Between(1, 2)],
                    Because = "there are two directions and the octet has room for 256.",
                },
                new Rule.Domain
                {
                    Field = flags,
                    Allowed = [ValueRange.Exactly(0), ValueRange.Exactly(0x8000)],
                    Because = "one bit is defined and the other fifteen must be zero, so most of the "
                            + "65536 values this field can hold are malformed rather than unusual.",
                },
                // Reaching into the options from outside them, which is the whole point of the kinds being
                // separately declared: this names a node, not "whichever component had code 53".
                new Rule.Domain
                {
                    Field = MessageTypeValue,
                    Allowed = [ValueRange.Between(1, 8)],
                    Because = "the standard defines eight message types and the octet has room for 256, so "
                            + "the rest are malformed rather than merely unfamiliar.",
                },
                new Rule.Requires
                {
                    Within = message.Root,
                    When = Expr.Parse("fields.hardwareType.value == 1"),
                    Then = Expr.Parse("fields.hardwareLength.value == 6"),
                    Because = "that hardware type has six-octet addresses; a length saying otherwise "
                            + "describes an address the type does not have.",
                },
            ],
        };
    }

    private static byte[] Capture(int index) => ProtocolCorpus.Get("dhcp").Captures[index].Bytes;

    /// <summary>
    /// The capture with one octet of padding spliced into the options, and one octet of trailing fill
    /// dropped to keep the datagram the length it was.
    /// </summary>
    /// <remarks>
    /// Synthetic, and it has to be: the corpus capture contains no padding, which is exactly why the
    /// shape could be wrong for as long as it was. Padding is legal anywhere between options and real
    /// implementations emit it to align what follows.
    /// </remarks>
    private static byte[] Padded()
    {
        var original = Capture(0);
        int terminator = Array.IndexOf(original, (byte)0xff, 240);

        return [.. original[..terminator], 0x00, .. original[terminator..^1]];
    }

    // ── The captures ──────────────────────────────────────────────────────────

    /// <summary>
    /// A component that carries nothing but the fact that it is there.
    ///
    /// <para>
    /// Pad is one octet: code zero, no length, no value. Under a uniform code/length/value element it was
    /// not merely unmodelled, it was actively mis-read — the octet after it became a length and every
    /// option after that moved. Declaring the kinds separately makes it a kind with no fields, which is
    /// not a special case of anything.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Padding_between_options_is_a_component_with_no_fields()
    {
        var decoded = new MessageCodec(Definition()).Decode(Padded());

        var components = decoded["options"].AsList().Cast<ProtoValue.Rec>().ToList();

        Assert.AreEqual(1, components.Count(o => o.Members["sort"].AsText() == "pad"),
            "the padding octet is a component of its own");

        // And every real option still reads as itself. That is the part the old shape got wrong: a pad
        // anywhere in the run made the octet after it a length, and everything from there was a different
        // option — a datagram that decoded without complaint and said something else.
        CollectionAssert.AreEqual(new long[] { 53, 61, 12, 55 },
            components.Where(o => o.Members["sort"].AsText() != "pad")
                      .Select(o => o.Members["optionCode"].AsInt()).ToArray());
    }

    [TestMethod]
    public void And_padding_goes_back_out_where_it_came_from()
    {
        var codec = new MessageCodec(Definition());
        var octets = Padded();

        CollectionAssert.AreEqual(octets, codec.Encode(InputsFrom(codec.Decode(octets))));
    }

    /// <summary>
    /// The message type is a node, so something outside the options can name it.
    ///
    /// <para>
    /// Under the old shape this was "whichever component happened to carry code 53", which nothing could
    /// point at and no rule could constrain. It is the same gain that made a body sizeable by a
    /// <c>Content-Length</c>, arriving in a protocol whose components are numbered rather than named.
    /// </para>
    /// </summary>
    [TestMethod]
    public void The_message_type_can_be_named_and_therefore_constrained()
    {
        Assert.AreEqual(1, new MessageCodec(Definition()).Decode(Capture(0))["messageTypeValue"].AsInt(),
            "a discover, named directly rather than found by looking for code 53");

        // Code 53 carrying 99 is structurally perfect and means nothing. The rule reaches a field inside a
        // component from outside it, which is only possible because that kind comes at most once.
        var wrong = (byte[])Capture(0).Clone();
        wrong[Array.IndexOf(wrong, (byte)53, 240) + 2] = 99;

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Definition()).Decode(wrong));
        StringAssert.Contains(ex.Message, "eight message types");
    }

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Definition()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void The_request_decodes_through_its_fixed_frame_and_its_tagged_tail()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(0));

        Assert.AreEqual(1, decoded["operation"].AsInt());
        Assert.AreEqual(0x3903f326, decoded["transactionId"].AsInt());
        Assert.AreEqual("", decoded["serverName"].AsText(), "64 octets of NUL is an empty name");
        Assert.AreEqual("", decoded["bootFile"].AsText());

        var options = decoded["options"].AsList().Cast<ProtoValue.Rec>().ToList();
        Assert.AreEqual(4, options.Count, "the chain stops at the terminator without eating it");
        CollectionAssert.AreEqual(new long[] { 53, 61, 12, 55 },
            options.Select(o => o.Members["optionCode"].AsInt()).ToArray());

        Assert.AreEqual(255, decoded["terminator"].AsInt());
        Assert.AreEqual(33, decoded["fill"].AsBytes().Length, "zero fill to the 300-octet minimum");
    }

    [TestMethod]
    public void The_reply_carries_text_in_a_field_far_wider_than_the_text()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(1));

        Assert.AreEqual(2, decoded["operation"].AsInt());
        Assert.AreEqual("dhcp.nexa.lan", decoded["serverName"].AsText(), "13 characters in a 64-octet field");
        CollectionAssert.AreEqual(new long[] { 53, 54, 51, 58, 59, 1, 3, 6, 15, 28 },
            decoded["options"].AsList().Cast<ProtoValue.Rec>()
                .Select(o => o.Members["optionCode"].AsInt()).ToArray());
        Assert.AreEqual(0, decoded["fill"].AsBytes().Length, "this one already reaches the minimum");
    }

    [TestMethod]
    public void Both_captures_re_encode_to_the_exact_original_octets()
    {
        // Every option length is withheld, and so is the marker — a value the document fixes is not one
        // the caller supplies. The padding of the two text fields is not supplied either: it comes back
        // from the width, which is the only reason a 13-character name can survive a 64-octet field.
        foreach (int index in (int[])[0, 1])
        {
            var original = Capture(index);
            var codec = new MessageCodec(Definition());
            var reEncoded = codec.Encode(InputsFrom(codec.Decode(original)));

            Assert.AreEqual(original.Length, reEncoded.Length, $"capture {index}: length");
            CollectionAssert.AreEqual(original, reEncoded,
                $"capture {index} did not round-trip.\nexpected {Convert.ToHexString(original).ToLowerInvariant()}"
              + $"\nactual   {Convert.ToHexString(reEncoded).ToLowerInvariant()}");
        }
    }

    // ── What it refuses ───────────────────────────────────────────────────────

    [TestMethod]
    public void A_constant_the_document_fixes_must_be_the_one_that_arrived()
    {
        // Four octets that mean "the tail of this datagram is options rather than vendor padding". A
        // wrong one used to be read, bound, and re-encoded as the right one.
        var tampered = (byte[])[.. Capture(0)];
        tampered[236] = 0x64;

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Definition()).Decode(tampered));
        StringAssert.Contains(ex.Message, "is fixed at");
    }

    [TestMethod]
    public void A_flag_word_with_a_reserved_bit_set_is_refused()
    {
        // 65536 values, two of them legal. Nothing about a two-octet field says that.
        var tampered = (byte[])[.. Capture(0)];
        tampered[10] = 0x40;

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Definition()).Decode(tampered));
        StringAssert.Contains(ex.Message, "'flags' is 16384");
        StringAssert.Contains(ex.Message, "most of the 65536 values");
    }

    [TestMethod]
    public void A_hardware_length_that_contradicts_the_hardware_type_is_refused()
    {
        var tampered = (byte[])[.. Capture(0)];
        tampered[2] = 0x08;

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Definition()).Decode(tampered));
        StringAssert.Contains(ex.Message, "obliges");
        StringAssert.Contains(ex.Message, "six-octet addresses");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EvalScope InputsFrom(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        foreach (var (name, value) in decoded.Captures)
            if (name is not ("marker" or "terminator")) inputs[name] = value;

        // Handed straight back. What a decode produces is what an encode consumes, so there is nothing to
        // rebuild — and nothing to get wrong by rebuilding it, which is what this used to do: it picked out
        // a code and a value, which is the wrong shape for a component that carries neither.
        inputs["options"] = decoded["options"];

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }
}
