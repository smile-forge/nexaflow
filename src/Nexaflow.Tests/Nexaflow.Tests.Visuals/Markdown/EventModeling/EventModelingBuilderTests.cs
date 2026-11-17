using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.EventModeling;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown.Mermaid;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.EventModeling;

/// <summary>
/// What an event model is drawn as: its frames along time, each in the swimlane its type belongs to; an arrow from each frame to every
/// frame it feeds; a note or a specification under the frame it is about; and what could not be read, set as written under it.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[CoversNode("eventmodeling")]
public class EventModelingBuilderTests : MermaidBuilderContract
{
    private const string Cart = """
        eventmodeling
        title Cart
        tf 01 ui CartUI
        tf 02 cmd AddItem [[AddItem]]
        tf 03 evt ItemAdded {"id": 1}
        tf 04 rmo CartItems
        rf 05 pcr Checkout
        tf 06 cmd Inventory.Reserve ->> 05
        data AddItem {
          {"sku": "A1"}
        }
        note 02 {
          Only signed-in shoppers
        }
        entity ItemAdded
        gwt 03 given evt ItemAdded then evt ItemAdded
        """;

    /// <inheritdoc/>
    public override string Language => EventModelingParser.Language;

    /// <inheritdoc/>
    protected override bool FitsTheRoom => true;

    /// <inheritdoc/>
    protected override IEnumerable<(string What, string Source)> Drawn { get; } =
    [
        ("a cart", Cart),
        ("namespaced lanes", "eventmodeling\ntf 01 cmd Orders.Place\ntf 02 evt Orders.Placed\ntf 03 cmd Billing.Charge\ntf 04 evt Billing.Charged"),
        ("long words", "eventmodeling\ntimeframe 01 processor P\ntimeframe 02 command C\ntimeframe 03 event E\nresetframe 04 readmodel R"),
        ("a theme", "---\nconfig:\n  eventmodeling:\n    padding: 10\n  themeVariables:\n    emEventFill: '#ff0000'\n---\neventmodeling\ntf 01 evt A"),
        ("half written", "eventmodeling\ntf 01 ui\ntf 0\ntf 02 cmd A ->>\ndata X {\n  a"),
        ("something unread", "eventmodeling\nflibble\ntf 01 ui A"),
        ("nothing", "eventmodeling"),
    ];

    [TestMethod]
    public void EveryFrameIsABoxStandingForIt_LeftToRightInTheOrderWritten() => UiThread.Run(() =>
    {
        var laid = Lay(Cart);
        var frames = Pieces(laid, EventModelingPiece.Frame);

        CollectionAssert.AreEqual(
            new[] { "CartUI", "AddItem", "ItemAdded", "CartItems", "Checkout", "Reserve" },
            frames.Select(frame => Words(frame, EventModelingPiece.Name)).ToArray(),
            "each frame's name on it — a namespaced name by its last word");

        for (var at = 1; at < frames.Count; at++)
            Assert.IsTrue(frames[at].Bounds.X > frames[at - 1].Bounds.X, $"frame {at + 1} stands right of frame {at}");

        Assert.IsTrue(frames.All(frame => Written(Cart, frame.Part).StartsWith("tf", StringComparison.Ordinal) || Written(Cart, frame.Part).StartsWith("rf", StringComparison.Ordinal)));
    });

    [TestMethod]
    public void AFrameStandsInItsTypesLane_UiAndProcessorsAbove_CommandsAndReadModels_EventsBelow() => UiThread.Run(() =>
    {
        var laid = Lay(Cart);
        var frames = Pieces(laid, EventModelingPiece.Frame);
        double Top(int at) => frames[at].Bounds.Y;

        Assert.AreEqual(Top(0), Top(4), 0.5, "a ui and a processor share the top lane");
        Assert.AreEqual(Top(1), Top(3), 0.5, "a command and a read model share the lane under it");
        Assert.IsTrue(Top(0) < Top(1) && Top(1) < Top(2), "and events are lowest");
        Assert.IsTrue(Top(5) > Top(1) && Top(5) < Top(2), "a namespaced command in a lane of its own, among the commands");

        CollectionAssert.AreEqual(
            new[] { Str.Get("Text.EventModeling.Automation"), Str.Get("Text.EventModeling.Commands"), Str.Format("Text.EventModeling.CommandsOf", "Inventory"), Str.Get("Text.EventModeling.Events") },
            Pieces(laid, EventModelingPiece.LaneName).Select(name => name.Words!.Glyphs.Text).ToArray());
    });

