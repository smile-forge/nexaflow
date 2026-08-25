using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Solver.Solving;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Solver;

/// <summary>
/// The solvers themselves — the half of this feature that has nothing to do with WPF.
/// <para>
/// What these mostly guard is <b>restraint</b>. A solver that answers everything is worse than one
/// that answers less: the chip strip is a promise that pressing a chip produces an answer, so a chip
/// offered for input the solver will then refuse is a bug even though nothing threw. Half of what
/// follows therefore asserts that a chip is <i>absent</i>.
/// </para>
/// </summary>
[TestClass]
public class SolverEngineTests
{
    private static readonly SolverRegistry Registry = SolverRegistry.CreateDefault(SolverTestDoubles.Ai());

    private static IReadOnlyList<string> Labels(string text, DefinitionMode mode = DefinitionMode.Calc)
        => Registry.ChipsFor(new SolverInput(mode, text)).Select(c => c.Label).ToList();

    private static async Task<SolverResult> Run(
        string text, string label, DefinitionMode mode = DefinitionMode.Calc,
        AngleUnit unit = AngleUnit.Radians)
    {
        var input = new SolverInput(mode, text, unit);
        var chip = Registry.ChipsFor(input).FirstOrDefault(c => c.Label == label);
        Assert.IsNotNull(chip, $"no '{label}' chip was offered for '{text}'");
        return await Registry.SolveAsync(chip, input, CancellationToken.None);
    }

    // ── Evaluate ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-equals")]
    public async Task AnArithmeticExpressionIsWorkedOut()
    {
        var result = await Run("2+2*3", "=");

        Assert.IsFalse(result.IsError);
        StringAssert.Contains(result.Markdown, "8", "precedence: 2+2*3 is 8, not 12");
    }

    [TestMethod]
    [CoversNode("solver-equals")]
    public async Task AnExactValueIsKeptAlongsideItsDecimal()
    {
        var result = await Run("sin(pi/4)", "=");

        StringAssert.Contains(result.Markdown, @"\sqrt{2}",
            "the answer is root two over two; showing only a decimal throws that away");
        StringAssert.Contains(result.Markdown, "0.707107");
        Assert.IsFalse(result.Markdown.Contains("0.70710678118654752440"),
            "the engine evaluates to ~100 digits — none of that belongs on screen");
    }

    [TestMethod]
    [CoversNode("solver-equals")]
    public void EvaluateIsNotOfferedForSomethingWithAnUnknownInIt()
        => CollectionAssert.DoesNotContain(Labels("4x + 3x").ToArray(), "=",
            "'=' promises a number, and a formula in x has none");

    // ── Algebra ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-algebra")]
    public async Task LikeTermsAreCollected()
    {
        var result = await Run("4x + 3x", "simplify");

        Assert.IsFalse(result.IsError);
        StringAssert.Contains(result.Markdown, "7 x");
    }

    [TestMethod]
    [CoversNode("solver-algebra")]
    public async Task APolynomialIsWrittenAsItsFactors()
    {
        var result = await Run("x^4 - 5x^2 + 4", "factor");

        Assert.IsFalse(result.IsError);
        foreach (var factor in new[] { "x+1", "x+2", "x-2", "x-1" })
            StringAssert.Contains(result.Markdown.Replace(" ", string.Empty), factor);
    }

    [TestMethod]
    [CoversNode("solver-algebra")]
    public async Task AnIrreduciblePolynomialComesBackUnchanged_AndSaysSo()
    {
        var result = await Run("x^2 + 1", "factor");

        Assert.IsFalse(result.IsError,
            "irreducible over the rationals is an answer, not a refusal — reporting it as an error would be wrong");
        StringAssert.Contains(result.Markdown, "Irreducible");
    }

    [TestMethod]
    [CoversNode("solver-algebra")]
    public void FactorIsNotOfferedForAMultivariatePolynomial()
        => CollectionAssert.DoesNotContain(Labels("x*y + y").ToArray(), "factor",
            "factorisation here is univariate; offering it would produce a refusal on press");

