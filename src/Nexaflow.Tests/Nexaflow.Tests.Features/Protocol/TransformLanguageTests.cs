using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Transforms;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The transform language, and the question it exists to answer: <b>can a protocol's arithmetic leave the
/// engine?</b>
///
/// <para>
/// A purity audit found that the engine's first-arc merge — <c>40x + y</c> with a saturating inverse — is
/// one encoding family's rule written in C#, with one corpus witness, unmovable because the converter set
/// is closed and the expression language could only <i>call</i> converters, never <i>define</i> one. It was
/// recorded as the single accepted piece of generalisation debt. These tests are the discharge.
/// </para>
///
/// <para>
/// The language is deliberately <b>total</b> rather than powerful: no recursion, no first-class functions,
/// no unbounded loop. Termination is a property of the syntax, which is what makes an AI-authored transform
/// safe to run and — because every body unrolls to a loop-free term — its round-trip law provable rather
/// than merely sampled.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol transform language — tree nodes land with the engine")]
public class TransformLanguageTests
{
    private static readonly Evaluator Eval = new();

    private static ProtoValue Run(string expr, EvalScope? scope = null)
        => Eval.Eval(expr, scope ?? new EvalScope());

    // ── The language itself ───────────────────────────────────────────────────

    [TestMethod]
    public void Let_binds_immutably_and_scopes_to_its_body()
    {
        Assert.AreEqual(30, Run("let x = 10 in x * 3").AsInt());
        Assert.AreEqual(13, Run("let x = 10 in let y = 3 in x + y").AsInt());

        // The inner binding shadows; the outer is untouched afterwards, because there is no assignment.
        Assert.AreEqual(12, Run("let x = 10 in (let x = 2 in x) + x").AsInt());
    }

    [TestMethod]
    public void Bounded_iteration_covers_map_filter_fold_and_scan()
    {
        Assert.AreEqual("[2, 4, 6]", Run("[1, 2, 3] |> map(x -> x * 2)").ToString());
        Assert.AreEqual("[1, 3]", Run("[1, 2, 3] |> filter(x -> x % 2 == 1)").ToString());
        Assert.AreEqual(6, Run("[1, 2, 3] |> fold(0, (acc, x) -> acc + x)").AsInt());
        Assert.AreEqual(2, Run("[1, 2, 3] |> findFirst(x -> x > 1)").AsInt());
        Assert.AreEqual("[1, 2]", Run("[1, 2, 3, 1] |> takeWhile(x -> x < 3)").ToString());

        // `scan` yields every intermediate accumulator — which is exactly what an encoding whose element N
        // depends on elements 0..N-1 needs, and why it is a primitive rather than something to fake.
        Assert.AreEqual("[1, 3, 6]", Run("[1, 2, 3] |> scan(0, (acc, x) -> acc + x)").ToString());
    }

    [TestMethod]
    public void An_accumulating_delta_encoding_is_expressible_with_scan()
    {
        // Absolute option numbers → the deltas a wire format carries, and back. The corpus protocol that
        // forced this needs element N's encoding to depend on every element before it.
        const string toDeltas = "let n = [11, 11, 15, 15, 17, 252] in "
                              + "range(n |> count(), 64) |> map(i -> n[i] - (i == 0 ? 0 : n[i - 1]))";
        Assert.AreEqual("[11, 0, 4, 0, 2, 235]", Run(toDeltas).ToString());

        // …and the inverse is a running sum, which is one `scan`.
        Assert.AreEqual("[11, 11, 15, 15, 17, 252]",
            Run("[11, 0, 4, 0, 2, 235] |> scan(0, (acc, d) -> acc + d)").ToString());
    }

