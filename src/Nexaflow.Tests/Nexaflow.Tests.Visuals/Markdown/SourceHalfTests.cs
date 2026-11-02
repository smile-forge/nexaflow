using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A document's source shown beside it as a page of text: a second surface on the one document (<see cref="EditedDocument"/>), so what
/// is written in either half is written in both and taken back from either — and what the page holding them asks of it: its text, as
/// automation reads a field; the places a host found, washed without moving it; a place picked out; and whether a stretch stands on it
/// at once.
/// <para>Interactive desktop only — the editor builds its document during a render pass.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-source-box")]
public class SourceHalfTests
{
    private const string Document = "# Title\n\nSome words.\n";

    [TestMethod]
    [CoversNode("markdown-split")]
    public void WhatIsWrittenInEitherHalf_IsShownInBoth_AndTakenBackFromEither() => UiThread.Run(() => Halves(Document, (rendered, source) =>
    {
        var at = "# Title\n\nSome ".Length;

        MarkdownEditorHarness.PlaceCaret(source, at);
        MarkdownEditorHarness.Type(source, "x");
        MarkdownEditorHarness.Pump();

        Assert.AreEqual("Some xwords.", MarkdownEditorHarness.Showing(rendered).Split('\n').Last(line => line.Length > 0).Trim(), "the rendered half shows what was written in the raw one");

        rendered.Undo();
        MarkdownEditorHarness.Pump();

        Assert.AreEqual(Document, source.Document.ToString());
        Assert.IsFalse(MarkdownEditorHarness.Showing(source).Contains("xwords"), "taken back in the rendered half, it is gone from the raw one");

        MarkdownEditorHarness.PlaceCaret(rendered, at);
        MarkdownEditorHarness.Type(rendered, "y");
        MarkdownEditorHarness.Pump();

        Assert.IsTrue(MarkdownEditorHarness.Showing(source).Contains("Some ywords."), "the raw half shows what was written in the rendered one");

        source.Undo();
        Assert.AreEqual(Document, rendered.Document.ToString(), "one history: the raw half takes back what the rendered half wrote");
    }));

    [TestMethod]
    public void ToAutomation_ItIsAFieldWhoseValueIsTheDocument_WrittenAsAStepToTakeBack() => UiThread.Run(() => Halves(Document, (_, source) =>
    {
        var value = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(source).GetPattern(PatternInterface.Value);

        Assert.AreEqual(Document, value.Value);
        Assert.IsFalse(value.IsReadOnly);

        value.SetValue("Other words.");
        Assert.AreEqual("Other words.", source.Document.ToString());

        source.Undo();
        Assert.AreEqual(Document, source.Document.ToString(), "setting it is a step the reader can take back");

        source.IsReadOnly = true;
        Assert.IsTrue(value.IsReadOnly);
        Assert.ThrowsExactly<ElementNotEnabledException>(() => value.SetValue("Not written."));
    }));

    [TestMethod]
    [CoversNode("markdown-find")]
    public void PlacesAHostFound_AreWashedWithoutMovingAnything_UntilOneIsPickedOut() => UiThread.Run(() =>
    {
        var text = string.Concat(Enumerable.Range(0, 200).Select(line => $"line {line} of the source\n"));
        var places = new[] { (text.IndexOf("line 3 ", StringComparison.Ordinal), 6), (text.IndexOf("line 180 ", StringComparison.Ordinal), 8) };

        Halves(text, (_, source) =>
        {
            var from = source.ShownFrom;
            var caret = source.Engine.State.Caret;

            source.ShowFound(places, 1);
            MarkdownEditorHarness.Pump();

            CollectionAssert.AreEqual(places, source.Found.ToArray());
            Assert.AreEqual(1, source.At);
            Assert.AreEqual(from, source.ShownFrom, "washing what was found moved the page");
            Assert.AreEqual(caret, source.Engine.State.Caret, "washing what was found moved the caret");

            Assert.IsTrue(source.PickOut(places[1]));
            MarkdownEditorHarness.Pump();

            var picked = source.Engine.State.Selection.Single();
            Assert.AreEqual((places[1].Item1, places[1].Item1 + places[1].Item2), (picked.Start, picked.End), "the place is what is picked out");
            Assert.IsTrue(source.ShownFrom > from, "and it was brought onto the page");
        });
    });

