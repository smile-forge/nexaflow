using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A real capture through the whole stack: document → decode → captures → encode → the same octets.
///
/// <para>
/// Everything before this was tested against synthetic inputs. This is the first point at which the
/// expression core, the converter set, the pattern library, the facet resolver and the codec are all
/// exercised together against bytes taken from the corpus rather than invented for the test — which is
/// the only kind of evidence that has been worth anything so far.
/// </para>
///
/// <para>
/// The protocol is chosen for what it exercises, not for being easy: sub-byte bit packing, signed scalars,
/// a fixed-point transform, and a 48-octet layout with no length fields anywhere, so every offset has to
/// come out of the declaration rather than out of the data.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class EndToEndCaptureTests
{
    /// <summary>
    /// The document. Note what is <i>not</i> here: no protocol name, no bespoke field kind, nothing the
    /// engine had to learn. Bit slices, scalars and opaque spans, with values supplied by expressions.
    /// </summary>
    internal static MessageDef Definition() => new()
    {
        Id = "timeSync",
        Fields =
        [
            // Three runs inside one octet — 2 + 3 + 3.
            new Field
            {
                Id = "flags",
                Pattern = new Pattern.Bits([new("leapIndicator", 2), new("version", 3), new("mode", 3)]),
                Value = Expr.Parse("inputs.flags"),
            },
            new Field { Id = "stratum",   Pattern = new Pattern.Scalar(1, true), Value = Expr.Parse("inputs.stratum") },

            // Signed: the capture carries +6 and -20 in single octets.
            new Field { Id = "poll",      Pattern = new Pattern.Scalar(1, true, Signed: true), Value = Expr.Parse("inputs.poll") },
            new Field { Id = "precision", Pattern = new Pattern.Scalar(1, true, Signed: true), Value = Expr.Parse("inputs.precision") },

            // Fixed point, not magnitudes. Declared as plain 4-octet integers these round-trip perfectly
            // and read 65536 times too large, which is the failure the explanation exists to expose: the
            // octets were never wrong, the description was.
            new Field
            {
                Id = "rootDelay",
                Pattern = new Pattern.Scalar(4, true),
                Value = Expr.Parse("inputs.rootDelay"),
                Via = Conversion.Of("fixed", 16, 16),
            },
            new Field
            {
                Id = "rootDispersion",
                Pattern = new Pattern.Scalar(4, true),
                Value = Expr.Parse("inputs.rootDispersion"),
                Via = Conversion.Of("fixed", 16, 16),
            },

            // Four octets, and four octets under every stratum. An audit against the specification points
            // out that what they MEAN changes — a reason code at 0, a source name at 1, an address above
            // that — and it is right, but that is interpretation and not representation. Nothing about
            // the layout differs, so a choice here would be three arms of identical shape distinguished
            // only by which converter dresses them up. The meaning belongs on a node of its own, in a
            // layer that does not exist yet; recording that is more honest than smuggling it in here.
            new Field { Id = "referenceId", Pattern = new Pattern.Opaque(4), Value = Expr.Parse("inputs.referenceId") },

            // 32 bits of seconds against 32 bits of fraction. Not the fixed-point converter: 64 bits do
            // not survive a double, so scaling here would lose the low bits and the round trip with them.
            Timestamp("referenceTimestamp"),
            Timestamp("originTimestamp"),
            Timestamp("receiveTimestamp"),
            Timestamp("transmitTimestamp"),
        ],

        // What the wire shape cannot say. Every one of these was found by reading the generated
        // description against the specification, and none of them could have been found by a round trip:
        // a message breaking all three re-encodes to exactly the octets it arrived as.
        Rules =
        [
            new Rule.Domain
            {
                Field = "version",
                Allowed = [ValueRange.Exactly(4)],
                Because = "this document describes version 4, and a packet announcing another version is "
                        + "not a packet it can claim to have understood.",
            },
            new Rule.Domain
            {
                Field = "mode",
                Allowed = [ValueRange.Between(1, 6)],
                Because = "0 is reserved and 7 is private use; both are three bits wide and neither is a "
                        + "value this message may carry.",
            },
            new Rule.Domain
            {
                Field = "stratum",
                Allowed = [ValueRange.Between(0, 16)],
                Because = "16 means unsynchronised and everything above it is reserved, so a stratum of "
                        + "200 decodes into something that looks exactly as valid as a real one.",
            },
        ],
    };

    private static Field Timestamp(string id) => new()
    {
        Id = id,
        Pattern = new Pattern.Bits([new($"{id}Seconds", 32), new($"{id}Fraction", 32)]),
        Value = Expr.Parse($"inputs.{id}"),
    };

    private static byte[] Capture(int index)
        => ProtocolCorpus.Get("ntp").Captures[index].Bytes;

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Definition()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void A_bit_group_whose_slices_do_not_fill_whole_octets_is_rejected()
    {
        // A misaligned group reads plausible values from the wrong place, which is the worst kind of bug
        // to leave to run time.
        var broken = new MessageDef
        {
            Id = "misaligned",
            Fields = [new Field { Id = "f", Pattern = new Pattern.Bits([new("a", 3), new("b", 2)]) }],
        };

        StringAssert.Contains(string.Join("\n", broken.Validate()), "whole number of");
    }

    [TestMethod]
    public void Decoding_the_client_capture_recovers_every_field_the_corpus_lists()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(0));

        // 0x23 = 00|100|011
        Assert.AreEqual(0, decoded["leapIndicator"].AsInt());
        Assert.AreEqual(4, decoded["version"].AsInt(), "version 4");
        Assert.AreEqual(3, decoded["mode"].AsInt(), "mode 3 = client");

        Assert.AreEqual(0, decoded["stratum"].AsInt());
        Assert.AreEqual(6, decoded["poll"].AsInt(), "0x06 as a signed octet is +6");
        Assert.AreEqual(-20, decoded["precision"].AsInt(), "0xec as a signed octet is -20");

        Assert.AreEqual(0.0, decoded["rootDelay"].AsNumber());
        Assert.AreEqual(0, decoded["originTimestampSeconds"].AsInt(), "a client has no prior server packet");

        // The one non-zero timestamp: 0xee22ea40 seconds against 0x4ccccccd of fraction. Read as one
        // 64-bit magnitude the number is meaningless; the split is what makes it a time.
        Assert.AreEqual(0xee22ea40, decoded["transmitTimestampSeconds"].AsInt());
        Assert.AreEqual(0x4ccccccd, decoded["transmitTimestampFraction"].AsInt());

        Assert.AreEqual(4, decoded["referenceId"].AsBytes().Length, "four octets, whatever they turn out to mean");
    }

    [TestMethod]
    public void Decoding_the_server_capture_recovers_the_values_that_differ()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(1));

        Assert.AreEqual(4, decoded["mode"].AsInt(), "mode 4 = server");
        Assert.AreEqual(2, decoded["stratum"].AsInt());
        Assert.AreEqual(-23, decoded["precision"].AsInt(), "0xe9 signed is -23");

        // Root delay 0x0000028f is 655 in 16.16 — which is 0.0099945 seconds, not 655 of anything. The
        // document says so now; declared as a plain integer it round-tripped perfectly and read 65536
        // times too large, and only a description laid beside the specification could catch that.
        Assert.AreEqual(0.0099945, decoded["rootDelay"].AsNumber(), 1e-7);

        // All four timestamps are populated in a response.
        foreach (var name in (string[])["referenceTimestamp", "originTimestamp", "receiveTimestamp", "transmitTimestamp"])
            Assert.AreNotEqual(0, decoded[$"{name}Seconds"].AsInt(), name);
    }

    [TestMethod]
    public void A_value_the_wire_shape_allows_but_the_protocol_does_not_is_refused()
    {
        // The gap all three audits found first, and the one a round trip can never find: each of these
        // re-encodes to exactly the octets it arrived as. Only a rule can tell them apart from real ones.
        var codec = new MessageCodec(Definition());

        foreach (var (offset, octet, expected) in ((int, byte, string)[])
        [
            (0, 0x1b, "'version' is 3"),        // 00|011|011 — version 3
            (0, 0x20, "'mode' is 0"),           // 00|100|000 — mode 0, reserved
            (1, 0xc8, "'stratum' is 200"),      // reserved stratum
        ])
        {
            var tampered = (byte[])[.. Capture(0)];
            tampered[offset] = octet;

            var ex = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Decode(tampered));
            StringAssert.Contains(ex.Message, expected);
            StringAssert.Contains(ex.Message, "not a value it may take");
        }
    }

    [TestMethod]
    public void A_refusal_says_why_in_the_words_of_whoever_wrote_the_document()
    {
        // The reason is the point. "Value 200 is not allowed" tells a reader nothing about what to do,
        // and tells a model drafting a document nothing about which way to correct it.
        var tampered = (byte[])[.. Capture(0)];
        tampered[1] = 0xc8;

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Definition()).Decode(tampered));
        StringAssert.Contains(ex.Message, "16 means unsynchronised");
        StringAssert.Contains(ex.Message, "looks exactly as valid as a real one");
    }

    [TestMethod]
    public void Both_captures_re_encode_to_the_exact_original_octets()
    {
        // THE test. Decode a real capture, feed the captures straight back in, and require the same bytes.
        foreach (int index in (int[])[0, 1])
        {
            var original = Capture(index);
            var codec = new MessageCodec(Definition());
            var decoded = codec.Decode(original);

            var reEncoded = codec.Encode(ScopeFrom(decoded));

            Assert.AreEqual(original.Length, reEncoded.Length, $"capture {index}: length");
            CollectionAssert.AreEqual(original, reEncoded,
                $"capture {index} did not round-trip.\nexpected {Convert.ToHexString(original).ToLowerInvariant()}"
              + $"\nactual   {Convert.ToHexString(reEncoded).ToLowerInvariant()}");
        }
    }

    [TestMethod]
    public void The_decoded_breakdown_accounts_for_all_48_octets()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(0));

        // Bit slices share their group's span, so distinct spans rather than rows.
        var covered = decoded.Spans
            .Select(s => (s.Offset, s.Length))
            .Distinct()
            .OrderBy(s => s.Offset)
            .ToList();

        int at = 0;
        foreach (var (offset, length) in covered)
        {
            Assert.AreEqual(at, offset, "spans must be contiguous with no gap");
            at += length;
        }
        Assert.AreEqual(48, at, "every octet of the capture is accounted for");
    }

    [TestMethod]
    public void A_length_that_disagrees_with_the_data_is_an_error_rather_than_a_silent_truncation()
    {
        var codec = new MessageCodec(Definition());

        // Short: the definition asks for more than the message holds.
        var truncated = Capture(0)[..40];
        var tooShort = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Decode(truncated));
        StringAssert.Contains(tooShort.Message, "disagree");

        // Long: trailing octets nobody claimed. Ignoring them accepts a malformed capture as valid, and
        // the corpus has a 68-octet variant of this very message that must NOT decode as the 48-octet one.
        var tooLong = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Decode(Capture(2)));
        StringAssert.Contains(tooLong.Message, "trailing");
    }

    [TestMethod]
    public void An_out_of_range_bit_slice_value_is_refused()
    {
        var codec = new MessageCodec(Definition());
        var scope = ScopeFrom(codec.Decode(Capture(0)));

        // leapIndicator is 2 bits; 4 does not fit. Silently masking it would emit a valid-looking wrong
        // message. (Not `mode` any more: that now carries a value rule, which refuses an illegal value
        // before the packing ever gets asked whether it fits — a better error, and a different one.)
        scope.Set("inputs", Merge(scope.Root("inputs"),
            ("flags", EvalScope.Record(("leapIndicator", ProtoValue.Of(4L)),
                                       ("version", ProtoValue.Of(4L)),
                                       ("mode", ProtoValue.Of(3L))))));

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Encode(scope));
        StringAssert.Contains(ex.Message, "does not fit");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Feeds decoded captures back in as <c>inputs.*</c>, which is what makes the round trip a
    /// genuine test rather than a restatement of the literal bytes.</summary>
    private static EvalScope ScopeFrom(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        foreach (var (name, value) in decoded.Captures) inputs[name] = value;

        // The bit group is written as a record of its slices, mirroring how it decodes.
        inputs["flags"] = EvalScope.Record(
            ("leapIndicator", decoded["leapIndicator"]),
            ("version", decoded["version"]),
            ("mode", decoded["mode"]));

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }

    private static ProtoValue Merge(ProtoValue record, params (string Name, ProtoValue Value)[] overrides)
    {
        var members = record is ProtoValue.Rec r
            ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
            : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

        foreach (var (name, value) in overrides) members[name] = value;
        return new ProtoValue.Rec(members);
    }
}
