using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The third way a width comes from a value.
///
/// <para>
/// The corpus had two and they are not this one. A continuation chain spends a bit of every octet on a
/// flag and regroups the value into seven-bit digits; an escaped marker octet is consumed entirely, either
/// being the value or counting what follows. Here the marker is two bits at the top of the first octet and
/// the rest of that octet is the value's most significant part — nothing spent that is not needed, and no
/// regrouping.
/// </para>
///
/// <para>
/// The worked examples are RFC 9000's own, which is the point of using them: they were written by somebody
/// with no interest in whether this engine's shapes compose, and one of them is the awkward case the other
/// two shapes would have got wrong.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol wire shapes — tree nodes land with the engine")]
public class PrefixedIntegerTests
{
    /// <summary>Two marker bits selecting one, two, four or eight octets. Stated rather than defaulted:
    /// the doubling is one family's choice, and a default would put it in the engine under a general
    /// name.</summary>
    private static Pattern.Prefixed Quic => new(2, [1, 2, 4, 8]);

    private static MessageDef Carrying(Pattern.Prefixed shape) => new()
    {
        Id = "carrying",
        Context = Context.Given.These("n"),
        Fields = [new Field { Id = "n", Pattern = shape, Value = Expr.Parse("inputs.n") }],
    };

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static long Read(string hex, Pattern.Prefixed? shape = null)
        => new MessageCodec(Carrying(shape ?? Quic)).Decode(Hex(hex))["n"].AsInt();

    private static byte[] Write(long value, Pattern.Prefixed? shape = null)
        => new MessageCodec(Carrying(shape ?? Quic))
               .Encode(new EvalScope().Set("inputs", EvalScope.Record(("n", ProtoValue.Of(value)))));

    // ── Both directions, against the specification's own examples ─────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Carrying(Quic)).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void Every_worked_example_reads_as_the_number_it_is_meant_to()
    {
        // RFC 9000 appendix A, verbatim. The eight-octet one matters most: its value needs 58 bits, which
        // is more than seven octets of a continuation chain would carry and more than a marker octet
        // counting seven could reach.
        Assert.AreEqual(151288809941952652L, Read("c2 19 7c 5e ff 14 e8 8c"));
        Assert.AreEqual(494878333L, Read("9d 7f 3e 7d"));
        Assert.AreEqual(15293L, Read("7b bd"));
        Assert.AreEqual(37L, Read("25"));
    }

    [TestMethod]
    public void And_each_one_is_written_back_as_the_octets_it_came_from()
    {
        foreach (var octets in (string[])["c2 19 7c 5e ff 14 e8 8c", "9d 7f 3e 7d", "7b bd", "25"])
            CollectionAssert.AreEqual(Hex(octets), Write(Read(octets)), octets);
    }

    [TestMethod]
    public void The_marker_is_not_taken_out_of_the_value()
    {
        // What separates this from the escaped-marker shape, in one assertion. The first octet of the
        // two-octet form is 0x7b: the top two bits select the width and the remaining six — 0x3b — are the
        // value's high bits, so it reads 15293 and not 0xbd.
        Assert.AreEqual(15293L, Read("7b bd"));
        Assert.AreEqual(0x3bbdL, 15293L);
    }

    [TestMethod]
    public void A_value_at_the_edge_of_a_width_moves_up_to_the_next_one()
    {
        // Six bits of the first octet, so one octet reaches 63 and not 255. Getting that boundary wrong is
        // the mistake the shape invites, and it is silent: 64 would encode into one octet whose marker bits
        // then say something else entirely.
        CollectionAssert.AreEqual(Hex("3f"), Write(63));
        CollectionAssert.AreEqual(Hex("40 40"), Write(64));

        Assert.AreEqual(63L, Read("3f"));
        Assert.AreEqual(64L, Read("40 40"));
    }

    [TestMethod]
    public void The_widest_form_holds_what_it_says_and_refuses_what_it_cannot()
    {
        long largest = (1L << 62) - 1;

        Assert.AreEqual(largest, Read(Convert.ToHexString(Write(largest))));

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => Write(1L << 62));
        StringAssert.Contains(ex.Message, "needs more than the 8 octet(s)");
    }

    // ── Canonicality ──────────────────────────────────────────────────────────

    [TestMethod]
    public void A_number_written_wider_than_it_needs_is_refused()
    {
        // The same law the other two shapes carry, and for the same reason rather than by analogy: 37 in
        // two octets decodes perfectly well and re-encodes to one, so accepting it makes
        // encode(decode(b)) != b for input the protocol calls malformed anyway.
        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => Read("40 25"));

        StringAssert.Contains(ex.Message, "carries 37 in 2 octet(s) and 1 would hold it");
        StringAssert.Contains(ex.Message, "re-encodes shorter");
    }

    [TestMethod]
    public void Unless_the_protocol_says_a_wider_one_is_a_different_message()
    {
        // Not every family refuses it. A field that pads to a fixed width and means something by the
        // padding needs the wide form to survive, so the rule is declared rather than assumed.
        Assert.AreEqual(37L, Read("40 25", new Pattern.Prefixed(2, [1, 2, 4, 8], Minimal: false)));
    }

    // ── What the shape refuses to be ──────────────────────────────────────────

    [TestMethod]
    public void Every_value_the_marker_can_take_has_to_mean_a_width()
    {
        // Three widths under two marker bits leaves one value undeclared, and a packet carrying it would
        // read as whatever came next rather than as an error.
        var issues = new MessageCodec(Carrying(new Pattern.Prefixed(2, [1, 2, 4]))).Validate();

        Assert.IsTrue(issues.Any(i => i.Contains("select 4 widths and 3 are given")),
            string.Join("\n", issues));
    }

    [TestMethod]
    public void The_widths_have_to_increase()
    {
        // Out of order they still decode. What breaks is encoding: the narrowest form of a value would
        // become a property of how the list was written rather than of the value.
        var issues = new MessageCodec(Carrying(new Pattern.Prefixed(2, [1, 4, 2, 8]))).Validate();

        Assert.IsTrue(issues.Any(i => i.Contains("the widths must increase")), string.Join("\n", issues));
    }

    [TestMethod]
    public void A_marker_that_takes_the_whole_octet_is_a_different_shape()
    {
        var issues = new MessageCodec(Carrying(new Pattern.Prefixed(8, [.. Enumerable.Range(1, 256)])))
            .Validate();

        Assert.IsTrue(issues.Any(i => i.Contains("marker must be 1..7 bits")), string.Join("\n", issues));
    }
}
