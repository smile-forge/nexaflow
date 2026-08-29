using System.Collections.Generic;
using System.Linq;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;
using XamlMath.Rendering;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Every construct the typesetter knows, gathered into a handful of formulas, each asked the questions
/// the whole feature rests on: does every piece of layout name a real part of the source, name a
/// <em>different</em> part from the piece holding it, and name something inside it?
///
/// <para>
/// This is the cheap standing version of the corpus sweep. The sweep reads a quarter of a million real
/// formulas and takes twenty minutes, which means in practice it runs when someone remembers; these run
/// in the ordinary suite. It cannot cover every nesting of everything — nothing can — but a construct
/// that breaks its own spans breaks them the first time it appears, and this is that first time for all
/// of them.
/// </para>
/// <para>
/// Every formula here must typeset. A construct that stops parsing is a regression whether or not its
/// spans are sound, so the list doubles as the record of what this typesetter supports.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-source-map")]
public class LatexConstructCoverageTests
{
    private const double Scale = 16;


    [TestMethod]
    public void EveryConstructTypesets() => UiThread.Run(() =>
    {
        foreach (var (what, latex) in LatexConstructs.Everything)
            Assert.IsNotNull(LatexLayout.Build(LatexConstructs.Flatten(latex), Scale), $"{what} no longer typesets");
    });

    [TestMethod]
    public void EveryConstructNestsItsNames() => UiThread.Run(() =>
    {
        // The same invariant asked of the finished tree rather than of the capture, so a repair that
        // failed to repair would still be caught.
        foreach (var (what, latex) in LatexConstructs.Everything)
        {
            var layout = LatexLayout.Build(LatexConstructs.Flatten(latex), Scale);
            Assert.IsNotNull(layout, what);

            foreach (var node in layout.Tree.Root.SelfAndDescendants().Where(n => n.SourceLength > 0))
            {
                Assert.IsFalse(
                    node.Ancestors().Any(a => a.SourceStart == node.SourceStart && a.SourceLength == node.SourceLength),
                    $"{what}: {node} repeats a name its ancestor carries");

                if (node.Parent is { SourceLength: > 0 } parent)
                    Assert.IsTrue(
                        node.SourceStart >= parent.SourceStart && node.SourceEnd() <= parent.SourceEnd(),
                        $"{what}: {node} names source outside its parent {parent}");
            }
        }
    });

}
