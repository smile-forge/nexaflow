using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Round-trip laws for every converter that declares an inverse.
///
/// <para>
/// A declared inverse is an assertion, and until something checks it, it is only a comment that happens to
/// live in a field. Two laws, borrowed from the partial-isomorphism literature rather than from lenses —
/// we build bytes from values and hold no previous packet, so a lens's "original concrete value" argument
/// has nothing to bind to:
/// </para>
/// <list type="number">
///   <item><description><b>Forward-round-trip</b>: <c>inverse(forward(x)) == x</c> for every <c>x</c> in
///   the declared domain. This is what semantic round-trip rests on.</description></item>
///   <item><description><b>Backward-round-trip</b>: <c>forward(inverse(y)) == y</c> for every canonical
///   <c>y</c>. Where it holds only up to a canonical form, the converter is a quotient and the
///   canonicaliser must be named — silence there is how a "byte-exact" claim becomes false.</description></item>
/// </list>
///
/// <para>
/// <b>The domain is the point.</b> Most of these laws are false over all inputs and true over the domain
/// the protocol actually uses; a property test with no declared domain is testing a false proposition and
/// will either pass by luck or fail meaninglessly. Each case below carries its domain explicitly.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("round-trip laws for the converter set — no single product node")]
public class ConverterInverseLawTests
{
    private static readonly Evaluator Eval = new();

    private static ProtoValue Run(string expr) => Eval.Eval(expr, new EvalScope());

    /// <summary>One law case: an expression that must reproduce its own input.</summary>
    /// <param name="Converter">Which converter pair is under test.</param>
    /// <param name="Domain">The declared domain, in words — what makes the law true.</param>
    /// <param name="Cases">(round-trip expression, expected literal) pairs drawn from that domain.</param>
    private sealed record Law(string Converter, string Domain, (string Expr, string Expect)[] Cases);

    private static readonly Law[] Laws =
    [
        new("hex / unhex", "canonical lower-case hex with no separators",
        [
            ("'0c8a9b' |> unhex() |> hex()", "0c8a9b"),
            ("'00' |> unhex() |> hex()", "00"),
            ("'ffffffffffff' |> unhex() |> hex()", "ffffffffffff"),
        ]),

        new("ascii / unascii", "printable ASCII",
        [
            ("'public' |> unascii() |> ascii()", "public"),
            ("'M-SEARCH * HTTP/1.1' |> unascii() |> ascii()", "M-SEARCH * HTTP/1.1"),
        ]),

        new("mac / unmac", "six octets",
        [
            ("'aa:bb:cc:dd:ee:ff' |> mac() |> unmac()", "aa:bb:cc:dd:ee:ff"),
            ("'00:00:00:00:00:00' |> mac() |> unmac()", "00:00:00:00:00:00"),
        ]),

        new("ipv4 / unipv4", "dotted quad",
        [
            ("'192.168.1.255' |> ipv4() |> unipv4()", "192.168.1.255"),
            ("'0.0.0.0' |> ipv4() |> unipv4()", "0.0.0.0"),
        ]),

        new("minint / unminint", "any i64 — minimal two's-complement is total and injective",
        [
            ("821915 |> minint() |> unminint()", "821915"),
            ("0 |> minint() |> unminint()", "0"),
            ("127 |> minint() |> unminint()", "127"),
            ("255 |> minint() |> unminint()", "255"),
            ("0 - 1 |> minint() |> unminint()", "-1"),
            ("0 - 128 |> minint() |> unminint()", "-128"),
        ]),

        new("minuint / unminuint", "non-negative, with the zero rule declared",
        [
            ("9740722 |> minuint('oneByte') |> unminuint()", "9740722"),
            ("50 |> minuint('oneByte') |> unminuint()", "50"),
            ("0 |> minuint('oneByte') |> unminuint()", "0"),
        ]),

        new("base128 / unbase128", "non-negative, with the group order declared",
        [
            ("2021 |> base128('msbFirst') |> unbase128('msbFirst')", "2021"),
            ("143 |> base128('lsbFirst') |> unbase128('lsbFirst')", "143"),
            ("0 |> base128('msbFirst') |> unbase128('msbFirst')", "0"),
            ("268435455 |> base128('lsbFirst') |> unbase128('lsbFirst')", "268435455"),
        ]),

        // No hierarchical-identifier entry: that encoding is a document-level transform now, and its round
        // trip is verified against its own declared domain in TransformLanguageTests. A converter here
        // would be one family's rule with a general name.

        new("decimal / undecimal", "non-negative integers, no padding",
        [
            ("1900 |> decimal() |> undecimal()", "1900"),
            ("0 |> decimal() |> undecimal()", "0"),
        ]),

        new("split / join", "no separator occurrence inside an element",
        [
            ("'a.b.c' |> split('.') |> join('.')", "a.b.c"),
        ]),

        new("fit / cstr", "text shorter than the width and containing no NUL",
        [
            ("'boot' |> fit(64) |> cstr()", "boot"),
            ("'' |> fit(16) |> cstr()", ""),
        ]),

        new("reverse", "self-inverse on any octet run",
        [
            ("'0102030405' |> unhex() |> reverse() |> reverse() |> hex()", "0102030405"),
        ]),

        new("xor", "self-inverse for a fixed key",
        [
            ("'0102030405' |> unhex() |> xor('ff' |> unhex()) |> xor('ff' |> unhex()) |> hex()", "0102030405"),
        ]),
    ];

