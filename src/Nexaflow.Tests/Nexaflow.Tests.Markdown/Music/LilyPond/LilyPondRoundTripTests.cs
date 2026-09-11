using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.LilyPond;

/// <summary>
/// The promise the whole tree rests on, for LilyPond as for ABC: what was read prints back as it was written.
///
/// <para>
/// Both halves again. <c>Print(Parse(s)) == s</c> says nothing was lost, and <see cref="TheParserOnlyEverCopies"/>
/// says nothing was invented — every leaf's characters are found in the source where the tree puts them. The
/// prefixes are what somebody typing a piece passes through, most of them with a brace or a quote still open.
/// </para>
/// </summary>
[TestClass]
[CoversNode("ly-ast-roundtrip")]
public class LilyPondRoundTripTests
{
    [TestMethod]
    public void EveryConstructReadsBackAsItWasWritten()
    {
        foreach (var (what, ly) in LilyPondConstructs.Everything)
            Assert.AreEqual(ly, LilyPondParser.Parse(ly).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryConstructReadsBackToo()
    {
        foreach (var (what, ly) in LilyPondConstructs.Everything)
            for (var length = 0; length <= ly.Length; length++)
            {
                var typed = ly[..length];
                Assert.AreEqual(typed, LilyPondParser.Parse(typed).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void AndEverySuffix()
    {
        // What arrives when somebody pastes the back half of a piece — a run that starts inside a chord, or with
        // the closing brace of something that was never opened here.
        foreach (var (what, ly) in LilyPondConstructs.Everything)
            for (var start = 0; start <= ly.Length; start++)
            {
                var pasted = ly[start..];
                Assert.AreEqual(pasted, LilyPondParser.Parse(pasted).Print(), $"{what}: from character {start}");
            }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, ly) in LilyPondConstructs.Everything)
            foreach (var place in LilyPondParser.Parse(ly).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= ly.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {ly.Length}");

                Assert.AreEqual(ly.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
    }

    [TestMethod]
    public void ACommandHoldsWhatItIsGiven()
    {
        // \relative is given a start pitch and the music it applies to, and both are its parts rather than its
        // neighbours — which is what lets a stage see which notes are inside it.
        var relative = LilyPondParser.Parse(@"\relative c'' { c4 d }").Children.Single();

        Assert.AreEqual(LilyPondKinds.Command, relative.Kind);
        Assert.AreEqual(@"\relative", relative.Part(Roles.Name)?.Text);
        Assert.AreEqual("c''", relative.Part(LilyPondRoles.Argument)?.Print());
        Assert.AreEqual(LilyPondKinds.Sequential, relative.Part(Roles.Body)?.Kind);
    }

    [TestMethod]
    public void AndACommandThatTakesNothingHoldsOnlyItsName()
    {
        // A variable's name, a \break, an articulation — whatever follows is read as what it is.
        var tree = LilyPondParser.Parse(@"{ \melody c4\fermata \break d }");
        var commands = Written(tree).Where(n => n.Kind == LilyPondKinds.Command).ToList();

        Assert.IsTrue(commands.All(c => c.Children.Count == 1), "none of them took an argument");
        Assert.AreEqual(2, Played(tree).Count, "and both notes are still notes");
    }

    [TestMethod]
    public void AWordIsOnlyANoteWhenItSpellsOne()
    {
        // `bass` starts with a note letter and is a clef; `es` and `as` are E flat and A flat.
        var tree = LilyPondParser.Parse(@"{ \clef bass es as \repeat volta 2 { b } }");
        CollectionAssert.AreEqual(new[] { "es", "as", "b" }, Played(tree).Select(n => n.Print()).ToArray());
    }

    [TestMethod]
    public void LyricsAndChordNamesAreReadByTheirOwnLexers()
    {
        var tree = LilyPondParser.Parse(@"\addlyrics { Twin -- kle __ _ sky. } \chordmode { g1:7 d2:m7/f }");

        CollectionAssert.AreEqual(new[] { "Twin", "kle", "sky." },
            Written(tree).Where(n => n.Kind == LilyPondKinds.Syllable).Select(n => n.Text).ToArray(),
            "a full stop is part of a word in a lyric");

        CollectionAssert.AreEqual(new[] { "--", "__", "_" },
            Written(tree).Where(n => n.Kind == LilyPondKinds.LyricMark).Select(n => n.Text).ToArray());

        CollectionAssert.AreEqual(new[] { "g1:7", "d2:m7/f" },
            Written(tree).Where(n => n.Kind == LilyPondKinds.ChordName).Select(n => n.Print()).ToArray(),
            "a colon and a slash are part of a chord's name");
    }

    [TestMethod]
    public void ANameFollowedByAnEqualsIsADefinition()
    {
        // Even a name that would otherwise be a note, and one written in quotes.
        var tree = LilyPondParser.Parse("a = { c4 }\n\"words1V1\" = \\lyricmode { la }");
        var definitions = tree.Children.Where(n => n.Kind == LilyPondKinds.Assignment).ToList();

        Assert.AreEqual(2, definitions.Count);
        Assert.AreEqual("a", definitions[0].Part(Roles.Name)?.Text);
        Assert.AreEqual("\"words1V1\"", definitions[1].Part(Roles.Name)?.Text);
        Assert.AreEqual(LilyPondKinds.Sequential, definitions[0].Part(LilyPondRoles.Value)?.Kind);
    }

    [TestMethod]
    public void WhatNothingReadsIsHeld_AndSaysWhy()
    {
        foreach (var ly in new[] { "{ c4 } }", "{ c4 >> }", "= c4", "{ c4^ }", "{ \\bar \"|. }", "{ #(set! x }" })
        {
            var held = Written(LilyPondParser.Parse(ly)).Where(n => n.Kind == Kinds.Verbatim).ToList();
            Assert.IsTrue(held.Count > 0, $"{ly}: something should have been held");
            Assert.IsTrue(held.All(n => n.Trouble is not null), $"{ly}: and it should say why");
        }
    }

    [TestMethod]
    public void AGroupLeftOpenEndsWhereTheOneAroundItCloses()
    {
        // `<< { c d >>` — the brace was never closed, and it stops at the >> rather than swallowing it.
        var outer = LilyPondParser.Parse("<< { c4 d >> e").Children.First(n => n.Kind == LilyPondKinds.Simultaneous);

        Assert.IsNotNull(outer.Part(Roles.Close), "the << is closed by the >>");
        Assert.IsNull(outer.Children.First(n => n.Kind == LilyPondKinds.Sequential).Part(Roles.Close),
            "the brace inside it was never closed");
    }

    /// <summary>Everything written, outermost first, leaving out what a stage worked out.</summary>
    private static IEnumerable<ContentNode> Written(ContentNode node)
    {
        if (node.IsDerived) yield break;

        yield return node;
        foreach (var child in node.Children)
            foreach (var inner in Written(child))
                yield return inner;
    }

    /// <summary>The notes a piece plays, in the order written — not the pitches handed to commands.</summary>
    private static List<ContentNode> Played(ContentNode tree) =>
        [.. Written(tree).Where(n => n.Kind == LilyPondKinds.Note && n.Role != LilyPondRoles.Argument)];
}
