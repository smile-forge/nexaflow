using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// What prose lets be written into it (<see cref="MarkdownParser.Rewrite"/>): the reader means words, and the parser
/// says how those words have to be spelled for the document to still be the document it was.
///
/// <para>
/// Prose is the one language that keeps the lines it was handed, so it is the only one where where the words land
/// decides how they are spelled. A hash is a heading at the start of a line and a hash everywhere else, and the
/// difference is not visible in the characters — only in the position, which is why the spelling is asked for one.
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class MarkdownTranspileTests
{
    /// <summary>Every character markdown gives a meaning to somewhere.</summary>
    private const string Typed = @"\`*_[]~&<>#-+=:.|";

    private const string Document = """
        # Title

        A paragraph with *emphasis*, a [link](https://example.com) and `code` in it.

        > A quote.

        - First item
        - Second item

        | Head | Also |
        | --- | --- |
        | Cell | Here |

        Last paragraph.
        """;

    [TestMethod]
    public void WordsThatMeanNothingElseGoInAsTheyAre()
    {
        Assert.AreEqual("xy", Written(At("paragraph"), "xy"));
        Assert.AreEqual("1 + 2", Written(At("paragraph"), "1 + 2"), "a plus in the middle of a line is a plus");
    }

    [TestMethod]
    public void EveryCharacterReadAsMarkupWhereverItStandsGoesInAsThatCharacter()
    {
        var into = At("paragraph");

        Assert.AreEqual(@"\*", Written(into, "*"), "left bare it would open emphasis");
        Assert.AreEqual(@"\_", Written(into, "_"));
        Assert.AreEqual(@"\`", Written(into, "`"), "a backtick would open a code span");
        Assert.AreEqual(@"\[\]", Written(into, "[]"), "brackets would make a link of what follows");
        Assert.AreEqual(@"\~", Written(into, "~"));
        Assert.AreEqual(@"\\", Written(into, @"\"), "and a backslash would escape whatever came after it");
    }

    [TestMethod]
    public void AHashIsAHeadingOnlyWhereALineStarts()
    {
        // The whole reason the spelling is asked where the words go. The same character, the same part, two answers.
        var paragraph = At("paragraph");

        Assert.AreEqual("#", Written(paragraph, "#"), "a hash in the middle of a line is a hash");

        var starting = At("A");
        Assert.AreEqual(@"\#", Spelled(starting, starting.Start, "#"), "and at the start of one it would be a heading");
    }

    [TestMethod]
    public void AndSoAreTheOtherThingsOnlyReadWhereALineStarts()
    {
        var starting = At("A");

        foreach (var (character, what) in new[]
                 {
                     (">", "a quote"), ("-", "a list item or a rule"), ("+", "a list item"),
                     ("=", "an underlined heading"), (":", "a definition"),
                 })
            Assert.AreEqual(@"\" + character, Spelled(starting, starting.Start, character), what);

        Assert.AreEqual(@"1\.", Spelled(starting, starting.Start, "1."), "and digits with a dot are a numbered item");
    }

    [TestMethod]
    public void APipeIsACellOnlyInATable()
    {
        Assert.AreEqual("|", Written(At("paragraph"), "|"), "a pipe in a paragraph is a pipe");
        Assert.AreEqual(@"\|", Written(At("Cell"), "|"), "and in a table it would divide one");
    }

    [TestMethod]
    public void AnEntityAndATagGoInWhateverFollowsThem()
    {
        // These two are closed by what comes after them, and what comes after them can be the document's own next
        // character rather than anything carried in — a less-than written at the front of a link's target has nothing
        // after it in the words at all, and still opens one. A run of words cannot see past its own end, so both go
        // in spelled whatever follows.
        var into = At("paragraph");

        Assert.AreEqual(@"Tom \& Jerry", Written(into, "Tom & Jerry"));
        Assert.AreEqual(@"\&amp; ", Written(into, "&amp; "), "which is the one that would have been read as a character");
        Assert.AreEqual(@"a \< b", Written(into, "a < b"));
        Assert.AreEqual(@"\<b>", Written(into, "<b>"), "and the one that would have been read as a tag");
    }

    [TestMethod]
    public void ABreakIsKeptBecauseAParagraphIsMeantToStayOne()
    {
        // Every other language collapses what it is handed onto one line. Prose cannot: two paragraphs carried in are
        // meant to arrive as two. So the break stays, and what follows it is spelled as the start of a line.
        var into = At("paragraph");

        Assert.AreEqual("one\ntwo", Written(into, "one\ntwo"), "the break is the one that was carried");
        Assert.AreEqual("one\n\\# two", Written(into, "one\n# two"), "and the second line starts one");
    }

    [TestMethod]
    public void NothingIsWrittenWhereABackslashIsNotAnEscape()
    {
        // A code span holds its characters as they are, so there is no spelling that would make one safe there.
        Assert.IsNull(Rewritten(At("code"), At("code").End, "`"), "nothing goes into a code span");
    }

    [TestMethod]
    public void WhateverIsWrittenIntoProseLeavesTheDocumentTheShapeItWas()
    {
        // The acceptance property, and the prose half of what DiagramEscapingTests holds every diagram to: a character
        // meant as a word never becomes markup. Said as the shape of the document, because that is what markup is.
        var was = Shaped(Document);
        var tried = 0;

        foreach (var part in Leaves(Document))
            foreach (var character in Typed)
            {
                if (Rewritten(part, part.Start, character.ToString()) is not { } change) continue;

                var landed = Made(Document, change);
                tried++;

                Assert.AreEqual(landed, MarkdownParser.Read(landed).Print(), landed);
                Assert.AreEqual(0, AstOracle.Faults(landed, MarkdownParser.Read(landed)).Count(), landed);
                var now = Shaped(landed);

                Assert.AreEqual(string.Join(" ", was), string.Join(" ", now),
                                $"'{character}' written before '{part.Print()}' ({part.Kind}/{part.Role}) changed the "
                                + $"document.\ngained: {string.Join(", ", now.Except(was))}\n"
                                + $"lost: {string.Join(", ", was.Except(now))}\n{landed}");
            }

        Assert.IsTrue(tried > 100, $"the document gives plenty to write into, not {tried}");
    }

    // ── What the fixtures mean ──────────────────────────────────────────────

    private static ContentPart Read(string source) =>
        ContentPart.Of(new MarkdownBlocks().Read(MarkdownParser.Read(source)));

    /// <summary>
    /// The smallest piece of the document holding where <paramref name="said"/> is written — found by where it stands
    /// rather than by what a piece prints, because how prose is cut into pieces is the parser's business and not a
    /// thing a test should know.
    /// </summary>
    private static ContentPart At(string said)
    {
        var at = Document.IndexOf(said, StringComparison.Ordinal);
        Assert.IsTrue(at >= 0, $"'{said}' is not written in the document");

        return Read(Document).SelfAndDescendants()
            .Where(part => part.Children.Count == 0 && part.Start <= at && at < part.End)
            .OrderBy(part => part.Length)
            .First();
    }

    /// <summary>Every piece of a document holding characters, which is everywhere words can be carried.</summary>
    private static IReadOnlyList<ContentPart> Leaves(string source) =>
        [.. Read(source).SelfAndDescendants().Where(part => part.Children.Count == 0 && part.Length > 0)];

    /// <summary>
    /// The constructs a document is made of, which is what must not change.
    ///
    /// <para>
    /// Words, escapes and the sequence a word holding one is read as are all left out. A word is what is being
    /// written; an escape and its sequence are the parser reading back the very spelling it asked for, so counting
    /// them would make every answer a change.
    /// </para>
    /// </summary>
    private static List<string> Shaped(string source) =>
        [.. Read(source).SelfAndDescendants()
            .Select(part => part.Kind)
            .Where(kind => kind is not (Kinds.Token or Kinds.Space or Kinds.Sequence
                                        or MarkdownKinds.Word or MarkdownKinds.Escape))
            .Order()];

    private static ContentChange? Rewritten(ContentPart part, int at, string text) =>
        MarkdownParser.Rewrite(new ContentChange([ContentWrite.Words(part, at, 0, text)], at + text.Length));

    /// <summary>What <paramref name="text"/> is spelled as going in at <paramref name="at"/>.</summary>
    private static string Spelled(ContentPart part, int at, string text)
    {
        var change = Rewritten(part, at, text);

        Assert.IsNotNull(change, $"'{text}' was refused where it should have been written");
        return change.Writes.Single().Text;
    }

    /// <summary>And going in at the end of a part, which is in the middle of a line.</summary>
    private static string Written(ContentPart part, string text) => Spelled(part, part.End, text);

    private static string Made(string source, ContentChange change)
    {
        foreach (var write in change.Writes.OrderByDescending(write => write.Start))
            source = string.Concat(source.AsSpan(0, write.Start), write.Text, source.AsSpan(write.End));

        return source;
    }
}
