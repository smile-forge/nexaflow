using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Tests.Features.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// "Is this formula finished?" and "what does the renderer complain about?" are one question, asked of one
/// reading. A solver is only handed a formula that is well-formed, so a construct the renderer draws without
/// a word but the check calls unfinished is a formula the reader can see is fine, silently losing its chips.
/// <para>
/// Needs an STA thread for the typesetter's tables. It opens no window and takes no focus.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("solver-latex-fence")]
public class LatexSyntaxTests
{
    private const double Scale = 16;

    [TestMethod]
    public void TheCheckComplainsAboutExactlyWhatTheRendererComplainsAbout() => UiThread.Run(() =>
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);

            // Placeholders on, because the check asks for holes: an empty argument is unfinished to both.
            var drawn = LatexBuilder.Lay(latex, Scale, placeholders: true).Trouble
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(Said).OrderBy(s => s).ToList();
            var checkedOut = LatexSyntax.Check(latex).Select(Said).OrderBy(s => s).ToList();

            CollectionAssert.AreEqual(drawn, checkedOut,
                $"{what}: the check said [{string.Join("; ", checkedOut)}], the renderer [{string.Join("; ", drawn)}]");
        }
    });

    [DataTestMethod]
    [DataRow(@"a\ b")]
    [DataRow(@"x\hspace{1em}y")]
    [DataRow(@"\frac{1}{2}")]
    [DataRow(@"\sqrt{x}")]
    [DataRow(@"a \not= b")]
    public void WhatTheBuilderDrawsItselfIsFinished(string latex) => UiThread.Run(() =>
        Assert.IsTrue(LatexSyntax.IsWellFormed(latex),
            $"{latex}: {string.Join("; ", LatexSyntax.Check(latex).Select(Said))}"));

    private static string Said(Diagnostic d) => $"{d.Start}+{d.Length} {d.Message}";
}
