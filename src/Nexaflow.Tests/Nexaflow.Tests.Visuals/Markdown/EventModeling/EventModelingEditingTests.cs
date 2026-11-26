using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Markdown.EventModeling;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.EventModeling;

/// <summary>
/// Working with an event model through the editor that hosts it: a frame's name written where it is drawn, Enter starting the next
/// frame and Backspace in its empty name taking it back, and the right-click changing a frame's type and whether it is a reset frame.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("eventmodeling")]
public class EventModelingEditingTests
{
    private const string Model = "tf 01 ui CartUI\ntf 02 cmd AddItem\ntf 03 evt ItemAdded";

    /// <summary>The event model inside a document, fenced as itself, with the editor handing its keys to it.</summary>
    private static void InADocument(Action<NexaTextSurface, DocumentBlock> test, string model = Model) =>
        MarkdownEditorHarness.Run("Shopping:\n\n```eventmodeling\n" + model + "\n```\n", editor =>
        {
            var block = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(block, "the event model did not render as content");

            test(editor, block!);
        });

    /// <summary>The event model as it is written in the document, between its fences.</summary>
    private static string Written(DocumentBlock model) => model.Source[model.Start..model.End].TrimEnd('\n');

    [TestMethod]
    public void TypingInAFramesNameChangesTheName() => UiThread.Run(() =>
        InADocument((editor, model) =>
        {
            Assert.IsFalse(model.IsReadOnly, "an event model's names are written in");

            PressPast(model, "AddItem");
            Write(editor, "s");

            StringAssert.Contains(model.Source, "tf 02 cmd AddItems\n", model.Source);
            Assert.AreEqual(0, model.Diagnostics.Count, Trouble(model));
        }));

    [TestMethod]
    public void WhatANameCannotHoldIsLeftOut() => UiThread.Run(() =>
        InADocument((editor, model) =>
        {
            PressPast(model, "AddItem");
            Write(editor, " -x");

            StringAssert.Contains(model.Source, "tf 02 cmd AddItemx\n", model.Source);
        }));

    [TestMethod]
    public void EnterOnAFrameStartsTheNextUnderIt_OfTheTypeThatUsuallyFollows_AndItsNameIsTyped() => UiThread.Run(() =>
        InADocument((editor, model) =>
        {
            PressPast(model, "CartUI");
            Press(editor, Key.Enter);

            Assert.AreEqual("tf 01 ui CartUI\ntf 04 cmd \ntf 02 cmd AddItem\ntf 03 evt ItemAdded", Written(model),
                            "a ui's command, numbered past every number written");

            Write(editor, "Remove");
            Assert.AreEqual("tf 01 ui CartUI\ntf 04 cmd Remove\ntf 02 cmd AddItem\ntf 03 evt ItemAdded", Written(model));
            Assert.AreEqual(0, model.Diagnostics.Count, Trouble(model));
        }));

    [TestMethod]
    public void TheNextFrameKeepsTheIndentationTheNumbersWidthAndTheLongWords() => UiThread.Run(() =>
        InADocument((editor, model) =>
        {
            PressPast(model, "Placed");
            Press(editor, Key.Enter);

            Assert.AreEqual("  timeframe 007 event Placed\n  timeframe 008 readmodel ", Written(model));
        }, "  timeframe 007 event Placed"));

    [TestMethod]
    public void BackspaceInTheNewFramesEmptyNameTakesItBack() => UiThread.Run(() =>
        InADocument((editor, model) =>
        {
            PressPast(model, "AddItem");
            Press(editor, Key.Enter);
            Press(editor, Key.Back);

            Assert.AreEqual(Model, Written(model), "the event model it was");
        }));

    [TestMethod]
    public void ARightClickOnAFrameOffersItsTypeAndWhetherItIsAResetFrame_TheCurrentOnesChosen() => UiThread.Run(() =>
        InADocument((editor, model) =>
        {
            var ribbon = Ribbon(editor, Frame(model, "AddItem"));

            var type = ribbon.Controls.OfType<MiniRibbonChoice>().Single(choice => choice.Id == "eventmodeling.type");
            CollectionAssert.AreEqual(new[] { "ui", "pcr", "cmd", "rmo", "evt" }, type.Options.Select(option => option.Id).ToArray());
            Assert.AreEqual("cmd", type.Current);

            var frame = ribbon.Controls.OfType<MiniRibbonChoice>().Single(choice => choice.Id == "eventmodeling.frame");
            Assert.AreEqual("tf", frame.Current);
        }));

    [TestMethod]
    public void ChoosingATypeWritesItsWord_SpelledAsTheWordItReplaces() => UiThread.Run(() =>
        InADocument((editor, model) =>
        {
            Click(Ribbon(editor, Frame(model, "AddItem")), "eventmodeling.type.rmo");
            StringAssert.Contains(model.Source, "tf 02 rmo AddItem\n", model.Source);

            Click(Ribbon(editor, Frame(model, "Placed")), "eventmodeling.type.cmd");
            StringAssert.Contains(model.Source, "timeframe 03 command Placed", model.Source);
        }, Model + "\ntimeframe 03 event Placed"));

    [TestMethod]
    public void ChoosingAResetFrameWritesItsWord() => UiThread.Run(() =>
        InADocument((editor, model) =>
        {
            Click(Ribbon(editor, Frame(model, "ItemAdded")), "eventmodeling.frame.rf");
            StringAssert.Contains(model.Source, "rf 03 evt ItemAdded", model.Source);
        }));

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>The frame named <paramref name="name"/>, as it is drawn.</summary>
    private static LayoutPiece Frame(DocumentBlock model, string name) =>
        model.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == EventModelingPiece.Frame
                                                            && piece.SelfAndDescendants().Any(inner => inner.Kind == EventModelingPiece.Name && inner.Words?.Glyphs.Text == name));

    /// <summary>Presses just inside the end of the name <paramref name="name"/>, where the frame draws it.</summary>
    private static void PressPast(DocumentBlock model, string name)
    {
        var words = Frame(model, name).SelfAndDescendants().First(inner => inner.Kind == EventModelingPiece.Name && inner.Words is not null);

        model.BeginPointerSelect(new Point(words.Bounds.Right - 1, words.Bounds.Y + (words.Bounds.Height / 2)));
        model.EndPointerSelect();
        Assert.IsTrue(model.HasCaret, $"a press on {name} takes the caret");
    }

    private static void Write(NexaTextSurface editor, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(editor, text);
        MarkdownEditorHarness.Pump();
    }

    private static void Press(NexaTextSurface editor, Key key)
    {
        MarkdownEditorHarness.RaiseKey(editor, key);
        MarkdownEditorHarness.Pump();
    }

    private static string Trouble(DocumentBlock model) => string.Join(" | ", model.Diagnostics.Select(diagnostic => diagnostic.Message));

    /// <summary>The ribbon a right-click inside <paramref name="frame"/>, clear of its words, opens.</summary>
    private static DiagramRibbon Ribbon(NexaTextSurface editor, LayoutPiece frame)
    {
        var box = frame.Bounds.ToWpf();
        var ribbon = editor.Shown.BuildRibbon(new Point(box.X + 3, box.Bottom - 3)) as DiagramRibbon;
        Assert.IsNotNull(ribbon, "a right-click opens the ribbon");
        return ribbon!;
    }

    private static void Click(DiagramRibbon ribbon, string named)
    {
        var button = Logical(ribbon).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "Diagram_Ribbon_" + named);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        MarkdownEditorHarness.Pump();
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Logical(DependencyObject from)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(from).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var further in Logical(child)) yield return further;
        }
    }
}
