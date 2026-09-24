using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a git graph through the editor that hosts it: a branch's name is the characters written in its label, so a
/// press puts the caret among them and a keystroke changes the branch — and what a commit says about itself is pressed
/// where it is drawn, turned or not.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("gitgraph-writing")]
public class GitEditingTests : MermaidEditing
{
    private const string History =
        "gitGraph\n  commit id: \"Alpha\"\n  branch develop\n  commit\n  checkout main\n  merge develop";

    /// <inheritdoc/>
    protected override string Source => History;

    [TestMethod]
    public void TypingInABranchsLabelChangesTheBranchItIsMade() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a git graph's branch names are written in");

            PressPast(diagram, "develop");
            Write(editor, "ing");

            StringAssert.Contains(diagram.Source, "branch developing", diagram.Source);
            StringAssert.Contains(editor.Markdown, "branch developing", "and so does the document");
        }));

    [TestMethod]
    public void WhatACommitSaysAboutItselfIsPressedWhereItIsDrawn() => UiThread.Run(() =>
        InADocument((_, diagram) =>
        {
            var id = diagram.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == "Id");
            var at = new Point(id.Bounds.X + (id.Bounds.Width / 2), id.Bounds.Y + (id.Bounds.Height / 2));

            diagram.BeginPointerSelect(at);
            diagram.EndPointerSelect();

            Assert.AreEqual("\"Alpha\"", diagram.Source.Substring(id.Sits().Start, id.Sits().Length), "the id stands for what writes it");
        }));
}
