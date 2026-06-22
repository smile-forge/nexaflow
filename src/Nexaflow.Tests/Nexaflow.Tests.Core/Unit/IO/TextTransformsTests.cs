using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Core.Unit.IO;

[TestClass]
public class TextTransformsTests
{
    [TestMethod]
    public void SplitLines_RoundTrips_AllTerminators()
    {
        var lines = TextTransforms.SplitLines("a\nb\r\nc\rd", out var trailing);
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, lines);
        Assert.IsFalse(trailing);
    }

    [TestMethod]
    public void SplitLines_DetectsTrailingNewline()
    {
        _ = TextTransforms.SplitLines("a\nb\n", out var trailing);
        Assert.IsTrue(trailing);
    }

    [TestMethod]
    public void RemoveEmptyLines_DropsBlankAndWhitespaceLines()
    {
        Assert.AreEqual("a\nb\n", TextTransforms.RemoveEmptyLines("a\n\n  \nb\n"));
    }

    [TestMethod]
    public void RemoveEmptyLines_WhitespaceKept_WhenDisabled()
    {
        Assert.AreEqual("a\n  \nb", TextTransforms.RemoveEmptyLines("a\n\n  \nb", whitespaceIsEmpty: false));
    }

    [TestMethod]
    public void RemoveDuplicateLines_Global_KeepsFirstPreservesOrder()
    {
        Assert.AreEqual("a\nb\nc", TextTransforms.RemoveDuplicateLines("a\nb\na\nc\nb"));
    }

    [TestMethod]
    public void RemoveDuplicateLines_AdjacentOnly_CollapsesRuns()
    {
        Assert.AreEqual("a\nb\na", TextTransforms.RemoveDuplicateLines("a\na\nb\na", adjacentOnly: true));
    }

    [TestMethod]
    public void SortLines_OrdinalAscendingAndDescending()
    {
        Assert.AreEqual("a\nb\nc", TextTransforms.SortLines("b\nc\na"));
        Assert.AreEqual("c\nb\na", TextTransforms.SortLines("b\nc\na", descending: true));
    }

    [TestMethod]
    public void SortLines_PreservesDominantCrlfAndTrailingNewline()
    {
        Assert.AreEqual("a\r\nb\r\n", TextTransforms.SortLines("b\r\na\r\n"));
    }

    [TestMethod]
    public void NormalizeLineEndings_RewritesMixedToTarget()
    {
        Assert.AreEqual("a\r\nb\r\nc\r\nd", TextTransforms.NormalizeLineEndings("a\nb\r\nc\rd", LineEnding.CrLf));
        Assert.AreEqual("a\nb\nc\nd",       TextTransforms.NormalizeLineEndings("a\nb\r\nc\rd", LineEnding.Lf));
    }

    [TestMethod]
    public void NormalizeLineEndings_Preserve_IsNoOp()
    {
        const string mixed = "a\nb\r\nc\rd";
        Assert.AreEqual(mixed, TextTransforms.NormalizeLineEndings(mixed, LineEnding.Preserve));
    }

    [TestMethod]
    public void Transforms_EmptyInput_ReturnEmpty()
    {
        Assert.AreEqual(string.Empty, TextTransforms.SortLines(string.Empty));
        Assert.AreEqual(string.Empty, TextTransforms.RemoveEmptyLines(string.Empty));
        Assert.AreEqual(string.Empty, TextTransforms.RemoveDuplicateLines(string.Empty));
    }
}