    [TestMethod]
    public void There_are_no_first_class_functions_so_recursion_is_unwritable()
    {
        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => Run("let f = x -> x in f"));
        StringAssert.Contains(ex.Message, "recursion");
    }

    [TestMethod]
    public void Iteration_is_bounded_by_a_literal_so_totality_is_syntactic()
    {
        Assert.AreEqual("[0, 1, 2]", Run("range(3, 10)").ToString());

        // Exceeding the declared maximum is an error, never a silent truncation — a truncated loop would
        // produce a short, structurally valid, wrong message.
        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => Run("range(50, 10)"));
        StringAssert.Contains(ex.Message, "maximum");

        // And the maximum itself is capped, so a document cannot buy a long loop by writing a big bound.
        Assert.ThrowsExactly<ProtoTypeException>(() => Run("range(1, 100000000)"));
    }

    // ── Containment: a transform sees values, never the graph ─────────────────

    [TestMethod]
    public void A_transform_body_may_reference_only_its_own_parameters()
    {
        var reaching = new Transform
        {
            Name = "reaches-sideways",
            Subject = "value",
            Forward = Expr.Parse("value + capture.somethingElse"),
            Inverse = Expr.Parse("value"),
            Domain = Expr.Parse("true"),
        };

        var issues = reaching.Validate();
        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "'capture'");
        StringAssert.Contains(issues[0], "the graph's job",
            "the message must explain the rule, because the natural fix is to add a parameter for it");
    }

    [TestMethod]
    public void A_binding_or_lambda_parameter_is_not_a_free_reference()
    {
        // The containment check has to understand binders, or every transform using `let` would look like
        // it was reaching outside itself.
        var ok = new Transform
        {
            Name = "uses-bindings",
            Subject = "value",
            Forward = Expr.Parse("let doubled = value * 2 in [1, 2] |> map(x -> x + doubled) |> count()"),
            Inverse = Expr.Parse("value"),
            Domain = Expr.Parse("true"),
        };

        CollectionAssert.AreEqual(Array.Empty<string>(), ok.Validate().ToArray());
    }

    [TestMethod]
    public void A_bijection_must_declare_a_domain()
    {
        var noDomain = new Transform
        {
            Name = "undeclared",
            Forward = Expr.Parse("value * 2"),
            Inverse = Expr.Parse("value / 2"),
        };

        StringAssert.Contains(string.Join("\n", noDomain.Validate()), "must declare a domain");
    }

    // ── The discharge: protocol arithmetic, as documents ──────────────────────

    /// <summary>
    /// The first-arc merge, as a document. This is the construct the purity audit named as the engine's one
    /// irreducible protocol specific.
    /// </summary>
    private static readonly Transform FirstArcMerge = new()
    {
        Name = "firstArcMerge",
        Subject = "arcs",
        Summary = "the leading pair of a hierarchical identifier, merged into one value",

        Forward = Expr.Parse("arcs[0] * 40 + arcs[1]"),

        // The saturating term is what makes this NOT a plain divmod, and it is exactly why the domain
        // below is not the whole of the input type.
        Inverse = Expr.Parse("let first = min(arcs / 40, 2) in [first, arcs - first * 40]"),

        // 40x + y is not injective over all pairs: (1,40) and (2,0) both give 80. The law holds only here.
        Domain = Expr.Parse("arcs[0] >= 0 && arcs[0] <= 2 && arcs[1] >= 0 && (arcs[0] == 2 || arcs[1] <= 39)"),
    };

    /// <summary>Base-128 digits, most-significant group first, as a document. Needs bounded iteration —
    /// the thing a closed converter set could not express.</summary>
    private static readonly Transform Base128MsbFirst = new()
    {
        Name = "base128MsbFirst",
        Subject = "value",
        Summary = "a value spread across octets with a continue flag, most-significant group first",

        Forward = Expr.Parse(
            "let n = max(1, (bitLength(value) + 6) / 7) in "
          + "range(n, 10) "
          + "  |> map(i -> ((value >> (7 * (n - 1 - i))) band 0x7f) bor (i < n - 1 ? 0x80 : 0)) "
          + "  |> octets()"),

        Inverse = Expr.Parse("value |> unoctets() |> fold(0, (acc, b) -> (acc << 7) bor (b band 0x7f))"),

        Domain = Expr.Parse("value >= 0 && value < 72057594037927936"),   // 2^56, ten groups
    };

    [TestMethod]
    public void The_first_arc_merge_round_trips_inside_its_declared_domain()
    {
        CollectionAssert.AreEqual(Array.Empty<string>(), FirstArcMerge.Validate().ToArray());

        foreach (var (a, b, merged) in (( long, long, long )[])[(1, 3, 43), (1, 2, 42), (2, 100, 180), (0, 0, 0)])
        {
            var arcs = new ProtoValue.List([ProtoValue.Of(a), ProtoValue.Of(b)]);
            Assert.IsTrue(FirstArcMerge.InDomain(arcs), $"({a},{b}) should be in domain");

            var forward = FirstArcMerge.Apply(arcs);
            Assert.AreEqual(merged, forward.AsInt(), $"({a},{b})");
            Assert.AreEqual($"[{a}, {b}]", FirstArcMerge.Undo(forward).ToString());
        }
    }

    [TestMethod]
    public void The_first_arc_domain_excludes_the_pairs_where_the_merge_is_not_injective()
    {
        // (1,40) and (2,0) both encode to 80. Exactly one of them may be in the domain, or the round-trip
        // law is a false proposition — which is the entire argument for domains being declarable at all.
        var collidingA = new ProtoValue.List([ProtoValue.Of(1L), ProtoValue.Of(40L)]);
        var collidingB = new ProtoValue.List([ProtoValue.Of(2L), ProtoValue.Of(0L)]);

        Assert.AreEqual(80, FirstArcMerge.Apply(collidingA).AsInt());
        Assert.AreEqual(80, FirstArcMerge.Apply(collidingB).AsInt());

        Assert.IsFalse(FirstArcMerge.InDomain(collidingA), "(1,40) must be outside the domain");
        Assert.IsTrue(FirstArcMerge.InDomain(collidingB), "(2,0) is the representative that survives");
    }

    [TestMethod]
    public void A_document_authored_base128_agrees_with_the_engine_converter()
    {
        // The proof that this arithmetic can leave the engine: the document and the built-in must produce
        // identical bytes for every corpus value.
        CollectionAssert.AreEqual(Array.Empty<string>(), Base128MsbFirst.Validate().ToArray());

        foreach (long value in (long[])[0, 1, 127, 128, 2021, 16383, 16384, 821915])
        {
            var fromDocument = Base128MsbFirst.Apply(ProtoValue.Of(value)).ToString();
            var fromEngine = Run($"{value} |> base128('msbFirst')").ToString();

            Assert.AreEqual(fromEngine, fromDocument, $"value {value}");
            Assert.AreEqual(value, Base128MsbFirst.Undo(ProtoValue.Of(Convert.FromHexString(fromDocument))).AsInt());
        }

        // And the value the corpus actually carries.
        Assert.AreEqual("8f65", Base128MsbFirst.Apply(ProtoValue.Of(2021L)).ToString());
    }

    /// <summary>
    /// The whole hierarchical-identifier encoding, as a document — the construct that was the engine's one
    /// accepted piece of generalisation debt.
    ///
    /// <para>
    /// Forward: split into arcs, merge the leading pair, regroup every arc into continuation octets.
    /// Inverse: fold the octets back into arcs (a running accumulator that resets after each terminal
    /// octet), then undo the merge with the saturating rule. Nothing here is a notion the engine lacks.
    /// </para>
    /// </summary>
    private static readonly Transform ObjectIdentifier = new()
    {
        Name = "objectIdentifier",
        Subject = "value",
        Summary = "dotted hierarchical identifier ↔ octets: leading-pair merge plus per-arc base-128",

        Forward = Expr.Parse(
            "let arcs = value |> split('.') |> map(a -> a |> undecimal()) in "
          + "let head = arcs[0] * 40 + arcs[1] in "
          + "let rest = range((arcs |> count()) - 2, 128) |> map(i -> arcs[i + 2]) in "
          + "[[head], rest] |> flatten() "
          + "  |> map(v -> v |> base128('msbFirst') |> unoctets()) |> flatten() |> octets()"),

        Inverse = Expr.Parse(
            // A running accumulator that resets after each terminal octet; the terminal entries are the arcs.
            "let groups = value |> unoctets() |> scan([0, true], "
          + "     (st, b) -> [ (st[1] ? 0 : st[0]) * 128 + (b band 0x7f), (b band 0x80) == 0 ]) in "
          + "let arcs = groups |> filter(s -> s[1]) |> map(s -> s[0]) in "
          + "let first = min(arcs[0] / 40, 2) in "
          + "let rest = range((arcs |> count()) - 1, 128) |> map(i -> arcs[i + 1]) in "
          + "[[first, arcs[0] - first * 40], rest] |> flatten() "
          + "  |> map(a -> a |> decimal()) |> join('.')"),

        // The leading-pair merge is not injective over all pairs, so the law holds only here.
        Domain = Expr.Parse(
            "let arcs = value |> split('.') |> map(a -> a |> undecimal()) in "
          + "(arcs |> count()) >= 2 && arcs[0] >= 0 && arcs[0] <= 2 "
          + "&& (arcs[0] == 2 || arcs[1] <= 39) && (arcs |> all(a -> a >= 0))"),
    };

    [TestMethod]
    public void The_object_identifier_encoding_is_expressible_as_a_document()
    {
        CollectionAssert.AreEqual(Array.Empty<string>(), ObjectIdentifier.Validate().ToArray());

        // Straight from the corpus: sysDescr.0, and the vendor arc that needs two octets.
        foreach (var (dotted, hex) in (( string, string )[])
        [
            ("1.3.6.1.2.1.1.1.0",          "2b06010201010100"),
            ("1.3.6.1.2.1.1.3.0",          "2b06010201010300"),
            ("1.3.6.1.4.1.2021.10.1.3.1",  "2b060104018f650a010301"),
            ("1.3.6.1.4.1",                "2b06010401"),
            ("2.100.3",                    "813403"),
        ])
        {
            Assert.IsTrue(ObjectIdentifier.InDomain(ProtoValue.Of(dotted)), dotted);
            Assert.AreEqual(hex, ObjectIdentifier.Apply(ProtoValue.Of(dotted)).ToString(), $"encode {dotted}");
            Assert.AreEqual(dotted,
                ObjectIdentifier.Undo(ProtoValue.Of(Convert.FromHexString(hex))).ToString(), $"decode {hex}");
        }
    }

    [TestMethod]
    public void The_document_form_excludes_the_leading_pairs_where_the_merge_is_not_injective()
    {
        // Same non-injectivity as the bare arithmetic, now declared on the composed transform.
        Assert.IsFalse(ObjectIdentifier.InDomain(ProtoValue.Of("1.40.7")), "collides with 2.0.7");
        Assert.IsTrue(ObjectIdentifier.InDomain(ProtoValue.Of("2.0.7")));
        Assert.IsFalse(ObjectIdentifier.InDomain(ProtoValue.Of("3.1.1")), "no such leading arc");
        Assert.IsFalse(ObjectIdentifier.InDomain(ProtoValue.Of("1")), "an identifier needs at least two arcs");
    }

    [TestMethod]
    public void A_derivation_has_no_inverse_and_says_so_when_asked_for_one()
    {
        var length = new Transform
        {
            Name = "octetCount",
            Subject = "value",
            Forward = Expr.Parse("value |> len()"),
            Summary = "a length — recomputed and compared on decode, never inverted",
        };

        CollectionAssert.AreEqual(Array.Empty<string>(), length.Validate().ToArray());
        Assert.AreEqual(ConverterRole.Derivation, length.Role);
        Assert.AreEqual(3, length.Apply(ProtoValue.Of(new byte[] { 1, 2, 3 })).AsInt());

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => length.Undo(ProtoValue.Of(3L)));
        StringAssert.Contains(ex.Message, "recomputed and compared");
    }
}
