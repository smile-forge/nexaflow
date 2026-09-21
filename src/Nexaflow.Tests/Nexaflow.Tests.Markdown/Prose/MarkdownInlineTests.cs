using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// The tree a block's text is read into: what was written prints back as it was written, and every mark the
/// writer typed is still in it — which is what lets bold text be drawn bold and still be typed in.
///
/// <para>
/// The stretches include what nobody means to write, a bracket that closes nothing and a backtick on its own,
/// because text is read on every keystroke and most of what it is handed is half-written.
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class MarkdownInlineTests
{
    private static readonly (string What, string Source)[] Stretches =
    [
        ("nothing at all", ""),
        ("plain words", "just some words"),
        ("emphasis", "an *emphatic* word"),
        ("strong", "a **heavy** word"),
        ("both at once", "***all of it***"),
        ("underscores", "an _emphatic_ word"),
        ("a name with underscores", "snake_case_name"),
        ("asterisks inside a word", "a*b*c"),
        ("multiplication", "2 * 3 * 4"),
        ("nested", "**heavy with *slant* in it**"),
        ("struck through", "~~gone~~"),
        ("under and over", "H~2~O and x^2^"),
        ("marked and inserted", "==this== and ++that++"),
        ("code", "a `span` of code"),
        ("code holding a backtick", "``a ` tick``"),
        ("code holding asterisks", "`a * b * c`"),
        ("a link", "see [the page](https://example.org)"),
        ("a link with a title", "see [the page](https://example.org \"over there\")"),
        ("a link with words of its own", "[a *slanted* word](x)"),
        ("a picture", "![a cat](cat.png)"),
        ("an autolink", "<https://example.org>"),
        ("a bare url", "see https://example.org for more"),
        ("an entity", "a &amp; b &#39;c&#39;"),
        ("an escape", "a \\* b \\_ c"),
        ("a heading's hashes", "# A Title"),
        ("an item's bullet", "- a thing"),
        ("a ticked item", "- [x] done"),
        ("an unticked item", "- [ ] to do"),
        ("a table cell", "**Yes** / *no* — see `Flag`"),
        ("an opening mark that closes nothing", "*unclosed"),
        ("a bracket that closes nothing", "[words"),
        ("a bracket pointing nowhere", "[words] and more"),
        ("a backtick on its own", "a ` tick"),
        ("marks and no words", "***"),
        ("every mark at once", "*a* **b** ~~c~~ `d` [e](f) ![g](h) &amp;"),
        ("only space", "   "),
    ];

    [TestMethod]
    public void EveryStretchReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in Stretches)
            Assert.AreEqual(source, MarkdownInline.Read(source).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryStretchReadsBackToo()
    {
        foreach (var (what, source) in Stretches)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, MarkdownInline.Read(typed).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void TheReaderOnlyEverCopies()
    {
        foreach (var (what, source) in Stretches)
            foreach (var place in MarkdownInline.Read(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= source.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {source.Length}");

                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
    }

    // ── What each construct is read as ──────────────────────────────────────

    [TestMethod]
    public void OneMarkEachSideIsEmphasisAndTwoIsStrong()
    {
        Assert.AreEqual("slant", Body(One("*slant*", MarkdownKinds.Emphasis)));
        Assert.AreEqual("heavy", Body(One("**heavy**", MarkdownKinds.Strong)));
        Assert.AreEqual("slant", Body(One("_slant_", MarkdownKinds.Emphasis)));
    }

    [TestMethod]
    public void TheMarksAreKeptWhereTheyWereTyped()
    {
        var strong = One("**heavy**", MarkdownKinds.Strong);

        Assert.AreEqual("**", strong.Part(Roles.Open)?.Text);
        Assert.AreEqual("**", strong.Part(Roles.Close)?.Text);
        Assert.AreEqual("**heavy**", strong.Print());
    }

    [TestMethod]
    public void AnUnderscoreInsideAWordIsACharacter()
    {
        // Which is the whole reason markdown treats the two marks differently: names are written like this.
        Assert.AreEqual(0, Every("snake_case_name", MarkdownKinds.Emphasis).Count);
        Assert.AreEqual("b", Body(One("a*b*c", MarkdownKinds.Emphasis)));
    }

    [TestMethod]
    public void AMarkWithSpaceAfterItOpensNothing()
    {
        Assert.AreEqual(0, Every("2 * 3 * 4", MarkdownKinds.Emphasis).Count);
    }

    [TestMethod]
    public void TheExtraMarksAreTheirOwnConstructs()
    {
        Assert.AreEqual("gone", Body(One("~~gone~~", MarkdownKinds.Strike)));
        Assert.AreEqual("2", Body(One("H~2~O", MarkdownKinds.Sub)));
        Assert.AreEqual("2", Body(One("x^2^", MarkdownKinds.Sup)));
        Assert.AreEqual("this", Body(One("==this==", MarkdownKinds.Mark)));
        Assert.AreEqual("that", Body(One("++that++", MarkdownKinds.Insert)));
    }

    [TestMethod]
    public void NothingInsideACodeSpanIsRead()
    {
        var code = One("`a * b * c`", MarkdownKinds.Code);

        Assert.AreEqual("a * b * c", Body(code));
        Assert.AreEqual(Kinds.Verbatim, code.Part(Roles.Body)?.Kind);
        Assert.AreEqual(0, Every("`a * b * c`", MarkdownKinds.Emphasis).Count);
    }

    [TestMethod]
    public void ALinkIsItsWordsAndWhereItPoints()
    {
        var link = One("[the page](https://example.org)", MarkdownKinds.Link);

        Assert.AreEqual("the page", Body(link));
        Assert.AreEqual("https://example.org", link.Part(MarkdownRoles.Destination)?.Text);
    }

    [TestMethod]
    public void ALinksWordsAreReadAsWordsAre()
    {
        Assert.AreEqual("slanted", Body(One("[a *slanted* word](x)", MarkdownKinds.Emphasis)));
    }

    [TestMethod]
    public void APictureIsALinkThatShowsWhatItPointsAt()
    {
        var picture = One("![a cat](cat.png)", MarkdownKinds.Image);

        Assert.AreEqual("cat.png", picture.Part(MarkdownRoles.Destination)?.Text);
    }

    // ── What a reader can tick ──────────────────────────────────────────────

    [TestMethod]
    public void ATickedItemSaysSoAndAnUntickedOneSaysSoToo()
    {
        Assert.IsNotNull(One("- [x] done", MarkdownKinds.Task).Part(MarkdownRoles.Done));
        Assert.IsNotNull(One("- [ ] to do", MarkdownKinds.Task).Part(MarkdownRoles.Todo));
    }

    [TestMethod]
    public void TheMarksOfATickAreKeptSoItCanBeWrittenIn()
    {
        // What is drawn is a box; what is there is three characters, and ticking one writes over them.
        Assert.AreEqual("[x]", One("- [x] done", MarkdownKinds.Task).Print());
    }

    // ── What nobody finished typing ─────────────────────────────────────────

    [TestMethod]
    public void AMarkThatClosesNothingIsACharacter()
    {
        Assert.AreEqual(0, Every("*unclosed", MarkdownKinds.Emphasis).Count);
        Assert.AreEqual(0, Every("***", MarkdownKinds.Strong).Count);
    }

    [TestMethod]
    public void ABracketPointingNowhereIsABracket()
    {
        Assert.AreEqual(0, Every("[words] and more", MarkdownKinds.Link).Count);
        Assert.AreEqual(0, Every("[words", MarkdownKinds.Link).Count);
    }

    [TestMethod]
    public void ABacktickThatClosesNothingIsACharacter()
    {
        Assert.AreEqual(0, Every("a ` tick", MarkdownKinds.Code).Count);
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static ContentNode One(string source, string kind)
    {
        var found = Every(source, kind);
        Assert.AreEqual(1, found.Count, $"'{source}' should hold one {kind}");

        return found[0];
    }

    private static IReadOnlyList<ContentNode> Every(string source, string kind) =>
        [.. MarkdownInline.Read(source).SelfAndDescendants().Where(node => node.Kind == kind)];

    private static string Body(ContentNode node) => node.Part(Roles.Body)?.Print() ?? string.Empty;
}
