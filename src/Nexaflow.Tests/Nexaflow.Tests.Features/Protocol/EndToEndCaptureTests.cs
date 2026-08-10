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
    private static MessageDef Definition() => new()
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

            new Field { Id = "rootDelay",      Pattern = new Pattern.Scalar(4, true), Value = Expr.Parse("inputs.rootDelay") },
            new Field { Id = "rootDispersion", Pattern = new Pattern.Scalar(4, true), Value = Expr.Parse("inputs.rootDispersion") },
            new Field { Id = "referenceId",    Pattern = new Pattern.Opaque(4),       Value = Expr.Parse("inputs.referenceId") },

            new Field { Id = "referenceTimestamp", Pattern = new Pattern.Scalar(8, true), Value = Expr.Parse("inputs.referenceTimestamp") },
            new Field { Id = "originTimestamp",    Pattern = new Pattern.Scalar(8, true), Value = Expr.Parse("inputs.originTimestamp") },
            new Field { Id = "receiveTimestamp",   Pattern = new Pattern.Scalar(8, true), Value = Expr.Parse("inputs.receiveTimestamp") },
            new Field { Id = "transmitTimestamp",  Pattern = new Pattern.Scalar(8, true), Value = Expr.Parse("inputs.transmitTimestamp") },
        ],
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

        Assert.AreEqual(0, decoded["rootDelay"].AsInt());
        Assert.AreEqual(0, decoded["originTimestamp"].AsInt(), "a client has no prior server packet");

        // The one non-zero timestamp: 0xee22ea404ccccccd.
        Assert.AreEqual(unchecked((long)0xee22ea404ccccccdUL), decoded["transmitTimestamp"].AsInt());
    }

    [TestMethod]
    public void Decoding_the_server_capture_recovers_the_values_that_differ()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture(1));

        Assert.AreEqual(4, decoded["mode"].AsInt(), "mode 4 = server");
        Assert.AreEqual(2, decoded["stratum"].AsInt());
        Assert.AreEqual(-23, decoded["precision"].AsInt(), "0xe9 signed is -23");

        // Root delay 0x0000028f = 655 in 16.16, which the corpus records as 0.0099945 s.
        Assert.AreEqual(655, decoded["rootDelay"].AsInt());
        Assert.AreEqual(0.0099945, new Evaluator().Eval("655 |> unfixed(16, 16)", new EvalScope()).AsNumber(), 1e-7);

        // All four timestamps are populated in a response.
        foreach (var name in (string[])["referenceTimestamp", "originTimestamp", "receiveTimestamp", "transmitTimestamp"])
            Assert.AreNotEqual(0, decoded[name].AsInt(), name);
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

        // mode is 3 bits; 8 does not fit. Silently masking it would emit a valid-looking wrong message.
        scope.Set("inputs", Merge(scope.Root("inputs"),
            ("flags", EvalScope.Record(("leapIndicator", ProtoValue.Of(0L)),
                                       ("version", ProtoValue.Of(4L)),
                                       ("mode", ProtoValue.Of(8L))))));

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
