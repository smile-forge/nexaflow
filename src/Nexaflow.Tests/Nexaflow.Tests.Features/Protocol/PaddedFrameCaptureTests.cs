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

    /// <summary>One tagged option: a code, a length, and that many octets.</summary>
    private static Field Option() => new()
    {
        Id = "option",
        Pattern = new Pattern.Group(
        [
            new Field { Id = "optionCode", Pattern = U8, Value = Expr.Parse("item.optionCode") },
            new Field { Id = "optionLength", Pattern = U8, Value = Expr.Parse("fields.optionValue.extent") },
            new Field
            {
                Id = "optionValue",
                Pattern = Pattern.Opaque.Measured(Expr.Parse("fields.optionLength.value")),
                Value = Expr.Parse("item.optionValue"),
            },
        ]),
    };

    private static MessageDef Definition()
    {
        var operation = new Field { Id = "operation", Pattern = U8, Value = Expr.Parse("inputs.operation") };
        var hardwareType = new Field { Id = "hardwareType", Pattern = U8, Value = Expr.Parse("inputs.hardwareType") };
        var hardwareLength = new Field { Id = "hardwareLength", Pattern = U8, Value = Expr.Parse("inputs.hardwareLength") };
        var flags = new Field { Id = "flags", Pattern = U16, Value = Expr.Parse("inputs.flags") };

        var message = new MessageDef
        {
            Id = "lease",
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
                // it is one, and swallowing it into the chain would leave nothing to write it back out.
                new Field
                {
                    Id = "options",
                    Value = Expr.Parse("inputs.options"),
                    Pattern = new Pattern.Chain(Option(), Expr.Parse("room > 0 && peek != 0xff")),
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

    // ── The captures ──────────────────────────────────────────────────────────

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

        // Each option keeps its code and its octets; the length comes back from the octets.
        inputs["options"] = new ProtoValue.List(
        [
            .. decoded["options"].AsList().Cast<ProtoValue.Rec>().Select(o => EvalScope.Record(
                ("optionCode", o.Members["optionCode"]),
                ("optionValue", o.Members["optionValue"]))),
        ]);

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }
}