    [TestMethod]
    [CoversNode("markdown-find")]
    public void APlaceFoundIsMarkedWhereItsRowStands_NotWhereItsLineComesInTheDocument() => UiThread.Run(() =>
    {
        // Two lines: the first a paragraph wrapping onto many rows, the second short. By line, the second is half way down.
        var text = string.Join(' ', Enumerable.Range(0, 400).Select(word => $"word{word}")) + "\nlast line\n";
        var place = (text.IndexOf("last", StringComparison.Ordinal), 4);

        Halves(text, (_, source) =>
        {
            source.ShowFound([place], 0);
            MarkdownEditorHarness.Pump();

            var mark = source.SearchMarkPositions().Single();
            Assert.IsTrue(mark > 0.8, $"marked at {mark:0.##} of the page, as though every line were one row");
        });
    });

    [TestMethod]
    [CoversNode("markdown-split")]
    public void AStretchStandsOnThePageAtOnce_WhereAllItsRowsDo() => UiThread.Run(() =>
    {
        var text = string.Concat(Enumerable.Range(0, 200).Select(line => $"line {line} of the source\n"));

        Halves(text, (_, source) =>
        {
            Assert.IsTrue(source.ShowsAtOnce((0, text.IndexOf("line 3 ", StringComparison.Ordinal))), "three short lines stand on the page together");
            Assert.IsFalse(source.ShowsAtOnce((0, text.Length)), "two hundred lines do not");
        });
    });

    [TestMethod]
    public void TextShownAsAPage_IsReadFromItsTop() => UiThread.Run(() => Halves(Document, (_, source) =>
        Assert.AreEqual(0, source.Engine.State.Caret, "a page of text is read from its top, not written at its end")));

    [TestMethod]
    [CoversNode("markdown-block-picture")]
    public void TextShownAsAPage_OffersNoBlockCorner_ThoughTheSameTextInAFieldDoes() => UiThread.Run(() =>
    {
        bool Offered(bool page)
        {
            var engine = new ContentEngine { WrapsLines = page };
            engine.Show(ContentLanguages.AsTyped, Nexaflow.Visuals.Text.Painting.WpfStyles.Dark);
            engine.Start("one\ntwo\n");
            engine.Fit(400);

            var box = engine.Where(engine.Blocked(1)!);
            engine.Input(new ContentHover(new Nexaflow.Markdown.Layout.LayoutPoint(box.X + (box.Width / 2), box.Y + (box.Height / 2))));

            return engine.Corner is not null;
        }

        Assert.IsFalse(Offered(page: true), "a page of text offered a corner, as though it were a block on a page");
        Assert.IsTrue(Offered(page: false), "the same text as a field of code lost its corner");
    });

    /// <summary>
    /// <paramref name="text"/> in one document shown in a window as two halves — rendered, and as typed with its lines wrapped, as the
    /// page holding its source shows it — and <paramref name="test"/> run on them.
    /// </summary>
    private static void Halves(string text, Action<MarkdownSurface, MarkdownSurface> test)
    {
        var document = new EditedDocument(text);
        var rendered = new MarkdownSurface { IsReadOnly = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var source = new MarkdownSurface
        {
            IsReadOnly = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            WrittenIn = ContentLanguages.AsTyped,
            WrapsLines = true,
        };

        var halves = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        Grid.SetColumn(rendered, 1);
        halves.Children.Add(source);
        halves.Children.Add(rendered);

        // Shown but never activated, so the machine's own keyboard focus is left where it was.
        var window = new Window
        {
            Width = 800, Height = 400, Content = halves, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000,
        };

        try
        {
            window.Show();
            rendered.Document = document;
            source.Document = document;
            halves.UpdateLayout();
            MarkdownEditorHarness.Pump();

            test(rendered, source);
        }
        finally { window.Close(); }
    }
}