    [TestMethod]
    [CoversNode("solver-algebra")]
    public async Task AWholeNumberIsBrokenIntoPrimes()
    {
        var result = await Run("1872", "factor");

        Assert.IsFalse(result.IsError);
        var tight = result.Markdown.Replace(" ", string.Empty);
        StringAssert.Contains(tight, "2^{4}");
        StringAssert.Contains(tight, "3^{2}");
        StringAssert.Contains(tight, "13");
    }

    [TestMethod]
    [CoversNode("solver-algebra")]
    public async Task TheWorkingIsTheEnginesOwn_NotANarration()
    {
        var result = await Run("4x + 3x + 2x", "steps");

        Assert.IsFalse(result.IsError);
        StringAssert.Contains(result.Markdown, @"\begin{aligned}");
        StringAssert.Contains(result.Markdown, "9 x", "the chain has to end on the answer");
    }

    // ── Calculus ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-calculus")]
    public async Task ADerivativeIsTakenAndTidied()
    {
        var result = await Run("x^2 + 3x", "d/dx");

        Assert.IsFalse(result.IsError);
        var tight = result.Markdown.Replace(" ", string.Empty);
        Assert.IsTrue(tight.Contains("3+2x") || tight.Contains("2x+3"), result.Markdown);
        Assert.IsFalse(tight.Contains(@"\cdot2}{4}"),
            "the cheap simplifier leaves (2x·2)/4 — correct, but nobody writes a derivative that way");
    }

    [TestMethod]
    [CoversNode("solver-calculus")]
    public async Task AnIntegralIsTaken()
    {
        var result = await Run("x^2 + a*x", "∫ dx");

        Assert.IsFalse(result.IsError);
        StringAssert.Contains(result.Markdown, "C", "an indefinite integral carries its constant");
    }

    [TestMethod]
    [CoversNode("solver-calculus")]
    public async Task AnIntegralWithNoClosedFormSaysSoRatherThanRenderingItself()
    {
        var result = await Run("e^(x^2)", "∫ dx");

        Assert.IsTrue(result.IsError,
            "the engine hands back an unevaluated integral, which typesets perfectly and would read as an answer");
        StringAssert.Contains(result.Markdown, "No closed form");
    }

    [TestMethod]
    [CoversNode("solver-calculus")]
    public void EveryFreeVariableGetsItsOwnChip()
    {
        var labels = Labels("x*y + y");

        CollectionAssert.Contains(labels.ToArray(), "d/dx");
        CollectionAssert.Contains(labels.ToArray(), "d/dy");
    }

    // ── Statistics ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-stats")]
    public async Task ASeriesGetsItsSummary()
    {
        var result = await Run("4, 8, 15, 16, 23, 42", "stats");

        Assert.IsFalse(result.IsError);
        StringAssert.Contains(result.Markdown, "| Mean | 18 |");
        StringAssert.Contains(result.Markdown, "| Median | 15.5 |");
        StringAssert.Contains(result.Markdown, "| Sum | 108 |");
    }

    [TestMethod]
    [CoversNode("solver-stats")]
    public async Task StandardDeviationUsesTheSampleDivisor()
    {
        // 4,8,15,16,23,42 → mean 18, Σ(x−x̄)² = 910. Sample variance divides by n−1 = 5 → 182,
        // so σ ≈ 13.4907. The population divisor would give 12.3153, which is the wrong answer for
        // data and the easiest thing in this file to get quietly wrong.
        var result = await Run("4, 8, 15, 16, 23, 42", "σ");

        StringAssert.Contains(result.Markdown, "13.490738");
    }

    [TestMethod]
    [CoversNode("solver-stats")]
    public void ProseIsNotMistakenForASeries()
        => Assert.IsFalse(StatsSolver.TryParseSeries("what is the mean of these", out _));

    [TestMethod]
    [CoversNode("solver-stats")]
    public void ASingleNumberIsNotASeries()
        => Assert.IsFalse(StatsSolver.TryParseSeries("42", out _),
            "one value has no mean worth reporting and no deviation at all");

