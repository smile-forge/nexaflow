using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Music;

/// <summary>
/// The keys a musician expects, pressed in a tune through the engine: a letter writes that note, <c>#</c> and <c>_</c>
/// sharpen and flatten the note before it, <c>+</c> and <c>-</c> lengthen and shorten it, Page Up and Page Down move it
/// an octave, and Space puts a pause in.
///
/// <para>
/// Only the staff, because only the staff can be reached: the builder draws no piece from a title, so a caret put there
/// stands against nothing and no language is asked. What this handler does say is that none of these keys is a note key
/// in text — a G in a title is the letter G — which is why it answers nothing where the caret is in a field or under a
/// staff.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("abc-editing")]
public class AbcKeyTests
{
    private const string Tune = "X:1\nT:Reel\nL:1/8\nK:C\nCDE |\n";

    [TestMethod]
    public void ALetterInTheStaffWritesThatNote() => UiThread.Run(() =>
        Assert.AreEqual("CDEG |\n", Staff(Pressed(After("CDE"), EditKind.Typing, "G")), "a G after the E"));

    [TestMethod]
    public void SpaceInTheStaffPutsInAPauseOfAWholeNote() => UiThread.Run(() =>
        // Eight, because everything in this tune is written in eighths: L:1/8.
        Assert.AreEqual("CDEz8 |\n", Staff(Pressed(After("CDE"), EditKind.Settling, " "))));

    [TestMethod]
    public void HashSharpensTheNoteBeforeTheCaret_AndUnderscoreFlattensIt() => UiThread.Run(() =>
    {
        Assert.AreEqual("CD^E |\n", Staff(Pressed(After("CDE"), EditKind.Typing, "#")));
        Assert.AreEqual("CD_E |\n", Staff(Pressed(After("CDE"), EditKind.Typing, "_")));
    });

    [TestMethod]
    public void PlusLengthensTheNoteBeforeTheCaret_AndMinusShortensIt() => UiThread.Run(() =>
    {
        Assert.AreEqual("CDE2 |\n", Staff(Pressed(After("CDE"), EditKind.Typing, "+")));
        Assert.AreEqual("CDE/ |\n", Staff(Pressed(After("CDE"), EditKind.Typing, "-")));
    });

    [TestMethod]
    public void PageUpAndPageDownMoveItAnOctave() => UiThread.Run(() =>
    {
        Assert.AreEqual("CDe |\n", Staff(Pressed(After("CDE"), EditKind.Raising, string.Empty)), "up a letter's case");
        Assert.AreEqual("CDE, |\n", Staff(Pressed(After("CDE"), EditKind.Lowering, string.Empty)), "down a comma");
    });

    /// <summary>Where the caret stands just past <paramref name="said"/>.</summary>
    private static int After(string said) => Tune.IndexOf(said, StringComparison.Ordinal) + said.Length;

    /// <summary>The tune once a key doing <paramref name="kind"/> was pressed at <paramref name="caret"/>.</summary>
    private static string Pressed(int caret, EditKind kind, string text)
    {
        var state = new EditState(Tune, caret);
        var laid = new ContentEngine().Lay("abc", state, StyleFormat.Dark, 700, readOnly: false);
        var made = ContentEngine.Edited(kind, text, new Landing(state, laid, -1));

        Assert.IsNotNull(made, $"{kind} '{text}' at {caret} was not answered at all");
        return made.Source;
    }

    /// <summary>The music line of a tune, which is the last thing written in this one.</summary>
    private static string Staff(string tune) => tune[(tune.LastIndexOf("K:C\n", StringComparison.Ordinal) + 4)..];
}
