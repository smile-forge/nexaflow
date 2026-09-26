using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Music.Abc;

/// <summary>
/// What a field's value is written as: a <c>K:</c>, <c>M:</c>, <c>L:</c> or <c>V:</c> split into its words — a key, figures,
/// <c>key=value</c> settings — and every other field held whole as the prose it is. Splitting is the parser's, so nothing
/// after it takes a field's characters apart; what the words mean is the stages'.
/// </summary>
[TestClass]
[CoversNode("abc-ast")]
public class AbcFieldTests
{
    [TestMethod]
    public void AKeyIsItsTonicItsSharpsOrFlatsAndItsMode()
    {
        var key = Words("K:Bbm clef=bass")[0];

        Assert.AreEqual(AbcKinds.Key, key.Kind);
        Assert.AreEqual("B", key.Part(Roles.Name)?.Text);
        Assert.AreEqual("b", key.Part(AbcRoles.Accidental)?.Text);
        Assert.AreEqual("m", key.Part(AbcRoles.Mode)?.Text);
    }

    [TestMethod]
    public void ASettingIsItsNameItsEqualsSignAndWhatItIsSetTo()
    {
        var setting = Words("K:Bbm clef=bass")[1];

        Assert.AreEqual(AbcKinds.Setting, setting.Kind);
        Assert.AreEqual("clef", setting.Part(Roles.Name)?.Text);
        Assert.AreEqual("=", setting.Part(Roles.Separator)?.Text);
        Assert.AreEqual("bass", setting.Part(AbcRoles.Value)?.Text);
    }

    [TestMethod]
    public void AQuotedSettingIsOneWordWhateverSpaceIsInIt()
    {
        var words = Words("V:1 name=\"Tenor Solo\"");

        Assert.AreEqual(2, words.Count);
        Assert.AreEqual("1", words[0].Text);
        Assert.AreEqual("Tenor Solo", words[1].Part(AbcRoles.Value)?.Part(Roles.Body)?.Text);
    }

    [TestMethod]
    public void AClefNamedOnItsOwnIsAWordAndNotAKey()
    {
        // A key is a capital letter from A to G; `bass` is a clef, whatever its first letter.
        Assert.AreEqual(AbcKinds.Word, Words("K:bass")[0].Kind);
        Assert.AreEqual(AbcKinds.Setting, Words("K:clef=treble")[0].Kind);
    }

    [TestMethod]
    public void AMeterIsItsNumbersAndTheMarksBetweenThem()
    {
        var figures = Words("M:(2+3)/8")[0];

        Assert.AreEqual(AbcKinds.Figures, figures.Kind);
        CollectionAssert.AreEqual(new[] { "(", "2", "+", "3", ")", "/", "8" }, figures.Children.Select(piece => piece.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "2", "3", "8" },
                                  figures.Children.Where(piece => piece.Kind == AbcKinds.Number).Select(piece => piece.Text).ToArray());
    }

    [TestMethod]
    public void AMeterWrittenAsASignIsAWord()
    {
        Assert.AreEqual("C|", Words("M:C|")[0].Text);
        Assert.AreEqual(AbcKinds.Word, Words("M:C|")[0].Kind);
    }

    [TestMethod]
    public void AnInlineFieldIsSplitTheSameWay()
    {
        var field = AbcParser.Parse("X:1\nK:C\nAB [K:Dm] cd|\n").SelfAndDescendants().Single(node => node.Kind == AbcKinds.InlineField);

        Assert.AreEqual(AbcKinds.Key, field.Part(AbcRoles.Value)!.Children[0].Kind);
    }

    [TestMethod]
    public void EveryOtherFieldIsHeldWholeAsProse()
    {
        var value = Field("T:My tune = the best one").Part(AbcRoles.Value)!;

        Assert.IsTrue(value.IsLeaf);
        Assert.AreEqual("My tune = the best one", value.Text);
    }

    [TestMethod]
    public void EverySplitValuePrintsAsItWasWritten()
    {
        foreach (var line in new[]
                 {
                     "K:Bbm clef=bass", "K:F# mix", "K:none", "K:AMix=g", "K: Dm octave=1", "M:C", "M:(2+3)/8", "M:4 3/4",
                     "L:1/16", "V:1 clef=bass name=\"Tenor Solo\"", "V:2 nm=\"Unclosed", "K:", "M:", "K:C=",
                 })
        {
            var abc = "X:1\n" + line + "\n";
            Assert.AreEqual(abc, AbcParser.Parse(abc).Print(), line);
        }
    }

    private static ContentNode Field(string line) =>
        AbcParser.Parse("X:1\n" + line + "\n").Children.First(child => child.Kind == AbcKinds.Field && child.Print().StartsWith(line[..2], StringComparison.Ordinal));

    private static IReadOnlyList<ContentNode> Words(string line) =>
        [.. Field(line).Part(AbcRoles.Value)!.Children.Where(child => child.Role != Roles.Trivia)];
}
