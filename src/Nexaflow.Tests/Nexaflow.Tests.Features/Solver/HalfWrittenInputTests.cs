using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using Nexaflow.Features.Solver.Solving;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Solver;

/// <summary>
/// What the solvers are asked while a formula is being written.
///
/// <para>
/// The definition is re-read on every keystroke, so nearly everything the engine would be handed is
/// half-written — <c>sqrt(x^2+1</c> on the way to <c>sqrt(x^2+1)</c>, and again on the way back when
/// a character is deleted. None of it has a chip: an expression that is not finished cannot be
/// evaluated, simplified or solved. Recognising that before the engine is asked is the point — it
/// saves three solvers a parse each on every keystroke, and it keeps a stream of unfinished input out
/// of a parser that recovers from it noisily.
/// </para>
/// <para>
/// How noisily is what made this worth a test. Backspacing at the end of <c>\sqrt{x^2+1}</c> used to
/// break into the debugger inside a generated ANTLR lambda: recovery leaves a rule's value unassigned
/// and the grammar's actions dereference it. AngouriMath turns that into a proper parse exception on
/// the way out, so nothing was ever broken — but a first-chance exception per keystroke stops a
/// debugging session dead, and none of those parses had an answer to give anyway.
/// </para>
/// </summary>
[TestClass]
[CoversNode("solver-parse")]
public class HalfWrittenInputTests
{
    /// <summary>Every prefix, suffix and single deletion of these — i.e. every state on the way in or out.</summary>
    private static readonly string[] Seeds =
    [
        @"\sqrt{x^2+1}", "sqrt(x^2+1)", "x^2+1", @"\frac{a}{b}",
        "2 + 2", "sin(x)/cos(x)", "{1, 2, 3}",
    ];

    [TestMethod]
    public void AFormulaTheRendererCouldNotReadNeverReachesTheEngine()
    {
        // The gate's whole promise, asserted by absence: while parsing something the renderer rejected,
        // the engine is not entered at all — no exception of any kind comes from inside it, not even
        // the parse exception it would otherwise have raised.
        var entered = new List<string>();
        var reading = string.Empty;

        void Watch(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception.StackTrace?.Contains("AngouriMath", StringComparison.Ordinal) != true) return;
            entered.Add($"\"{reading}\" reached the engine ({e.Exception.GetType().Name})");
        }

        AppDomain.CurrentDomain.FirstChanceException += Watch;
        try
        {
            foreach (var (mode, candidate) in EveryStateOnTheWay())
            {
                var input = new SolverInput(mode, candidate, AngleUnit.Radians, 6);
                if (!ExpressionParser.IsStillBeingWritten(input)) continue;   // the renderer was content

                reading = candidate;
                Assert.IsFalse(ExpressionParser.TryParse(input, AngleUnit.Radians, out _),
                    $"\"{candidate}\" cannot be read, so it cannot be solved");
            }
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= Watch; }

        Assert.AreEqual(0, entered.Count,
            "these were rejected on screen and still went to the engine:\n  "
            + string.Join("\n  ", entered.Take(8)));
    }

    [TestMethod]
    public void NothingHalfWrittenEverThrows()
    {
        // What the gate cannot promise, the caller still must: the renderer has no opinion on whether
        // a "+" has a right-hand side, so "\sqrt{x^2+}" typesets perfectly and reaches the engine
        // anyway. It has to come back as "no chips" rather than as an exception, whichever of the two
        // readings rejected it.
        foreach (var (mode, candidate) in EveryStateOnTheWay())
        {
            var input = new SolverInput(mode, candidate, AngleUnit.Radians, 6);
            try { ExpressionParser.TryParse(input, AngleUnit.Radians, out _); }
            catch (Exception e) { Assert.Fail($"\"{candidate}\" threw {e.GetType().Name}: {e.Message}"); }
        }
    }

    [TestMethod]
    public void AFinishedFormulaStillParses()
    {
        // The guard against making the gate pass by rejecting everything — which would show up as
        // chips quietly never appearing.
        (DefinitionMode Mode, string Text)[] finished =
        [
            (DefinitionMode.Calc, "2 + 2"),
            (DefinitionMode.Calc, "sqrt(x^2+1)"),
            (DefinitionMode.Calc, "sin(x)/cos(x)"),
            (DefinitionMode.Calc, "-x + 1"),          // a leading minus is unary, not unfinished
            (DefinitionMode.Calc, "5!"),              // a trailing factorial is postfix, not dangling
            (DefinitionMode.Latex, @"\frac{a}{b}"),
            (DefinitionMode.Latex, @"\sqrt{x^2+1}"),
        ];

        foreach (var (mode, text) in finished)
            Assert.IsTrue(
                ExpressionParser.TryParse(new SolverInput(mode, text, AngleUnit.Radians, 6), AngleUnit.Radians, out _),
                text);
    }

    [TestMethod]
    public void UnfinishedMeansWhateverTheRendererCouldNotRead()
    {
        // The same verdict the reader is being shown: what wears a red wave on screen is exactly what
        // has no chip. There is no second opinion about what counts as a finished formula, which is
        // the point — two graders would disagree the moment either learned something.
        foreach (var unfinished in new[] { @"\sqrt{x^2+1", @"\frac{a}", @"\nosuchcommand", @"x^{2" })
            Assert.IsTrue(Unfinished(unfinished), unfinished);

        foreach (var whole in new[] { @"\sqrt{x^2+1}", @"\frac{a}{b}", "x^2", @"\alpha + 1" })
            Assert.IsFalse(Unfinished(whole), whole);

        static bool Unfinished(string latex) =>
            ExpressionParser.IsStillBeingWritten(new SolverInput(DefinitionMode.Latex, latex, AngleUnit.Radians, 6));
    }

    /// <summary>Every prefix, suffix and single deletion of each seed, in the tab it would be typed in.</summary>
    private static IEnumerable<(DefinitionMode Mode, string Text)> EveryStateOnTheWay()
    {
        foreach (var seed in Seeds)
        {
            var mode = seed.Contains('\\') ? DefinitionMode.Latex : DefinitionMode.Calc;
            var states = new List<string> { seed };

            for (var i = 0; i < seed.Length; i++) states.Add(seed.Remove(i, 1));   // a delete anywhere
            for (var i = 1; i <= seed.Length; i++) states.Add(seed[..i]);           // typed this far
            for (var i = 0; i < seed.Length; i++) states.Add(seed[i..]);            // eaten from the front

            foreach (var state in states) yield return (mode, state);
        }
    }
}
