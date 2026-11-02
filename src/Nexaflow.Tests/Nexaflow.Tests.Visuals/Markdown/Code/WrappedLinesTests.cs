using System;
using System.Linq;
using System.Windows;

using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Code;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Languages;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Code;

/// <summary>
/// Text shown as typed, wrapped as a page (<see cref="ContentEngine.WrapsLines"/>): a line too long for the room carries on in rows
/// beneath, a long document stands as tall before its units are laid as after, a new room wraps it again — and only the content the
/// engine shows wraps, never content inside it.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("markdown-source-box")]
public class WrappedLinesTests
{
    private const double Room = 300;

    private static readonly Rect Screen = new(0, 0, Room, 600);

    [TestMethod]
    public void ALineLongerThanTheRoom_CarriesOnInRowsBeneath_BrokenAfterASpace_AndNoCharacterPastTheRoom() => UiThread.Run(() =>
    {
        var text = string.Join(' ', Enumerable.Range(0, 60).Select(word => $"word{word}"));
        var engine = Shown(text, wraps: true);

        var root = engine.Laid.Root;
        var advance = CodeSetting.LineHeight(WpfStyles.Dark, WpfMedium.OnThisThread);
        // Each character where it is drawn — a caret where a row breaks stands at the end of the row above, so it is no help here.
        LayoutRect Drawn(int at) => root.RangeRects(at, 1)[0];
        var rows = Enumerable.Range(0, text.Length).Select(at => Drawn(at).Y).Distinct().Count();

        Assert.IsTrue(rows >= 3, $"It came to {rows} rows.");
        Assert.AreEqual(rows * advance, engine.Laid.Size.Height, 1e-6, "It stands as tall as its rows.");

        for (var at = 1; at < text.Length; at++)
        {
            if (Drawn(at).Y > Drawn(at - 1).Y) Assert.AreEqual(' ', text[at - 1], $"The row starting at {at} did not start after a space.");
            if (text[at] != ' ') Assert.IsTrue(Drawn(at).Right <= Room + 0.5, $"The character at {at} stands past the room.");
        }
    });

    [TestMethod]
    public void AnIndentedLineWrapped_CarriesOnUnderItsIndent() => UiThread.Run(() =>
    {
        var text = "    " + string.Join(' ', Enumerable.Range(0, 60).Select(word => $"word{word}"));
        var root = Shown(text, wraps: true).Laid.Root;

        LayoutRect Drawn(int at) => root.RangeRects(at, 1)[0];
        var under = Drawn(4).X;
        var rows = 1;

        for (var at = 5; at < text.Length; at++)
            if (Drawn(at).Y > Drawn(at - 1).Y)
            {
                rows++;
                Assert.AreEqual(under, Drawn(at).X, 0.5, $"the row starting at {at} does not start under the line's indent");
            }

        Assert.IsTrue(rows >= 3, $"it came to {rows} rows");
    });

    [TestMethod]
    public void ALongIndentedDocumentWrapped_StandsAsTallBeforeItsUnitsAreLaid_AsAfter() => UiThread.Run(() =>
    {
        var line = "    - " + string.Join(' ', Enumerable.Range(0, 40).Select(word => $"word{word}")) + "\n";
        var text = string.Concat(Enumerable.Repeat(line, 400 * 1024 / line.Length));
        var engine = Shown(text, wraps: true);
        var before = engine.Laid.Size.Height;

        engine.Uncover(text.Length);
        for (var pass = 0; pass < 20; pass++) engine.Relay();

        Assert.AreEqual(before, engine.Laid.Size.Height, 1e-6, "What was said of the indented units not laid is not what they came to.");
    });

    [TestMethod]
    public void ALongDocumentWrapped_StandsAsTallBeforeItsUnitsAreLaid_AsAfter() => UiThread.Run(() =>
    {
        var text = MarkdownLayoutBench.GeneratedProse(400 * 1024);
        var engine = Shown(text, wraps: true);
        var before = engine.Laid.Size.Height;

        // The end, laid where it was only said.
        engine.Uncover(text.Length);
        for (var pass = 0; pass < 20; pass++) engine.Relay();

        Assert.AreEqual(before, engine.Laid.Size.Height, 1e-6, "What was said of the units not laid is not what they came to.");
        Assert.IsTrue(before > Shown(text, wraps: false).Laid.Size.Height, "It stands no taller wrapped than with its lines kept.");
    });

    [TestMethod]
    public void InANarrowerRoom_ALineIsWrappedAgain() => UiThread.Run(() =>
    {
        var text = string.Join(' ', Enumerable.Range(0, 60).Select(word => $"word{word}"));
        var engine = Shown(text, wraps: true);
        var wide = engine.Laid.Size.Height;

        engine.Fit(Room / 2);
        engine.Relay();

        Assert.IsTrue(engine.Laid.Size.Height > wide, "The line kept from the wider room was set down as it was.");
        Assert.IsTrue(engine.Laid.Size.Width <= Room / 2 + 0.5, "A row reaches past the room it was laid in.");
    });

    [TestMethod]
    public void ADocumentShownWrapping_KeepsTheLinesOfItsFences() => UiThread.Run(() =>
    {
        var text = "Before.\n\n```text\n" + string.Join(' ', Enumerable.Range(0, 60).Select(word => $"word{word}")) + "\n```\n\nAfter.\n";

        Assert.AreEqual(Shown(text, wraps: false, language: null).Laid.Size.Height,
                        Shown(text, wraps: true, language: null).Laid.Size.Height, 1e-6,
                        "A fence inside the document wrapped with it.");
    });

    [TestMethod]
    public void TheNameTextIsShownUnder_IsPlainTexts() =>
        Assert.AreSame(Shipped.Text, ContentLanguages.For(ContentLanguages.AsTyped), "what is shown as typed is read as plain text");

    /// <summary><paramref name="text"/> shown on a screen <see cref="Room"/> wide, read and laid — as typed unless a language is named.</summary>
    private static ContentEngine Shown(string text, bool wraps, string? language = ContentLanguages.AsTyped)
    {
        var engine = new ContentEngine { WrapsLines = wraps };
        engine.Show(language, WpfStyles.Dark);
        engine.OnScreen = Screen.ToLayout();
        engine.Start(text);
        engine.Fit(Room);

        engine.ReadingRest.Wait(TimeSpan.FromMinutes(2));
        for (var pass = 0; pass < 20; pass++) engine.Relay();

        return engine;
    }
}