    // ── Parsing ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-parse")]
    public void LatexIsRewrittenIntoSomethingTheEngineReads()
    {
        Assert.AreEqual("((x^(2))/(2)) + 3x", LatexNormalizer.ToInfix(@"\frac{x^2}{2} + 3x"));
        Assert.AreEqual("sqrt(x + 1)", LatexNormalizer.ToInfix(@"\sqrt{x + 1}"));
        Assert.AreEqual("((8)^(1/(3)))", LatexNormalizer.ToInfix(@"\sqrt[3]{8}"));
        Assert.AreEqual("2 * pi * r", LatexNormalizer.ToInfix(@"2 \cdot \pi \cdot r"));
        Assert.AreEqual("((1)/(2))", LatexNormalizer.ToInfix(@"\frac12"), @"\frac12 is legal LaTeX");
    }

    [TestMethod]
    [CoversNode("solver-parse")]
    public void SizingCommandsLeaveNoGapBehindThem()
    {
        // The engine's lexer reads a function name and its bracket as ONE token, so a stray space
        // from \left is the difference between parsing and not.
        Assert.AreEqual("ln(x)", LatexNormalizer.ToInfix(@"\ln\left(x\right)"));
    }

    [TestMethod]
    [CoversNode("solver-parse")]
    public void DegreesAreConvertedGoingInAndComingBackOut()
    {
        Assert.AreEqual("sin((45) * pi / 180)", TrigDegreeRewriter.ToRadians("sin(45)"));
        Assert.AreEqual("((arcsin(0.5)) * 180 / pi)", TrigDegreeRewriter.ToRadians("arcsin(0.5)"),
            "an inverse function returns an angle — converting only the input leaves it answering radians");
    }

    [TestMethod]
    [CoversNode("solver-parse")]
    public void HyperbolicFunctionsAreLeftAlone()
        => Assert.AreEqual("sinh(1)", TrigDegreeRewriter.ToRadians("sinh(1)"),
            "sinh takes a real number, not an angle");

    [TestMethod]
    [CoversNode("solver-parse")]
    public async Task AnAngleInDegreesIsShownAsItWasTyped()
    {
        var result = await Run("sin(45)", "=", DefinitionMode.Calc, AngleUnit.Degrees);

        StringAssert.Contains(result.Markdown, @"\sqrt{2}", "sin 45° is root two over two");
        Assert.IsFalse(result.Markdown.Contains("180"),
            "the ×π/180 is how it was computed, not what was asked — echoing it back answers a different question");
    }

    [TestMethod]
    [CoversNode("solver-parse")]
    public void ASentenceIsNotTreatedAsAlgebra()
    {
        var labels = Labels("what is the area of a circle", DefinitionMode.Text);

        CollectionAssert.DoesNotContain(labels.ToArray(), "simplify");
        Assert.IsFalse(labels.Any(l => l.StartsWith("d/d", StringComparison.Ordinal)),
            "prose parses as a pile of variables multiplied together, which offers d/dwhat and ∫ dthe");
        CollectionAssert.Contains(labels.ToArray(), "Solve", "the AI chips are the right answer for a sentence");
    }

    [TestMethod]
    [CoversNode("solver-parse")]
    public void AFormulaTypedInTheTextTabIsStillAlgebra()
        => CollectionAssert.Contains(Labels("4x + 3x", DefinitionMode.Text).ToArray(), "simplify");

    [TestMethod]
    [CoversNode("solver-parse")]
    public void AShortNameIsNotMistakenForAWord()
        => CollectionAssert.Contains(Labels("alpha + beta").ToArray(), "simplify",
            "two spelled-out Greek letters is a formula, not a sentence");

    // ── AI ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-ai-solve")]
    public async Task TheAiChipsAreOfferedForAnythingAtAll()
    {
        // These are the catch-all: a sentence, a formula the engine declined, a half-typed line.
        // If they ever stop appearing, the page has nothing to offer for the inputs it can least
        // handle itself.
        foreach (var (mode, text) in new (DefinitionMode, string)[]
        {
            (DefinitionMode.Text, "how many primes are below 100?"),
            (DefinitionMode.Calc, "2 +*"),
            (DefinitionMode.Latex, @"\int e^{x^2} dx"),
        })
        {
            var labels = Labels(text, mode);
            CollectionAssert.Contains(labels.ToArray(), "Solve", $"for '{text}'");
            CollectionAssert.Contains(labels.ToArray(), "Solve by steps", $"for '{text}'");
        }

        await Task.CompletedTask;
    }