    [TestMethod]
    public void Every_declared_inverse_satisfies_the_forward_round_trip_law()
    {
        List<string> broken = [];

        foreach (var law in Laws)
            foreach (var (expr, expect) in law.Cases)
            {
                string actual;
                try { actual = Run(expr).ToString(); }
                catch (Exception ex) { broken.Add($"{law.Converter}: {expr} threw {ex.GetType().Name}: {ex.Message}"); continue; }

                if (!string.Equals(actual, expect, StringComparison.Ordinal))
                    broken.Add($"{law.Converter}: {expr}\n      expected '{expect}', got '{actual}'"
                             + $"\n      domain: {law.Domain}");
            }

        Assert.AreEqual(0, broken.Count,
            "A declared inverse that does not round-trip is a false claim the engine relies on.\n\n"
          + string.Join("\n  • ", broken));
    }

    [TestMethod]
    public void Every_converter_declaring_an_inverse_names_one_that_exists_and_points_back()
    {
        // A one-way declaration is how `fit` came to name `cstr`: different arity, different types, and
        // nothing anywhere that would notice.
        List<string> broken = [];
        var byName = ConverterTable.Default.All.ToDictionary(c => c.Name, StringComparer.Ordinal);

        foreach (var c in ConverterTable.Default.All)
        {
            if (c.Inverse is null) continue;

            if (!byName.TryGetValue(c.Inverse, out var inv))
            {
                broken.Add($"'{c.Name}' declares inverse '{c.Inverse}', which is not a registered converter");
                continue;
            }

            if (inv.Inverse is null || !string.Equals(inv.Inverse, c.Name, StringComparison.Ordinal))
                broken.Add($"'{c.Name}' declares inverse '{inv.Name}', but '{inv.Name}' declares "
                         + $"'{inv.Inverse ?? "none"}' — an inverse relation must be mutual");
        }

        Assert.AreEqual(0, broken.Count, string.Join("\n  • ", broken));
    }

    [TestMethod]
    public void A_converter_pair_that_is_not_actually_invertible_does_not_claim_to_be()
    {
        // Shifting is the case that caught this out. `shr(shl(x, k), k) == x` only while no high bit is
        // lost, and `shl(shr(x, k), k) != x` for ANY x with low bits set — so the law fails in one
        // direction for essentially every input. Declaring the pair as inverses was simply false, and no
        // test could have noticed while the laws went unchecked.
        var shl = ConverterTable.Default.All.Single(c => c.Name == "shl");
        var shr = ConverterTable.Default.All.Single(c => c.Name == "shr");

        Assert.IsNull(shl.Inverse, "shl loses high bits; it has no inverse");
        Assert.IsNull(shr.Inverse, "shr loses low bits; it has no inverse");

        // And the arithmetic that proves it, so the reason survives the next reader.
        Assert.AreEqual(4, Run("5 |> shr(1) |> shl(1)").AsInt(), "5 >> 1 << 1 is 4, not 5");
    }

    [TestMethod]
    public void Every_digest_is_a_derivation_rather_than_a_broken_bijection()
    {
        // The distinction that halves the transform problem: a derived value is never inverted, it is
        // recomputed and compared. A digest declaring an inverse would be a false claim; a digest treated
        // as merely "one-way" is a field we read and ignore, which is how a corrupt capture decodes
        // cleanly. Neither — they are Derivations, and on decode they are checked.
        foreach (var name in (string[])["md5", "sha1", "sha256", "hmacSha256",
                                        "crc16", "crc32", "onesComplementSum"])
        {
            var c = ConverterTable.Default.All.Single(x => x.Name == name);
            Assert.IsNull(c.Inverse, $"{name} cannot declare an inverse");
            Assert.AreEqual(ConverterRole.Derivation, c.Role, name);
        }
    }

    [TestMethod]
    public void Every_encoding_pair_is_classified_as_a_bijection()
    {
        foreach (var name in (string[])["hex", "unhex", "ascii", "unascii", "mac", "unmac",
                                        "minint", "unminint", "base128", "unbase128",
                                        "fixed", "unfixed"])
            Assert.AreEqual(ConverterRole.Bijection, ConverterTable.Default.All.Single(x => x.Name == name).Role,
                $"{name} is a reversible encoding and must be verified by the round-trip laws");
    }

    [TestMethod]
    public void A_lossy_operation_is_not_offered_as_a_transform()
    {
        // Shifting, slicing and clamping are legal inside an expression and must never be the transform on
        // a carries edge — a value that cannot survive the round trip cannot define one.
        foreach (var name in (string[])["shl", "shr"])
            Assert.AreNotEqual(ConverterRole.Bijection, ConverterTable.Default.All.Single(x => x.Name == name).Role,
                $"{name} loses bits");
    }

    [TestMethod]
    public void A_quotient_converter_declares_which_form_is_canonical()
    {
        // `unhex` deliberately accepts separators, so `hex(unhex("aa:bb")) == "aabb"` — the backward law
        // holds only up to a canonical form. That is a quotient, and it is legitimate; what is not
        // legitimate is leaving it unsaid, because a byte-exact claim then quietly becomes false.
        Assert.AreEqual("aabb", Run("'aa:bb' |> unhex() |> hex()").ToString());
        Assert.AreEqual("aabb", Run("'AA BB' |> unhex() |> hex()").ToString());
        Assert.AreEqual("aabb", Run("'aabb' |> unhex() |> hex()").ToString(),
            "…and lower-case unseparated hex is the canonical form, which is stable");
    }
}
