using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// A code table in a document, and a codec in the engine that has never heard of it.
///
/// <para>
/// The table below is HPACK's, and every row of it is RFC 7541 Appendix B — which is exactly why it is
/// here and not in the engine. A specification that prints two hundred and fifty-seven rows of data has
/// stated something about its own protocol; the engine's part is the sentence underneath, that symbols
/// become bit runs of unequal length and the last octet is filled out from the end-of-stream code. The
/// same split <c>crc16</c> makes with its polynomial, at a larger scale.
/// </para>
///
/// <para>
/// Worth saying what is <b>not</b> here: any Huffman <i>construction</i>. Building an optimal code from
/// symbol frequencies is a compression concern and no wire format asks for it — a protocol ships the
/// finished table and both ends read from it. So the notion the engine needs is the smaller one, and the
/// table is data like any other.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol converter set — a codec law, not a product node")]
public class PrefixCodeTests
{
    private static ProtoValue R(long symbol, long code, int bits)
        => new ProtoValue.List([ProtoValue.Of(symbol), ProtoValue.Of(code), ProtoValue.Of((long)bits)]);

    /// <summary>
    /// RFC 7541 Appendix B, verbatim: every octet value, plus symbol 256 for the end of a run.
    /// </summary>
    /// <remarks>
    /// Transcribed data is the kind that goes wrong quietly, so two properties are asserted about it below
    /// rather than assumed — that it is a <i>complete</i> code (Kraft equality, which pins the multiset of
    /// lengths) and that it is the <i>canonical</i> assignment for those lengths (which pins every
    /// individual code). Together they mean a single mistyped row cannot survive, including among the
    /// hundred and eighty symbols no published test vector ever exercises.
    /// </remarks>
    internal static readonly ProtoValue HeaderCode = new ProtoValue.List(
    [
        R(0, 0x1ff8, 13), R(1, 0x7fffd8, 23), R(2, 0xfffffe2, 28), R(3, 0xfffffe3, 28),
        R(4, 0xfffffe4, 28), R(5, 0xfffffe5, 28), R(6, 0xfffffe6, 28), R(7, 0xfffffe7, 28),
        R(8, 0xfffffe8, 28), R(9, 0xffffea, 24), R(10, 0x3ffffffc, 30), R(11, 0xfffffe9, 28),
        R(12, 0xfffffea, 28), R(13, 0x3ffffffd, 30), R(14, 0xfffffeb, 28), R(15, 0xfffffec, 28),
        R(16, 0xfffffed, 28), R(17, 0xfffffee, 28), R(18, 0xfffffef, 28), R(19, 0xffffff0, 28),
        R(20, 0xffffff1, 28), R(21, 0xffffff2, 28), R(22, 0x3ffffffe, 30), R(23, 0xffffff3, 28),
        R(24, 0xffffff4, 28), R(25, 0xffffff5, 28), R(26, 0xffffff6, 28), R(27, 0xffffff7, 28),
        R(28, 0xffffff8, 28), R(29, 0xffffff9, 28), R(30, 0xffffffa, 28), R(31, 0xffffffb, 28),
        R(32, 0x14, 6), R(33, 0x3f8, 10), R(34, 0x3f9, 10), R(35, 0xffa, 12),
        R(36, 0x1ff9, 13), R(37, 0x15, 6), R(38, 0xf8, 8), R(39, 0x7fa, 11),
        R(40, 0x3fa, 10), R(41, 0x3fb, 10), R(42, 0xf9, 8), R(43, 0x7fb, 11),
        R(44, 0xfa, 8), R(45, 0x16, 6), R(46, 0x17, 6), R(47, 0x18, 6),
        R(48, 0x0, 5), R(49, 0x1, 5), R(50, 0x2, 5), R(51, 0x19, 6),
        R(52, 0x1a, 6), R(53, 0x1b, 6), R(54, 0x1c, 6), R(55, 0x1d, 6),
        R(56, 0x1e, 6), R(57, 0x1f, 6), R(58, 0x5c, 7), R(59, 0xfb, 8),
        R(60, 0x7ffc, 15), R(61, 0x20, 6), R(62, 0xffb, 12), R(63, 0x3fc, 10),
        R(64, 0x1ffa, 13), R(65, 0x21, 6), R(66, 0x5d, 7), R(67, 0x5e, 7),
        R(68, 0x5f, 7), R(69, 0x60, 7), R(70, 0x61, 7), R(71, 0x62, 7),
        R(72, 0x63, 7), R(73, 0x64, 7), R(74, 0x65, 7), R(75, 0x66, 7),
        R(76, 0x67, 7), R(77, 0x68, 7), R(78, 0x69, 7), R(79, 0x6a, 7),
        R(80, 0x6b, 7), R(81, 0x6c, 7), R(82, 0x6d, 7), R(83, 0x6e, 7),
        R(84, 0x6f, 7), R(85, 0x70, 7), R(86, 0x71, 7), R(87, 0x72, 7),
        R(88, 0xfc, 8), R(89, 0x73, 7), R(90, 0xfd, 8), R(91, 0x1ffb, 13),
        R(92, 0x7fff0, 19), R(93, 0x1ffc, 13), R(94, 0x3ffc, 14), R(95, 0x22, 6),
        R(96, 0x7ffd, 15), R(97, 0x3, 5), R(98, 0x23, 6), R(99, 0x4, 5),
        R(100, 0x24, 6), R(101, 0x5, 5), R(102, 0x25, 6), R(103, 0x26, 6),
        R(104, 0x27, 6), R(105, 0x6, 5), R(106, 0x74, 7), R(107, 0x75, 7),
        R(108, 0x28, 6), R(109, 0x29, 6), R(110, 0x2a, 6), R(111, 0x7, 5),
        R(112, 0x2b, 6), R(113, 0x76, 7), R(114, 0x2c, 6), R(115, 0x8, 5),
        R(116, 0x9, 5), R(117, 0x2d, 6), R(118, 0x77, 7), R(119, 0x78, 7),
        R(120, 0x79, 7), R(121, 0x7a, 7), R(122, 0x7b, 7), R(123, 0x7ffe, 15),
        R(124, 0x7fc, 11), R(125, 0x3ffd, 14), R(126, 0x1ffd, 13), R(127, 0xffffffc, 28),
        R(128, 0xfffe6, 20), R(129, 0x3fffd2, 22), R(130, 0xfffe7, 20), R(131, 0xfffe8, 20),
        R(132, 0x3fffd3, 22), R(133, 0x3fffd4, 22), R(134, 0x3fffd5, 22), R(135, 0x7fffd9, 23),
        R(136, 0x3fffd6, 22), R(137, 0x7fffda, 23), R(138, 0x7fffdb, 23), R(139, 0x7fffdc, 23),
        R(140, 0x7fffdd, 23), R(141, 0x7fffde, 23), R(142, 0xffffeb, 24), R(143, 0x7fffdf, 23),
        R(144, 0xffffec, 24), R(145, 0xffffed, 24), R(146, 0x3fffd7, 22), R(147, 0x7fffe0, 23),
        R(148, 0xffffee, 24), R(149, 0x7fffe1, 23), R(150, 0x7fffe2, 23), R(151, 0x7fffe3, 23),
        R(152, 0x7fffe4, 23), R(153, 0x1fffdc, 21), R(154, 0x3fffd8, 22), R(155, 0x7fffe5, 23),
        R(156, 0x3fffd9, 22), R(157, 0x7fffe6, 23), R(158, 0x7fffe7, 23), R(159, 0xffffef, 24),
        R(160, 0x3fffda, 22), R(161, 0x1fffdd, 21), R(162, 0xfffe9, 20), R(163, 0x3fffdb, 22),
        R(164, 0x3fffdc, 22), R(165, 0x7fffe8, 23), R(166, 0x7fffe9, 23), R(167, 0x1fffde, 21),
        R(168, 0x7fffea, 23), R(169, 0x3fffdd, 22), R(170, 0x3fffde, 22), R(171, 0xfffff0, 24),
        R(172, 0x1fffdf, 21), R(173, 0x3fffdf, 22), R(174, 0x7fffeb, 23), R(175, 0x7fffec, 23),
        R(176, 0x1fffe0, 21), R(177, 0x1fffe1, 21), R(178, 0x3fffe0, 22), R(179, 0x1fffe2, 21),
        R(180, 0x7fffed, 23), R(181, 0x3fffe1, 22), R(182, 0x7fffee, 23), R(183, 0x7fffef, 23),
        R(184, 0xfffea, 20), R(185, 0x3fffe2, 22), R(186, 0x3fffe3, 22), R(187, 0x3fffe4, 22),
        R(188, 0x7ffff0, 23), R(189, 0x3fffe5, 22), R(190, 0x3fffe6, 22), R(191, 0x7ffff1, 23),
        R(192, 0x3ffffe0, 26), R(193, 0x3ffffe1, 26), R(194, 0xfffeb, 20), R(195, 0x7fff1, 19),
        R(196, 0x3fffe7, 22), R(197, 0x7ffff2, 23), R(198, 0x3fffe8, 22), R(199, 0x1ffffec, 25),
        R(200, 0x3ffffe2, 26), R(201, 0x3ffffe3, 26), R(202, 0x3ffffe4, 26), R(203, 0x7ffffde, 27),
        R(204, 0x7ffffdf, 27), R(205, 0x3ffffe5, 26), R(206, 0xfffff1, 24), R(207, 0x1ffffed, 25),
        R(208, 0x7fff2, 19), R(209, 0x1fffe3, 21), R(210, 0x3ffffe6, 26), R(211, 0x7ffffe0, 27),
        R(212, 0x7ffffe1, 27), R(213, 0x3ffffe7, 26), R(214, 0x7ffffe2, 27), R(215, 0xfffff2, 24),
        R(216, 0x1fffe4, 21), R(217, 0x1fffe5, 21), R(218, 0x3ffffe8, 26), R(219, 0x3ffffe9, 26),
        R(220, 0xffffffd, 28), R(221, 0x7ffffe3, 27), R(222, 0x7ffffe4, 27), R(223, 0x7ffffe5, 27),
        R(224, 0xfffec, 20), R(225, 0xfffff3, 24), R(226, 0xfffed, 20), R(227, 0x1fffe6, 21),
        R(228, 0x3fffe9, 22), R(229, 0x1fffe7, 21), R(230, 0x1fffe8, 21), R(231, 0x7ffff3, 23),
        R(232, 0x3fffea, 22), R(233, 0x3fffeb, 22), R(234, 0x1ffffee, 25), R(235, 0x1ffffef, 25),
        R(236, 0xfffff4, 24), R(237, 0xfffff5, 24), R(238, 0x3ffffea, 26), R(239, 0x7ffff4, 23),
        R(240, 0x3ffffeb, 26), R(241, 0x7ffffe6, 27), R(242, 0x3ffffec, 26), R(243, 0x3ffffed, 26),
        R(244, 0x7ffffe7, 27), R(245, 0x7ffffe8, 27), R(246, 0x7ffffe9, 27), R(247, 0x7ffffea, 27),
        R(248, 0x7ffffeb, 27), R(249, 0xffffffe, 28), R(250, 0x7ffffec, 27), R(251, 0x7ffffed, 27),
        R(252, 0x7ffffee, 27), R(253, 0x7ffffef, 27), R(254, 0x7fffff0, 27), R(255, 0x3ffffee, 26),
        R(256, 0x3fffffff, 30),
    ]);

