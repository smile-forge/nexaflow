using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.Generic;
using System;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// What pointing at a tune means: the thing under the pointer, and what a drag from one to another
/// covers.
///
/// <para>
/// The tune carries a chord symbol over some notes and a word under most of them, because that is what
/// broke it. A note holding those as children stopped being a leaf, and the descent that finds what a
/// press landed on only considers leaves — so it walked straight past the note, fell back to the nearest
/// ink, and the bar won the tie because it came first. Half the notes in a tune could not be clicked, and
/// which half depended on whether anything was sung on them.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class AbcPointerTests
{
    private const string Sung =
        "X: 1\nT: the Auld Grey Cat\nM: C|\nL: 1/8\nK: EDorian\n"
        + "\"Em\"e2e2 E3F | GFGA BABc |\n"
        + "w: one two three four five six seven eight\n";

    [TestMethod]
    public void AndPressingWhatANoteIsDrawnFromMeansTheNoteToo() => UiThread.Run(() =>
    {
        // A head, a stem, a ledger line and a dot are how a note is drawn, not anything anybody typed.
        // Each is a node so that a press lands on it and climbs — which is the same rule that resolves a
        // staff line, said once rather than per piece.
        var layout = AbcBuilder.Build(Sung, 900, Brushes.Black, 1.0);

        foreach (var note in Every(layout, "note"))
            foreach (var piece in note.Children)
            {
                Assert.IsNull(piece.Part, $"a {Kind(piece)} names a piece of the tune; nobody wrote one");

                Assert.AreEqual(note, layout.Root.PieceAt(Middle(piece)).Selectable(),
                    $"a press on a {Kind(piece)} did not come back as the note it draws");
            }
    });

    private static System.Collections.Generic.List<Piece> Every(Laid layout, string kind) =>
        [.. layout.Root.SelfAndDescendants().Where(n => Kind(n) == kind)];

    private static Point Middle(Piece node) =>
        new(node.Bounds.X + (node.Bounds.Width / 2), node.Bounds.Y + (node.Bounds.Height / 2));

    private static string Kind(Piece node) => node.Exists ? node.Kind : "nothing";
}
