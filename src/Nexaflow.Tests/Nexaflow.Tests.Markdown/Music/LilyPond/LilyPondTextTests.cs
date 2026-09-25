using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.LilyPond;

/// <summary>
/// What LilyPond's strings, names, chord names and syllables say, read off the pieces the parser made of them rather than by
/// taking their characters apart — and every one of them still printing as what was written.
/// </summary>
[TestClass]
[CoversNode("ly-ast")]
public class LilyPondTextTests
{
    private static ContentPart Read(string source)
    {
        var tree = LilyPondParser.Parse(source);
        Assert.AreEqual(source, tree.Print(), "it prints as what was written");
        return ContentPart.Of(tree);
    }

    private static ContentPart First(ContentPart tree, string kind) => tree.SelfAndDescendants().First(part => part.Kind == kind);

    [TestMethod]
    public void AQuotedStringIsItsQuotesAndALetterForEachCharacter_AnEscapeBeingOne()
    {
        var quoted = First(Read(@"\header { title = ""Say \""hi\"""" }"), LilyPondKinds.Quoted);

        Assert.AreEqual("Say \"hi\"", LilyPondText.Said(quoted));
        Assert.AreEqual(8, LilyPondText.Letters(quoted).Count, "an escape is the one character it writes");
        Assert.AreEqual(2, LilyPondText.Letters(quoted).Count(letter => letter.Kind == LilyPondKinds.Escape));
    }

    [TestMethod]
    public void AVariableCalledInQuotesIsCalledByWhatTheQuotesSay()
    {
        var tree = Read("\"voice1\" = { c d }\n{ \\\"voice1\" \\melody }");
        var calls = tree.SelfAndDescendants().Where(part => part.Kind == LilyPondKinds.Command).Select(LilyPondText.Called).ToArray();

        CollectionAssert.AreEqual(new[] { "voice1", "melody" }, calls);
        Assert.AreEqual("voice1", LilyPondText.Said(First(tree, LilyPondKinds.Assignment).Part(Roles.Name)));
    }

    [TestMethod]
    public void AChordNamesQualityAndBassArePartsOfTheirOwn()
    {
        var names = Read(@"\chordmode { d2:m7/f g1:maj c:7^5/+e }").SelfAndDescendants().Where(part => part.Kind == LilyPondKinds.ChordName).ToList();

        CollectionAssert.AreEqual(new[] { "m7", "maj", "7^5" }, names.Select(name => name.Part(LilyPondRoles.Quality)?.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "f", null, "e" }, names.Select(name => name.Part(LilyPondRoles.Bass)?.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "m7", "maj7", "7no5" }, names.Select(LilyPondText.Quality).ToArray());
    }

    [TestMethod]
    public void ASyllablesDurationIsAPartOfItsOwn_AndWhatItSingsIsItsWords()
    {
        var syllables = Read(@"\lyricmode { Twin4. kle sky. rain_drop }").SelfAndDescendants()
            .Where(part => part.Kind == LilyPondKinds.Syllable).ToList();

        CollectionAssert.AreEqual(new[] { "Twin", "kle", "sky.", "rain drop" }, syllables.Select(LilyPondText.Sung).ToArray());
        Assert.AreEqual("4.", syllables[0].Part(LilyPondRoles.Duration)?.Text);
    }
}