    private static readonly Evaluator Eval = new();

    private static ProtoValue Run(string expression)
        => Eval.Eval(expression, new EvalScope().Set("table", HeaderCode));

    private static (long Symbol, long Code, int Bits)[] Rows()
        => [.. HeaderCode.AsList().Select(r => r.AsList())
                         .Select(p => (p[0].AsInt(), p[1].AsInt(), (int)p[2].AsInt()))];

    // ── What the table itself has to be ───────────────────────────────────────

    [TestMethod]
    public void The_table_covers_every_octet_once_and_leaves_no_bit_pattern_spare()
    {
        var rows = Rows();

        CollectionAssert.AreEqual(Enumerable.Range(0, 257).Select(i => (long)i).ToArray(),
            rows.Select(r => r.Symbol).Order().ToArray(),
            "every octet value, and the one symbol that is not an octet");

        // Kraft equality. A prefix code can fall short of this and still be readable, but a code somebody
        // published is complete — so the sum being exactly one is a check on the lengths, and a mistyped
        // width breaks it even when the code it carries is perfectly well formed.
        long spare = rows.Sum(r => 1L << (30 - r.Bits));

        Assert.AreEqual(1L << 30, spare,
            "the code lengths do not add up to a complete code, so a row has the wrong width");
    }

    [TestMethod]
    public void And_is_the_canonical_assignment_for_those_lengths()
    {
        // How a published table of this kind is built, and therefore how to check one was copied down
        // correctly: order by width then by symbol, count up, shift left at each change of width. Every
        // code follows from the lengths, so this catches a transposed digit in a row nothing else reads.
        long code = 0;
        int width = 0;
        List<string> wrong = [];

        foreach (var row in Rows().OrderBy(r => r.Bits).ThenBy(r => r.Symbol))
        {
            if (width != 0) code <<= row.Bits - width;
            width = row.Bits;

            if (row.Code != code)
                wrong.Add($"symbol {row.Symbol} at {row.Bits} bits is 0x{row.Code:x}, "
                        + $"and the canonical code there is 0x{code:x}");
            code++;
        }

        Assert.AreEqual(0, wrong.Count, string.Join("\n  • ", wrong));
    }

