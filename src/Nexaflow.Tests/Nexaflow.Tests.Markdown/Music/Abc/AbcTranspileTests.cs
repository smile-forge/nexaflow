using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.Abc;

/// <summary>
/// What a tune lets be written into it (<see cref="AbcParser.Rewrite"/>): the reader means words, and the parser says how
/// those words have to be spelled for the tune to read back as the same tune.
///
/// <para>
/// Each rule here is one the parser reads. A line is the unit of everything in ABC, so nothing may hold a break. A
/// percent on a field's line opens a comment unless a backslash holds it. An annotation ends at the next quote and a
/// decoration at the next bang, neither with any escape, so a character that would close one early cannot go in at all.
/// </para>
/// </summary>
[TestClass]
[CoversNode("abc-editing")]
public class AbcTranspileTests
{
    private const string Tune = "X:1\nT:Reel\nM:4/4\nK:C\n\"^up\" C D !trill! E F |\n";

    [TestMethod]
    public void WordsThePlaceCanHoldGoInAsTheyAre()
    {
        Assert.AreEqual("ly", Written(Words("Reel"), "ly"), "a letter in a title is only a letter");
        Assert.AreEqual("!", Written(Words("Reel"), "!"), "and a bang closes nothing on a field's line");
    }

    [TestMethod]
    public void APercentOnAFieldsLineIsHeldByABackslash()
    {
        // Left bare it would open a comment and take the rest of the title with it.
        Assert.AreEqual("\\%", Written(Words("Reel"), "%"));

        var after = Tune.Replace("T:Reel", "T:Reel\\%");
        Assert.AreEqual("Reel\\%", Title(after), "and the title still says what was typed into it");
    }

    [TestMethod]
    public void ABreakIsASpace_BecauseALineIsTheUnitOfEverythingHere()
    {
        Assert.AreEqual("a b", Written(Words("Reel"), "a\nb"));
        Assert.AreEqual("a b", Written(Words("up"), "a\r\nb"));
    }

    [TestMethod]
    public void WhatWouldCloseWhereItIsWrittenCannotGoInAtAll()
    {
        // Nothing escapes either of these: the parser finds the end of an annotation by looking for the next quote,
        // and the end of a decoration by looking for the next bang.
        Assert.IsNull(Rewritten(Words("up"), "\""), "a quote would close the annotation");
        Assert.IsNull(Rewritten(Words("trill"), "!"), "a bang would close the decoration");

        Assert.AreEqual("x", Written(Words("up"), "x"), "and what closes nothing goes in");
    }

    [TestMethod]
    public void AChangeWritingNothingMeantIsTheChangeItWas()
    {
        var change = ContentChange.Write(0, 0, "anything");

        Assert.AreSame(change, AbcParser.Rewrite(change), "a write nobody asked to be made safe is left alone");
    }

    /// <summary>The run of words <paramref name="said"/> was written as.</summary>
    private static ContentPart Words(string said)
    {
        var reading = ContentReading.Of(AbcParser.Parse(Tune));

        return reading.Root.SelfAndDescendants().First(part => part.Kind == Kinds.Words && part.Text == said);
    }

    /// <summary>What <paramref name="text"/> is written as into <paramref name="part"/>, or null where it cannot be.</summary>
    private static ContentChange? Rewritten(ContentPart part, string text) =>
        AbcParser.Rewrite(new ContentChange([ContentWrite.Words(part, part.End, 0, text)], part.End + text.Length));

    private static string Written(ContentPart part, string text)
    {
        var change = Rewritten(part, text);

        Assert.IsNotNull(change, $"'{text}' was refused where it should have been written");
        return change.Writes.Single().Text;
    }

    /// <summary>What the <c>T:</c> line of <paramref name="tune"/> says, read back from the source.</summary>
    private static string? Title(string tune) =>
        ContentReading.Of(AbcParser.Parse(tune)).Root.SelfAndDescendants()
            .Where(part => part.Kind == Kinds.Words && part.Parent?.Kind == AbcKinds.Field)
            .Select(part => part.Text)
            .FirstOrDefault(said => said.StartsWith("Reel", System.StringComparison.Ordinal));
}
