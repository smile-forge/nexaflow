using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Packet;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Packet;

/// <summary>
/// Writing in a packet through the editor that hosts it: a label and a bit number written there are the characters drawn, so a
/// press puts the caret among them and a keystroke changes the packet — kept by a backslash where it would end a label, and
/// only digits in a number.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("packet-writing")]
public class PacketEditingTests
{
    private const string Header = "0-7: \"Flags\"\n8-15: \"Length\"";

    /// <summary>The packet inside a document, fenced as itself, with the editor handing its keys to it.</summary>
    private static void InADocument(Action<MarkdownSurface, DocumentBlock> test, string packet = Header) =>
        MarkdownEditorHarness.Run("Header:\n\n```packet\n" + packet + "\n```\n", editor =>
        {
            var block = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(block, "the packet did not render as content");

            test(editor, block!);
        });

    /// <summary>Presses just inside the end of <paramref name="words"/> where the packet draws them — a label, or a number over a box.</summary>
    private static void PressPast(DocumentBlock packet, string words)
    {
        var at = packet.Source.IndexOf(words, packet.Start, StringComparison.Ordinal);
        var piece = packet.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind is PacketPiece.Label or PacketPiece.Bit && piece.Words is { Maps: true } && piece.Sits().Start == at);

        packet.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        packet.EndPointerSelect();
    }

    private static void Write(MarkdownSurface editor, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(editor, text);
        MarkdownEditorHarness.Pump();
    }

    private static string Trouble(DocumentBlock packet) => string.Join(" | ", packet.Diagnostics.Select(diagnostic => diagnostic.Message));

    [TestMethod]
    public void TypingInALabelChangesTheLabel() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            Assert.IsFalse(packet.IsReadOnly, "a packet's labels are written in");

            PressPast(packet, "Flags");
            Assert.IsTrue(packet.HasCaret, "a press on a label takes the caret");

            Write(editor, "X");

            StringAssert.Contains(packet.Source, "0-7: \"FlagsX\"", packet.Source);
            StringAssert.Contains(editor.Markdown, "0-7: \"FlagsX\"", "and so does the document");
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
        }));

    [TestMethod]
    public void AQuoteTypedIntoALabelIsKeptByABackslash() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            PressPast(packet, "Flags");
            Write(editor, "\"");

            StringAssert.Contains(packet.Source, "0-7: \"Flags\\\"\"", packet.Source);
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
        }));

    [TestMethod]
    public void BackspaceAfterABackslashTypedTakesTheBackslashAndWhatItKeepsTogether() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            PressPast(packet, "Flags");
            Write(editor, "\\");
            StringAssert.Contains(packet.Source, "0-7: \"Flags\\\\\"", packet.Source);

            Press(editor, Key.Back);

            StringAssert.Contains(packet.Source, "0-7: \"Flags\"", packet.Source);
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
        }));

    [TestMethod]
    public void ABitNumberIsNoPlaceToWrite() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            var number = packet.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == PacketPiece.Bit);
            packet.BeginPointerSelect(new Point(number.Bounds.X + (number.Bounds.Width / 2), number.Bounds.Y + (number.Bounds.Height / 2)));
            packet.EndPointerSelect();

            Write(editor, "9");

            StringAssert.Contains(packet.Source, "```packet\n0-7: ", $"the caret never stands in a number, so no key changes one: {packet.Source}");
            StringAssert.Contains(packet.Source, "\n8-15: ", packet.Source);
        }));

    [TestMethod]
    public void AMermaidBlockHeadedPacketIsWrittenInTheSameWay() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Header:\n\n```mermaid\npacket\n" + Header + "\n```\n", editor =>
        {
            var packet = MarkdownEditorHarness.Block(editor)!;

            PressPast(packet, "Length");
            Write(editor, "s");

            StringAssert.Contains(packet.Source, "8-15: \"Lengths\"", packet.Source);
        }));

    // ── Whole fields ────────────────────────────────────────────────────────

    /// <summary>Three fields of different lengths, written as ranges.</summary>
    private const string Three = "0-3: \"A\"\n4-11: \"B\"\n12-15: \"C\"";

    [TestMethod]
    public void ARightClickOnAFieldOffersToPutOneInBesideIt_TakeItOut_AndHowManyBitsItTakes() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            var ribbon = Ribbon(editor, Box(packet, "B"));

            CollectionAssert.IsSubsetOf(new[] { "packet.bits", "packet.insert.before", "packet.insert.after", "packet.delete" },
                                        ribbon.Controls.Select(control => control.Id).ToArray());
            Assert.IsTrue(ribbon.Controls.OfType<MiniRibbonButton>().Where(button => button.Id.StartsWith("packet.", StringComparison.Ordinal)).All(button => button.Shape is not null),
                          "what is done to the field stands in the ribbon as its icon, side by side, rather than named down a list");

            var length = ribbon.Controls.OfType<MiniRibbonEntry>().Single();
            Assert.AreEqual("8", length.Current, "the box holds the length it takes now");
            CollectionAssert.IsSubsetOf(new[] { "1", "8", "16", "32" }, length.Listed.ToArray(), "and the usual lengths drop down beside it");
            Assert.IsTrue(length.Accepts!("12"), "any whole number is a length");
            Assert.IsFalse(length.Accepts!("0"), "nought is none");
            Assert.IsFalse(length.Accepts!("four"), "and words are none");
        }, Three));

    [TestMethod]
    public void ChoosingALengthMakesTheFieldThatLong_AndTheFieldsAfterItMoveOn() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            Click(Ribbon(editor, Box(packet, "B")), "packet.bits.16");

            StringAssert.Contains(packet.Source, "0-3: \"A\"\n4-19: \"B\"\n20-23: \"C\"", packet.Source);
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
        }, Three));

    [TestMethod]
    public void ALengthTypedIntoTheBoxMakesTheFieldThatLong() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            var ribbon = Ribbon(editor, Box(packet, "B"));
            var window = new Window { Content = ribbon, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Top = -10000 };
            window.Show();

            try
            {
                var box = Logical(ribbon).OfType<TextBox>().Single(found => System.Windows.Automation.AutomationProperties.GetAutomationId(found) == "Diagram_Ribbon_packet.bits");
                box.Text = "12";
                box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                MarkdownEditorHarness.Pump();
            }
            finally
            {
                window.Close();
            }

            StringAssert.Contains(packet.Source, "0-3: \"A\"\n4-15: \"B\"\n16-19: \"C\"", packet.Source);
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
        }, Three));

    /// <summary>
    /// The end of a field taken hold of — nothing picked out first — and carried a step at a time over the next field, as a hand drags
    /// it: the field ends at the bit it is let go over, and every field after it is numbered on.
    /// </summary>
    [TestMethod]
    public void DraggingTheEndOfAFieldMakesItEndWhereItIsLetGo() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            var end = End(packet, "A");
            var from = Middle(end);

            // Bit 7 is the fourth of B's eight.
            var b = Box(packet, "B");
            var to = new Point(b.X + (b.Width * 3.5 / 8), from.Y);

            Assert.AreEqual(0, packet.SelectionLength, "nothing is picked out first");

            packet.BeginPointerSelect(from);
            for (var x = from.X; x <= to.X; x += 2) packet.ExtendPointerSelect(new Point(x, from.Y));
            packet.ExtendPointerSelect(to);
            packet.EndPointerSelect();
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(packet.Source, "```packet\n0-7: \"A\"\n8-15: \"B\"\n16-19: \"C\"\n```", packet.Source);
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
        }, Three));

    [TestMethod]
    public void AndPressingTheEndWithoutDraggingIsAPressOnTheField() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            var before = packet.Source;

            packet.BeginPointerSelect(Middle(End(packet, "A")));
            packet.EndPointerSelect();

            Assert.AreEqual(before, packet.Source, "nothing written");
            Assert.IsTrue(packet.SelectionLength > 0, "the field it ends is picked out, as a press on its box picks it out");
        }, Three));

    /// <summary>Where the end of the field labelled <paramref name="label"/> is drawn — the piece a drag takes hold of to make it longer or shorter.</summary>
    private static Rect End(DocumentBlock packet, string label) =>
        packet.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == PacketPiece.End && piece.Part is { } part
                                                             && packet.Source.Substring(part.Start, part.Length).Contains($"\"{label}\"", StringComparison.Ordinal))
              .Bounds.ToWpf();

    [TestMethod]
    public void ChoosingToTakeAFieldOutTakesItsLine_AndTheFieldsAfterItCloseUp() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            Click(Ribbon(editor, Box(packet, "B")), "packet.delete");

            StringAssert.Contains(packet.Source, "```packet\n0-3: \"A\"\n4-7: \"C\"\n```", packet.Source);
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
        }, Three));

    [TestMethod]
    public void ChoosingAFieldAfterPutsOneBitIn_TheCaretInItsLabel() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
        Click(Ribbon(editor, Box(packet, "A")), "packet.insert.after");

            StringAssert.Contains(packet.Source, "0-3: \"A\"\n4: \"\"\n5-12: \"B\"\n13-16: \"C\"", packet.Source);
            Assert.AreEqual(packet.Source.IndexOf("4: \"", StringComparison.Ordinal) + 4, packet.Caret, "between its quotes");

            Write(editor, "Flag");
            StringAssert.Contains(packet.Source, "4: \"Flag\"", packet.Source);
        }, Three));

    [TestMethod]
    public void DeleteTakesAChosenFieldOut() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            ChooseTheField(packet, "B");
            Press(editor, Key.Delete);

            StringAssert.Contains(packet.Source, "```packet\n0-3: \"A\"\n4-7: \"C\"\n```", packet.Source);
        }, Three));

    [TestMethod]
    public void DraggingAChosenFieldOntoAnotherPutsItThere_EveryFieldKeepingItsLength() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            ChooseTheField(packet, "A");

            var from = Inside(Box(packet, "A"));
            var to = Middle(Box(packet, "C"));

            packet.BeginPointerSelect(from);
            packet.ExtendPointerSelect(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            packet.ExtendPointerSelect(to);
            packet.EndPointerSelect();
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(packet.Source, "```packet\n0-7: \"B\"\n8-11: \"C\"\n12-15: \"A\"\n```", $"after the field it was let go on: {packet.Source}");
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
        }, Three));

    /// <summary>
    /// A drag as a hand makes it, a step at a time, from one box to the next and clear of their labels: the only stops are in the labels,
    /// so the nearest offset stands still — inside the field carried — while the pointer crosses onto the other box.
    /// </summary>
    [TestMethod]
    public void DraggingAFieldStepByStepOntoTheNextBoxPutsItThere() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            ChooseTheField(packet, "A");

            var from = Inside(Box(packet, "A"));
            var to = Inside(Box(packet, "B"));

            packet.BeginPointerSelect(from);
            for (var x = from.X; x <= to.X; x += 2) packet.ExtendPointerSelect(new Point(x, from.Y));
            packet.EndPointerSelect();
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(packet.Source, "```packet\n0-7: \"B\"\n8-11: \"A\"\n12-15: \"C\"\n```", packet.Source);
        }, Three));

    // ── The clipboard ───────────────────────────────────────────────────────

    [TestMethod]
    public void WordsPickedOutInALabelAreOfferedToCutCopyAndPaste_AndCuttingTakesThemOut() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            packet.Select(packet.Source.IndexOf("Flags", packet.Start, StringComparison.Ordinal), 3);

            var ribbon = Ribbon(editor, Label(packet, "Flags"));
            CollectionAssert.IsSubsetOf(new[] { LayoutVerbs.Cut, LayoutVerbs.Copy, LayoutVerbs.Paste }, ribbon.Controls.Select(control => control.Id).ToArray());

            Click(ribbon, LayoutVerbs.Cut);

            Assert.AreEqual("Fla", MarkdownClipboard.ReadPlainText(MarkdownEditorHarness.Clipboard!).TrimEnd(), "what was picked out is on the clipboard, as the engine copies a stretch");
            StringAssert.Contains(packet.Source, "0-7: \"gs\"", $"and gone from the label: {packet.Source}");
        }));

    [TestMethod]
    public void APasteIntoALabelIsKeptByABackslashWhereItWouldEndIt() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            packet.TakeCaret(packet.Source.IndexOf("Flags", packet.Start, StringComparison.Ordinal) + "Flags".Length);
            MarkdownEditorHarness.Clipboard = new DataObject(DataFormats.UnicodeText, " say \"hi\"");

            var ribbon = Ribbon(editor, Label(packet, "Flags"));
            CollectionAssert.Contains(ribbon.Controls.Select(control => control.Id).ToArray(), LayoutVerbs.Paste);
            CollectionAssert.DoesNotContain(ribbon.Controls.Select(control => control.Id).ToArray(), LayoutVerbs.Cut, "nothing is picked out to cut");

            Click(ribbon, LayoutVerbs.Paste);

            StringAssert.Contains(packet.Source, "0-7: \"Flags say \\\"hi\\\"\"", packet.Source);
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
        }));

    [TestMethod]
    public void OverAWholeFieldPickedOutOnlyACopyIsOffered() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            ChooseTheField(packet, "Flags");

            var verbs = Ribbon(editor, Box(packet, "Flags")).Controls.Select(control => control.Id).ToArray();

            CollectionAssert.Contains(verbs, LayoutVerbs.Copy);
            CollectionAssert.DoesNotContain(verbs, LayoutVerbs.Cut, "cut as characters it would leave a gap; Delete takes a field out");
            CollectionAssert.DoesNotContain(verbs, LayoutVerbs.Paste);
        }));

    /// <summary>Where the label written <paramref name="words"/> is drawn — in the box on the row its field starts on.</summary>
    private static Rect Label(DocumentBlock packet, string words)
    {
        var at = packet.Source.IndexOf(words, packet.Start, StringComparison.Ordinal);
        return packet.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == PacketPiece.Label && piece.Words is { Maps: true } && piece.Sits().Start == at).Bounds.ToWpf();
    }

    /// <summary>Where the box of the field labelled <paramref name="label"/> is drawn — on the row it starts on.</summary>
    private static Rect Box(DocumentBlock packet, string label)
    {
        var field = packet.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == PacketPiece.Field && piece.Part is { } part
                                                                         && packet.Source.Substring(part.Start, part.Length).Contains($"\"{label}\"", StringComparison.Ordinal));

        return field.Children.First(piece => piece.Kind == MermaidPiece.Shape).Bounds.ToWpf();
    }

    /// <summary>A point inside a box but clear of its label and its edges, where a press means the box.</summary>
    private static Point Inside(Rect box) => new(box.X + 6, box.Y + 4);

    private static Point Middle(Rect box) => new(box.X + (box.Width / 2), box.Y + (box.Height / 2));

    /// <summary>Chooses a field whole, as a press on its box does.</summary>
    private static void ChooseTheField(DocumentBlock packet, string label)
    {
        packet.BeginPointerSelect(Inside(Box(packet, label)));
        packet.EndPointerSelect();

        Assert.IsTrue(packet.SelectionLength > 0, $"pressing {label}'s box chooses its field");
    }

    private static void Press(MarkdownSurface editor, Key key)
    {
        MarkdownEditorHarness.RaiseKey(editor, key);
        MarkdownEditorHarness.Pump();
    }

    /// <summary>The ribbon a right-click in the middle of <paramref name="over"/> opens.</summary>
    private static DiagramRibbon Ribbon(MarkdownSurface editor, Rect over)
    {
        var ribbon = editor.Shown.BuildRibbon(Middle(over)) as DiagramRibbon;
        Assert.IsNotNull(ribbon, "a right-click opens the ribbon");
        return ribbon!;
    }

    /// <summary>Clicks the ribbon's button named <c>Diagram_Ribbon_</c> and <paramref name="named"/>.</summary>
    private static void Click(DiagramRibbon ribbon, string named)
    {
        var button = Logical(ribbon).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "Diagram_Ribbon_" + named);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        MarkdownEditorHarness.Pump();
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject from)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(from).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var further in Logical(child)) yield return further;
        }
    }

    [TestMethod]
    public void WhatEndsALabelIsKeptByABackslash_InTheQuoteTheLabelIsWrittenIn()
    {
        const string source = "packet\n0-7: 'it'\n8-15: \"say\"";
        var root = Laying.Read(PacketParser.Language, source).Root;
        var it = root.SelfAndDescendants().First(part => part.Kind == Kinds.Words && part.Text == "it");
        var say = root.SelfAndDescendants().First(part => part.Kind == Kinds.Words && part.Text == "say");

        Assert.AreEqual("\\'s", PacketEdits.Escaping(it, it.End, "'s")!.Value.Text);
        Assert.IsNull(PacketEdits.Escaping(it, it.End, "\"so\""), "a double quote ends nothing in single quotes");
        Assert.AreEqual(" \\\"hi\\\" \\\\", PacketEdits.Escaping(say, say.End, " \"hi\" \\")!.Value.Text);
    }

    [TestMethod]
    public void WhatIsTypedWhereALabelIsStillToWriteGoesInQuotes_TheCaretInsideThem()
    {
        const string source = "packet\n0-7:";
        var hole = Laying.Read(PacketParser.Language, source, writing: true).Root.SelfAndDescendants()
                         .First(part => part.Kind == Kinds.Hole && PacketParser.LabelOf(part) is not null);
        var writing = PacketEdits.Escaping(hole, hole.Start, "Flags")!.Value;

        Assert.AreEqual("\"Flags\"", writing.Text);
        Assert.AreEqual(hole.Start + "\"Flags".Length, writing.Caret);
    }
}