    // ── The vectors the specification prints ──────────────────────────────────

    /// <summary>
    /// RFC 7541 Appendix C, the encoded string literals — four independent confirmations that the table
    /// and the packing agree with the people who wrote them down.
    /// </summary>
    [DataTestMethod]
    [DataRow("www.example.com", "f1e3c2e5f23a6ba0ab90f4ff")]
    [DataRow("no-cache", "a8eb10649cbf")]
    [DataRow("custom-key", "25a849e95ba97d7f")]
    [DataRow("custom-value", "25a849e95bb8e8b4bf")]
    public void A_value_becomes_the_octets_the_specification_prints(string text, string expected)
    {
        Assert.AreEqual(expected, Run($"'{text}' |> unascii() |> packBits(table) |> hex()").AsText());

        // And back, which is the law that matters: the octets are the only thing the two ends share, and a
        // codec that writes them and cannot read them is half a codec.
        Assert.AreEqual(text, Run($"'{expected}' |> unhex() |> unpackBits(table) |> ascii()").AsText());
    }

    [TestMethod]
    public void A_run_that_ends_mid_octet_is_filled_from_the_end_of_stream_code()
    {
        // 'a' is five bits, so three are left over and they come from the top of symbol 256's code.
        Assert.AreEqual("1f", Run("'a' |> unascii() |> packBits(table) |> hex()").AsText());
    }