    [TestMethod]
    [CoversNode("solver-ai-solve")]
    public async Task TheAnswerComesBackAsMarkdown()
    {
        var registry = SolverRegistry.CreateDefault(SolverTestDoubles.Ai("The answer is $42$."));
        var input = new SolverInput(DefinitionMode.Text, "what is six times seven?");
        var chip = registry.ChipsFor(input).First(c => c.Label == "Solve");

        var result = await registry.SolveAsync(chip, input, CancellationToken.None);

        Assert.IsFalse(result.IsError);
        StringAssert.Contains(result.Markdown, "$42$");
    }

    [TestMethod]
    [CoversNode("solver-ai-solve")]
    public async Task NoConfiguredModelIsReportedAsSomethingActionable()
    {
        var registry = SolverRegistry.CreateDefault(SolverTestDoubles.Ai(null));
        var input = new SolverInput(DefinitionMode.Text, "what is six times seven?");
        var chip = registry.ChipsFor(input).First(c => c.Label == "Solve");

        var result = await registry.SolveAsync(chip, input, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.Markdown, "Problem Solving",
            "an empty cell tells the user nothing; naming the ability tells them where to go");
    }

    // ── Registry ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-registry")]
    public void HalfTypedInputOffersNothingButTheAiChips()
    {
        var labels = Labels("2 +*");

        CollectionAssert.AreEquivalent(new[] { "Solve", "Solve by steps" }, labels.ToArray(),
            "the parser throws on this constantly — it must read as 'nothing recognises it yet'");
    }

    [TestMethod]
    [CoversNode("solver-registry")]
    public void AnEmptyDefinitionOffersNothingAtAll()
        => Assert.AreEqual(0, Labels("   ").Count);

    [TestMethod]
    [CoversNode("solver-registry")]
    public void OneMisbehavingSolverDoesNotEmptyTheStrip()
    {
        var registry = new SolverRegistry([new ExplodingSolver(), new EqualsSolver()]);

        var chips = registry.ChipsFor(new SolverInput(DefinitionMode.Calc, "2+2"));

        CollectionAssert.AreEquivalent(new[] { "=" }, chips.Select(c => c.Label).ToArray(),
            "a solver that throws on half-typed input must cost only its own chips");
    }

    [TestMethod]
    [CoversNode("solver-registry")]
    public async Task ASolverThatThrowsOnPressBecomesAnErrorCell_NotAnUnhandledException()
    {
        var registry = new SolverRegistry([new ExplodingSolver(offerAnyway: true)]);
        var input = new SolverInput(DefinitionMode.Calc, "2+2");
        var chip = new SolverChip("boom", "boom", "boom", string.Empty, string.Empty);

        var result = await registry.SolveAsync(chip, input, CancellationToken.None);

        Assert.IsTrue(result.IsError);
    }

    [TestMethod]
    [CoversNode("solver-registry")]
    public void ChipsComeBackInSolverOrder()
    {
        var labels = Labels("x^2 + 3x");

        Assert.IsTrue(
            labels.ToList().IndexOf("simplify") < labels.ToList().IndexOf("d/dx"),
            "algebra sits left of calculus, and the AI chips last, so the strip reads the same every time");
        Assert.AreEqual("Solve by steps", labels[^1]);
    }

    /// <summary>A solver that fails the way half-typed input makes real ones fail.</summary>
    private sealed class ExplodingSolver(bool offerAnyway = false) : ISolver
    {
        public string Id => "boom";
        public string DisplayName => "Exploding";
        public int Order => -1;

        public IReadOnlyList<SolverChip> CanSolve(SolverInput input)
            => offerAnyway ? [new SolverChip(Id, "boom", "boom", string.Empty, string.Empty)]
                           : throw new InvalidOperationException("half-typed");

        public Task<SolverResult> SolveAsync(SolverChip chip, SolverInput input, CancellationToken ct)
            => throw new InvalidOperationException("still broken");
    }
}
