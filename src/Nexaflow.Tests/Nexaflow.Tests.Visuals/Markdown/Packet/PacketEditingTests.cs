using System;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Packet;

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
    public void TypingIntoABitNumberMovesTheEndOfItsField_AndALetterGoesNowhere() => UiThread.Run(() =>
        InADocument((editor, packet) =>
        {
            PressPast(packet, "15");
            Write(editor, "x");
            StringAssert.Contains(packet.Source, "8-15: \"Length\"", $"a letter is no bit number: {packet.Source}");

            Write(editor, "9");
            StringAssert.Contains(packet.Source, "8-159: \"Length\"", packet.Source);
            Assert.AreEqual(0, packet.Diagnostics.Count, Trouble(packet));
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

    [TestMethod]
    public void ABitNumberTakesOnlyDigits()
    {
        const string source = "packet\n0-7: \"A\"";
        var end = Laying.Read(PacketParser.Language, source).Root.SelfAndDescendants().First(part => part.Kind == Kinds.Number && part.Role == PacketRoles.End);

        Assert.AreEqual("12", PacketEdits.Escaping(end, end.End, "1a2")!.Value.Text);
        Assert.IsNull(PacketEdits.Escaping(end, end.End, "12"), "digits go in as they are");
    }
}
