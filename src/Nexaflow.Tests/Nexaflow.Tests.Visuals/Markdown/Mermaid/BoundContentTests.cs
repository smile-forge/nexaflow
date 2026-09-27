using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using Nexaflow.Markdown.Binding;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A diagram whose content is bound (<c>{{Path}}</c> on a line of its own) is drawn from what the binding comes to, as if
/// it had been written there — and content that grows says so, and is drawn again, with nothing else on the page touched.
///
/// <para>How a binding is read into the tree is <c>MermaidBindingTests</c>; this is that, drawn.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid")]
public class BoundContentTests
{
    private const string Flowchart = "graph TD\n  a[\"Alpha\"] --> b[\"Beta\"]\n";

    private sealed class Host
    {
        public string Graph { get; set; } = Flowchart;
        public string More { get; set; } = "b --> c[\"Gamma\"]\n";
        public string Two { get; set; } = "b --> c[\"Gamma\"]\nc --> d[\"Delta\"]\n";
    }

    private static ContentElement Drawn(string source, IDataContext? data)
    {
        var element = Alone.Drawn("mermaid", source, StyleFormat.Dark, new ContentInputs(Data: data));

        element.Measure(new Size(900, 900));
        element.Arrange(new Rect(0, 0, 900, 900));
        element.UpdateLayout();
        return element;
    }

    private static List<string> Words(ContentElement element) =>
        [.. element.Laid.Root.SelfAndDescendants().Select(piece => piece.Words?.Glyphs.Text).OfType<string>()];

    /// <summary>The middle of the run of words saying <paramref name="says"/>.</summary>
    private static Point Middle(ContentElement element, string says)
    {
        var (_, where) = element.Laid.Tree.Root.Placed().First(at => at.Piece.Words?.Glyphs.Text == says);
        return new Point(where.X + (where.Width / 2), where.Y + (where.Height / 2));
    }

    /// <summary>What is picked out after <paramref name="act"/>, as the element tells whoever follows it.</summary>
    private static IReadOnlyList<ContentPick> Picked(ContentElement element, Action act)
    {
        IReadOnlyList<ContentPick> picked = [];
        element.SelectionChanged += (_, change) => picked = change.Picked;

        act();
        return picked;
    }

    private const string Grown = "graph TD\n  a[\"Alpha\"] --> b[\"Beta\"]\n  {{More}}\n";

    /// <summary>Lets whatever was handed to the element's dispatcher happen.</summary>
    private static void Settle(ContentElement element)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        element.UpdateLayout();
    }

    [TestMethod]
    public void AWholeDiagramBoundIsDrawnAsIfItWereWritten()
    {
        UiThread.Run(() =>
        {
            var bound = Drawn("{{Graph}}\n", new ReflectionDataContext(new Host()));
            var written = Drawn(Flowchart, data: null);

            CollectionAssert.AreEqual(Words(written), Words(bound));
            Assert.AreEqual("{{Graph}}\n", bound.Source, "what is drawn is a reading of the source, never a rewrite of it");
        });
    }

    [TestMethod]
    public void LinesBoundAfterTheHeaderAreLinesOfThatDiagram()
    {
        UiThread.Run(() =>
        {
            var element = Drawn("graph TD\n  a[\"Alpha\"] --> b[\"Beta\"]\n  {{More}}\n", new ReflectionDataContext(new Host()));

            CollectionAssert.IsSubsetOf(new[] { "Alpha", "Beta", "Gamma" }, Words(element));
        });
    }

    [TestMethod]
    public void ABindingNothingSuppliesIsDrawnAsWritten()
    {
        UiThread.Run(() =>
        {
            var element = Drawn("{{Nowhere}}\n", new ReflectionDataContext(new Host()));

            Assert.IsTrue(element.Laid.ShowsSource, "something is always drawn: what was written");
            Assert.IsTrue(element.Laid.Trouble.Count > 0, "with why");
        });
    }

    [TestMethod]
    public void ContentThatGrowsIsDrawnAgainWhenItSaysSo()
    {
        UiThread.Run(() =>
        {
            var graph = new BoundGraph<string>((opened, _) => Task.FromResult(opened.Count == 0 ? Flowchart : Flowchart + "  b --> c[\"Gamma\"]\n"),
                                               text => text);
            graph.Walk();

            var element = Drawn("{{Graph}}\n", new ReflectionDataContext(new { Graph = graph }));
            Assert.IsFalse(Words(element).Contains("Gamma"));

            graph.Expand("b", open: true);
            Settle(element);

            CollectionAssert.Contains(Words(element), "Gamma", "the walk landed, said so, and the diagram was read again");
        });
    }

    [TestMethod]
    public void APressOnSuppliedContentPicksItOutWhole()
    {
        UiThread.Run(() =>
        {
            var element = Drawn(Grown, new ReflectionDataContext(new Host()));

            var picked = Picked(element, () =>
            {
                element.BeginPointerSelect(Middle(element, "Gamma"));
                element.EndPointerSelect();
            });

            Assert.AreEqual(0, element.Selection.Count, "none of it stands for any source");
            Assert.AreEqual(1, picked.Count);
            Assert.AreEqual("Gamma", picked[0].Text, "the node, with the words drawn on it");
        });
    }

    [TestMethod]
    public void NothingCanBeWrittenInSuppliedContent()
    {
        UiThread.Run(() =>
        {
            var element = Drawn(Grown, new ReflectionDataContext(new Host()));

            element.BeginPointerSelect(Middle(element, "Gamma"));
            element.EndPointerSelect();
            element.Insert("x");

            Assert.AreEqual(Grown, element.Source, "nobody wrote it here, so nobody writes in it here");
        });
    }

    [TestMethod]
    public void WhatWasWrittenBesideItIsWrittenInAsEver()
    {
        UiThread.Run(() =>
        {
            var element = Drawn(Grown, new ReflectionDataContext(new Host()));

            element.BeginPointerSelect(Middle(element, "Alpha"));
            element.EndPointerSelect();
            element.Insert("x");

            Assert.AreNotEqual(Grown, element.Source, "the diagram's own words are still the writer's");
        });
    }

    [TestMethod]
    public void ADragPicksOutEverySuppliedPieceItSpans()
    {
        UiThread.Run(() =>
        {
            var element = Drawn("graph TD\n  a[\"Alpha\"] --> b[\"Beta\"]\n  {{Two}}\n", new ReflectionDataContext(new Host()));

            var picked = Picked(element, () =>
            {
                element.BeginPointerSelect(Middle(element, "Gamma"));
                element.ExtendPointerSelect(Middle(element, "Delta"));
                element.EndPointerSelect();
            });

            CollectionAssert.IsSubsetOf(new[] { "Gamma", "Delta" }, picked.Select(pick => pick.Text).ToList());
        });
    }
}