    [TestMethod]
    public void AnArrowRunsFromEachFrameToEveryFrameItFeeds() => UiThread.Run(() =>
    {
        var laid = Lay(Cart);

        // 01→02, 02→03, 03→04 inferred; 05 a reset frame fed by nothing; 06 written as fed by 05.
        Assert.AreEqual(4, Pieces(laid, EventModelingPiece.Feed).Count);
    });

    [TestMethod]
    public void ANoteAndASpecificationAreDrawnUnderTheFrameEachIsAbout() => UiThread.Run(() =>
    {
        var laid = Lay(Cart);
        var frames = Pieces(laid, EventModelingPiece.Frame);
        var note = Pieces(laid, EventModelingPiece.Note).Single();
        var specification = Pieces(laid, EventModelingPiece.Specification).Single();

        Assert.IsTrue(note.Bounds.Y >= frames.Max(frame => frame.Bounds.Bottom), "under every frame");
        Assert.AreEqual(Middle(frames[1].Bounds.ToWpf()).X, Middle(note.Bounds.ToWpf()).X, frames[1].Bounds.Width, "under frame 02");
        Assert.AreEqual(Middle(frames[2].Bounds.ToWpf()).X, Middle(specification.Bounds.ToWpf()).X, frames[2].Bounds.Width, "under frame 03");

        CollectionAssert.AreEqual(
            new[] { Str.Get("Text.EventModeling.Given"), Str.Get("Text.EventModeling.Then") },
            Pieces(laid, EventModelingPiece.Clause).Select(clause => clause.Words!.Glyphs.Text).ToArray());
    });

    [TestMethod]
    public void AFramesDataIsDrawnInIt_FromItsRowOrTheBlockItNames() => UiThread.Run(() =>
    {
        var laid = Lay(Cart);
        var frames = Pieces(laid, EventModelingPiece.Frame);

        StringAssert.Contains(Rows(frames[1]), "sku", "AddItem shows the data block it names");
        StringAssert.Contains(Rows(frames[2]), "\"id\": 1", "ItemAdded the data on its row");
        Assert.AreEqual(string.Empty, Rows(frames[0]), "CartUI has none");
    });

    [TestMethod]
    public void TheThemesColoursFillTheFramesOfTheirType() => UiThread.Run(() =>
    {
        var laid = Lay("---\nconfig:\n  themeVariables:\n    emEventFill: '#ff0000'\n---\neventmodeling\ntf 01 evt A\ntf 02 cmd B");
        var frames = Pieces(laid, EventModelingPiece.Frame);

        Assert.AreEqual(Color.FromRgb(0xFF, 0, 0), Fill(frames[0]));
        Assert.AreNotEqual(Color.FromRgb(0xFF, 0, 0), Fill(frames[1]), "a command keeps the style's colour");
    });

    [TestMethod]
    public void WhatCouldNotBeReadIsSetAsWrittenUnderTheDiagram_WithWhy() => UiThread.Run(() =>
    {
        var laid = Lay("eventmodeling\nflibble\ntf 01 ui A");

        Assert.AreEqual("flibble", Pieces(laid, EventModelingPiece.Unread).Single().Words!.Glyphs.Text);
        Assert.AreEqual(1, Pieces(laid, EventModelingPiece.Reason).Count);
        Assert.AreEqual(1, Pieces(laid, EventModelingPiece.Frame).Count, "the rest still drawn");
    });

    [TestMethod]
    public void WhileItIsWritten_AFrameWithNoNameYetHasAHoleForIt() => UiThread.Run(() =>
    {
        var laid = Lay("eventmodeling\ntf 01 ui ", writing: true);

        Assert.AreEqual(1, laid.Root.SelfAndDescendants().Count(piece => (piece.Part as ContentPart)?.Kind == Kinds.Hole));
    });

    /// <summary>The words drawn in <paramref name="piece"/> as <paramref name="kind"/>.</summary>
    private static string Words(LayoutPiece piece, string kind) =>
        string.Concat(piece.SelfAndDescendants().Where(inner => inner.Kind == kind && inner.Words is not null).Select(inner => inner.Words!.Glyphs.Text));

    /// <summary>Every row of data drawn in a frame, one after another.</summary>
    private static string Rows(LayoutPiece frame) =>
        string.Join("\n", frame.SelfAndDescendants().Where(inner => inner.Kind == EventModelingPiece.Row && inner.Words is not null).Select(inner => inner.Words!.Glyphs.Text));
}
