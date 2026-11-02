using System;
using System.Collections.Generic;

using Nexaflow.Markdown.Layout.Code;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Code;

/// <summary>
/// Where a line of text shown as typed wraps onto rows: after the last space that fits, a word wider than the row broken where the row
/// ends, the spaces a row ends on hanging past it, and nothing that takes no room breaking a row — one rule, which laying a line and
/// saying how tall a stretch stands without laying it both follow.
/// </summary>
[TestClass]
[CoversNode("markdown-source-box")]
public class CodeRowsTests
{
    [TestMethod]
    public void ALineThatFits_IsOneRow() => CollectionAssert.AreEqual(Array.Empty<int>(), Broken("one two three", 13));

    [TestMethod]
    public void ALineTooLong_BreaksAfterTheLastSpaceThatFits_RowAfterRow() =>
        CollectionAssert.AreEqual(new[] { 8, 14 }, Broken("one two three four", 9));

    [TestMethod]
    public void AWordWiderThanTheRow_IsBrokenWhereTheRowEnds() => CollectionAssert.AreEqual(new[] { 4, 8 }, Broken("abcdefghij", 4));

    [TestMethod]
    public void TheSpacesARowEndsOn_HangPastIt_RatherThanStartingTheNext() => CollectionAssert.AreEqual(new[] { 7 }, Broken("abc    def", 3));

    [TestMethod]
    public void ARowIsNeverEndedAfterNothingButTheIndent_AWordTooLongToFollowItBreaksWhereTheRowEnds() =>
        CollectionAssert.AreEqual(new[] { 4 }, Broken("  abcdef", 4));

    [TestMethod]
    public void AfterTheIndent_TheRowStillBreaksAfterTheLastSpaceBetweenWords() =>
        CollectionAssert.AreEqual(new[] { 5 }, Broken("  ab cde", 5));

    [TestMethod]
    public void ACharacterTakingNoRoom_NeverBreaksARow()
    {
        var rows = new CodeRows(3);

        foreach (var letter in "abc") Assert.AreEqual(-1, rows.Next(letter, 1));

        Assert.AreEqual(-1, rows.Next('́', 0), "an accent on the last letter that fits stays with it");
        Assert.AreEqual(0, rows.Breaks);
    }

    [TestMethod]
    public void TheFirstCharacterOnARow_StaysOnIt_HoweverNarrowTheRoom() => CollectionAssert.AreEqual(new[] { 1, 2 }, Broken("abc", 0));

    [TestMethod]
    public void ANewLine_StartsOnARowOfItsOwn_WithNothingOnIt()
    {
        var rows = new CodeRows(3);
        foreach (var letter in "abcdef") rows.Next(letter, 1);

        Assert.AreEqual(1, rows.Breaks);

        rows.NewLine();
        Assert.AreEqual(0, rows.Breaks);
        Assert.AreEqual(-1, rows.Next('a', 1), "the line before left nothing on the new line's row");
        Assert.AreEqual(-1, rows.Next('b', 1));
    }

    /// <summary>Where each row of <paramref name="line"/> after its first starts, every character a column wide in a room <paramref name="columns"/> wide.</summary>
    private static List<int> Broken(string line, int columns)
    {
        var rows = new CodeRows(columns);
        var breaks = new List<int>();

        for (var at = 0; at < line.Length; at++)
            if (rows.Next(line[at], 1) is >= 0 and var starts) breaks.Add(starts);

        Assert.AreEqual(breaks.Count, rows.Breaks, "it counts every break it says");
        return breaks;
    }
}
