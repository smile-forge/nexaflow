using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// A document read a unit at a time, as a screen reader reads it: the unit holding a place, a step to the next, and a search within a
/// stretch that never runs across a line break.
/// </summary>
[TestClass]
[CoversNode("content-editor")]
public class ContentTextUnitsTests
{
    private static readonly ContentDocument Written = ContentDocument.Of("int count = total;\nnext line\nlast");

    [TestMethod]
    public void TheUnitHoldingAPlace_IsItsCharacter_ItsWordWithTheSpaceAfter_ItsLineWithItsBreak_OrTheWhole()
    {
        Assert.AreEqual((5, 6), ContentTextUnits.Enclosing(Written, 5, ContentTextUnit.Character));
        Assert.AreEqual((4, 10), ContentTextUnits.Enclosing(Written, 6, ContentTextUnit.Word), "a word runs on over the space after it");
        Assert.AreEqual((19, 29), ContentTextUnits.Enclosing(Written, 22, ContentTextUnit.Line), "a line holds its line break");
        Assert.AreEqual((0, Written.Length), ContentTextUnits.Enclosing(Written, 22, ContentTextUnit.Document));
    }

    [TestMethod]
    public void StepsGoToTheNextStartOfAUnit_AndStopAtAnEnd_SayingHowFarTheyWent()
    {
        Assert.AreEqual(19, ContentTextUnits.Step(Written, 3, ContentTextUnit.Line, forward: true));
        Assert.AreEqual(0, ContentTextUnits.Step(Written, 3, ContentTextUnit.Line, forward: false), "back from the first line is its start");

        var (at, moved) = ContentTextUnits.Stepped(Written, 0, ContentTextUnit.Line, 5);
        Assert.AreEqual(Written.Length, at, "past the last line is the end");
        Assert.AreEqual(3, moved, "only the steps there were to take are counted");

        Assert.AreEqual((19, -1), ContentTextUnits.Stepped(Written, 29, ContentTextUnit.Line, -1));
    }

    [TestMethod]
    public void ASearchFindsTheFirstOrLastPlaceInsideTheStretch_ALineAtATime()
    {
        var document = ContentDocument.Of("alpha beta\nbeta gamma\nalpha");

        Assert.AreEqual((6, 10), ContentTextUnits.Found(document, 0, document.Length, "beta", backward: false, ignoreCase: false));
        Assert.AreEqual((11, 15), ContentTextUnits.Found(document, 0, document.Length, "beta", backward: true, ignoreCase: false));
        Assert.AreEqual((22, 27), ContentTextUnits.Found(document, 12, document.Length, "ALPHA", backward: false, ignoreCase: true));
        Assert.IsNull(ContentTextUnits.Found(document, 0, document.Length, "beta\nbeta", backward: false, ignoreCase: false), "nothing runs across a line break");
        Assert.IsNull(ContentTextUnits.Found(document, 0, 8, "beta", backward: false, ignoreCase: false), "nothing past the stretch's end");
    }
}
