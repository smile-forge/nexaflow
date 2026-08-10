using Nexaflow.IO.Protocol.Converters;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The register of accepted generalisation debt.
///
/// <para>
/// A purity audit of the engine against the ten-protocol corpus found violations that the name-scanning
/// guard could not see, because the failure mode is subtler than a protocol name: <b>a general name, a
/// general node, and a parameter list missing exactly the parameter that distinguishes the two witnesses</b>
/// — so the implementation serves one protocol while the metadata claims two. Most were fixed by restoring
/// the missing parameter. One could not be, and it is recorded here rather than left silent.
/// </para>
///
/// <para>
/// The register exists because the alternative to writing debt down is not having less of it. An entry is
/// a commitment: it names the construct, why it resisted, and what would discharge it. The list is asserted
/// to be <b>exactly</b> this set, so a new violation fails the build and a fixed one must be deleted here.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("architecture guard — bounds accepted deviations from the no-protocol-specifics rule")]
public class GeneralisationDebtTests
{
    /// <param name="Construct">Where it lives.</param>
    /// <param name="Why">Why it could not be generalised.</param>
    /// <param name="Discharge">What would remove it.</param>
    private sealed record Debt(string Construct, string Why, string Discharge);

    /// <summary>
    /// <b>Empty, and that is the point.</b> The one entry this register ever held — a hierarchical
    /// identifier's leading-pair merge, sitting in the engine because a closed converter set gave it
    /// nowhere else to go — was discharged when the transform language landed. It is now a document,
    /// built from notions the engine already had, with its non-injective domain declared.
    /// </summary>
    private static readonly Debt[] Accepted = [];

    [TestMethod]
    public void The_accepted_debt_is_exactly_what_is_recorded()
    {
        // A fixed count rather than a soft warning: an unbounded register is a list nobody reads, and the
        // whole value is that adding to it requires a decision. At zero, that decision is maximally sharp.
        Assert.AreEqual(0, Accepted.Length,
            "The accepted-debt register changed. Adding an entry means the engine has taken on a protocol "
          + "specific — justify it here and in review, or fix it. Before accepting one, check the three "
          + "usual causes: a missing parameter (the notion is real but the parameter distinguishing its "
          + "witnesses was omitted), a protocol constant as a default, or something that genuinely belongs "
          + "in a document as a composition of existing notions.\n\n"
          + string.Join("\n", Accepted.Select(d => $"  • {d.Construct}\n      why: {d.Why}\n      out: {d.Discharge}")));
    }

    [TestMethod]
    public void Every_debt_entry_names_a_construct_that_still_exists()
    {
        // A register that outlives what it describes is worse than none: it teaches the next reader that
        // the rule is decorative.
        var converterNames = ConverterTable.Default.All.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var debt in Accepted.Where(d => d.Construct.StartsWith("ConverterTable.", StringComparison.Ordinal)))
        {
            var named = debt.Construct["ConverterTable.".Length..]
                .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            foreach (var name in named)
                Assert.IsTrue(converterNames.Contains(name),
                    $"the register still lists '{name}', which no longer exists — delete the entry");
        }
    }

    [TestMethod]
    public void Every_debt_entry_says_what_would_discharge_it()
    {
        foreach (var debt in Accepted)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(debt.Why), debt.Construct);
            Assert.IsFalse(string.IsNullOrWhiteSpace(debt.Discharge),
                $"{debt.Construct}: debt with no route out is not debt, it is a decision nobody made");
        }
    }

    [TestMethod]
    public void No_converter_carries_a_non_identity_default()
    {
        // The audit's third check. A default that is not the identity is one protocol's constant living in
        // the engine while the name stays clean — a polynomial, an epoch, a byte order, a zero-encoding.
        // Verified behaviourally: each of these must REFUSE to run without its argument.
        var mustRefuse = new (string Expr, string Parameter)[]
        {
            ("2021 |> base128()",              "group order"),
            ("0 |> minuint()",                 "zero encoding"),
            ("'aabb' |> unhex() |> crc16()",   "polynomial"),
            ("1.5 |> fixed(16)",               "fraction width"),
        };

        var evaluator = new Nexaflow.IO.Protocol.Expressions.Evaluator();

        foreach (var (expr, parameter) in mustRefuse)
        {
            var ex = Assert.ThrowsExactly<Nexaflow.IO.Protocol.Values.ProtoTypeException>(
                () => evaluator.Eval(expr, new Nexaflow.IO.Protocol.Expressions.EvalScope()),
                $"{expr} must require its {parameter} rather than defaulting it");

            StringAssert.Contains(ex.Message, "no default", expr);
        }
    }
}