    // ── What a reader refuses ─────────────────────────────────────────────────

    private static string Refused(string expression)
        => Assert.ThrowsExactly<ProtoTypeException>(() => Run(expression)).Message;

    [TestMethod]
    public void Padding_wide_enough_to_have_held_a_symbol_is_a_malformed_value()
    {
        // The same 'a', with one more octet of ones after it. It decodes to the same symbol and is not the
        // same value, which is the whole objection — two spellings of one thing, and only one of them can
        // be what gets written back.
        StringAssert.Contains(Refused("'1fff' |> unhex() |> unpackBits(table)"),
            "enough to have carried another symbol");
    }

    [TestMethod]
    public void Padding_that_is_not_the_end_of_stream_prefix_is_refused()
    {
        // '1e' is 'a' followed by 110 where the table says 111.
        StringAssert.Contains(Refused("'1e' |> unhex() |> unpackBits(table)"),
            "not the leading bits of the end-of-stream code");
    }

    [TestMethod]
    public void The_end_of_stream_symbol_inside_a_value_is_refused()
    {
        // Thirty ones, then two more. The run has been terminated by hand.
        StringAssert.Contains(Refused("'ffffffff' |> unhex() |> unpackBits(table)"),
            "appears inside the value");
    }

    // ── What a table has to be, checked where it is read ──────────────────────

    private static string Rejected(string table)
        => Assert.ThrowsExactly<ProtoTypeException>(
               () => Eval.Eval($"'00' |> unhex() |> packBits({table})", new EvalScope())).Message;

    [TestMethod]
    public void A_table_with_no_end_of_stream_row_cannot_finish_a_run()
    {
        StringAssert.Contains(Rejected("list(list(0, 0, 1), list(1, 2, 2))"), "no symbol 256");
    }

    [TestMethod]
    public void A_table_where_one_code_leads_another_is_refused()
    {
        // 0 and 01: a reader that has seen a zero cannot tell whether it is finished.
        StringAssert.Contains(
            Rejected("list(list(0, 0, 1), list(1, 1, 2), list(256, 127, 7))"), "prefix-free");
    }

    [TestMethod]
    public void A_table_naming_one_symbol_twice_is_refused()
    {
        StringAssert.Contains(
            Rejected("list(list(0, 0, 1), list(0, 2, 2), list(256, 127, 7))"), "appears twice");
    }

    [TestMethod]
    public void An_end_of_stream_code_too_short_to_pad_with_is_refused()
    {
        StringAssert.Contains(Rejected("list(list(0, 0, 1), list(256, 3, 2))"), "padding can need 7");
    }

    // ── The notion, not the protocol ──────────────────────────────────────────

    [TestMethod]
    public void A_three_symbol_code_works_exactly_the_same_way()
    {
        // Nothing above is about HPACK except the table. Hand the same converter a different code and it
        // is a different codec — which is the property that keeps a specification's appendix out of the
        // engine.
        const string Tiny = "list(list(0, 0, 1), list(1, 2, 2), list(256, 127, 7))";

        Assert.AreEqual("5f",
            Eval.Eval($"'0001' |> unhex() |> packBits({Tiny}) |> hex()", new EvalScope()).AsText());
        Assert.AreEqual("0001",
            Eval.Eval($"'5f' |> unhex() |> unpackBits({Tiny}) |> hex()", new EvalScope()).AsText());
    }
}
