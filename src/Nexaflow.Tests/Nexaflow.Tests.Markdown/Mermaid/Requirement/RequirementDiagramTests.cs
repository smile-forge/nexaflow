using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Requirement;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Requirement;

/// <summary>
/// What a <c>requirementDiagram</c> block says once its lines are read together: the requirements and the fields in each, what
/// holds between them and which way round it was written, and what styles them.
/// </summary>
[TestClass]
[CoversNode("requirement-diagram")]
public class RequirementDiagramTests
{
    [TestMethod, TestCategory("Unit")]
    public void ARequirementKeepsItsFieldsInTheOrderTheyAreWritten()
    {
        var node = RequirementDiagram
            .Read("requirementDiagram\n  requirement A {\n    id: 1\n    text: what it asks for\n    risk: high\n    verifymethod: test\n  }")
            .Find("A")!;

        CollectionAssert.AreEqual(new[] { "id", "text", "risk", "verifymethod" }, node.Facts.Select(fact => fact.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "1", "what it asks for", "high", "test" }, node.Facts.Select(fact => fact.Says).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void WhatIsDrawnInFrontOfAFieldIsMermaidsWordForItRatherThanTheKeyWritten()
    {
        var node = RequirementDiagram
            .Read("requirementDiagram\n  element E {\n    type: simulation\n    docRef: reqs/e\n  }")
            .Find("E")!;

        CollectionAssert.AreEqual(new[] { "Type", "Doc Ref" }, node.Facts.Select(fact => fact.Label).ToArray());
        Assert.AreEqual("Element", node.Says, "an element says what it is over its name like a requirement does");
    }

    [TestMethod, TestCategory("Unit")]
    public void WhatKindOfRequirementItIsIsDrawnTheWayMermaidSpacesIt()
    {
        var diagram = RequirementDiagram.Read(
            "requirementDiagram\n  functionalRequirement A {\n    id: 1\n  }\n  designConstraint B {\n    id: 2\n  }");

        Assert.AreEqual("Functional Requirement", diagram.Find("A")!.Says);
        Assert.AreEqual("Design Constraint", diagram.Find("B")!.Says);
    }

    [TestMethod, TestCategory("Unit")]
    public void AValueInQuotesSaysWhatIsBetweenThem()
    {
        var node = RequirementDiagram.Read("requirementDiagram\n  element E {\n    type: \"test suite\"\n  }").Find("E")!;

        Assert.AreEqual("test suite", node.Facts.Single().Says);
    }

    [TestMethod, TestCategory("Unit")]
    public void ARelationWrittenTheOtherWayRoundStillLeavesTheOneThatHolds()
    {
        var onward = RequirementDiagram.Read("requirementDiagram\n  a - satisfies -> b").Relations.Single();
        var back = RequirementDiagram.Read("requirementDiagram\n  b <- satisfies - a").Relations.Single();

        Assert.AreEqual(("a", "b", "satisfies"), (onward.From, onward.To, onward.Says));
        Assert.AreEqual(("a", "b", "satisfies"), (back.From, back.To, back.Says));
    }

    [TestMethod, TestCategory("Unit")]
    public void ContainsIsTheOneDrawnAsAWholeAndItsParts()
    {
        var diagram = RequirementDiagram.Read("requirementDiagram\n  a - contains -> b\n  a - derives -> c");

        Assert.IsTrue(diagram.Relations[0].Holds);
        Assert.IsFalse(diagram.Relations[1].Holds);
    }

    [TestMethod, TestCategory("Unit")]
    public void ARelationNamingWhatNoBlockWritesMakesTheBoxForIt()
    {
        var diagram = RequirementDiagram.Read("requirementDiagram\n  requirement A {\n    id: 1\n  }\n  A - traces -> B");

        CollectionAssert.AreEqual(new[] { "A", "B" }, diagram.Nodes.Select(node => node.Id).ToArray());
        Assert.IsNull(diagram.Find("B")!.Says, "nothing says what kind of thing it is, so nothing is drawn over its name");
    }

    [TestMethod, TestCategory("Unit")]
    public void ANameWrittenTwiceIsOneBox()
    {
        var diagram = RequirementDiagram.Read("requirementDiagram\n  a - traces -> b\n  requirement a {\n    id: 1\n  }");

        Assert.AreEqual(2, diagram.Nodes.Count);
        Assert.AreEqual("1", diagram.Find("a")!.Facts.Single().Says);
    }

    [TestMethod, TestCategory("Unit")]
    public void ANameWithNothingInItYetIsABoxOfItsOwnSoWritingItIsWatched()
    {
        var diagram = RequirementDiagram.Of(MermaidParser.Read("requirementDiagram\n  \"\" - satisfies -> \"\"", holes: true));

        Assert.AreEqual(2, diagram.Nodes.Count, "the two ends of a relation nobody has named yet are two boxes");
        Assert.IsTrue(diagram.Nodes.All(node => node.SaidHole is not null), "each with a hole where its name goes");
    }

    [TestMethod, TestCategory("Unit")]
    public void ANameOnALineOfItsOwnWritesTheBoxAndTakesTheClassGivenIt()
    {
        var diagram = RequirementDiagram.Read("requirementDiagram\n  A:::blue\n  classDef blue fill:#00f");

        Assert.AreEqual("A", diagram.Nodes.Single().Id);
        Assert.AreEqual("#00f", diagram.Find("A")!.Style.Fill);
    }

    [TestMethod, TestCategory("Unit")]
    public void AStyleLineAndAClassLineBothReachTheBoxTheyName()
    {
        var diagram = RequirementDiagram.Read(
            "requirementDiagram\n  a - traces -> b\n  classDef blue fill:#00f\n  class a blue\n  style b fill:#f00");

        Assert.AreEqual("#00f", diagram.Find("a")!.Style.Fill);
        Assert.AreEqual("#f00", diagram.Find("b")!.Style.Fill);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheWayItIsLaidOutIsWhatTheDirectionLineSays()
    {
        Assert.AreEqual(RequirementWay.Down, RequirementDiagram.Read("requirementDiagram\n  a - traces -> b").Way);
        Assert.AreEqual(RequirementWay.Right, RequirementDiagram.Read("requirementDiagram\n  direction LR\n  a - traces -> b").Way);
        Assert.AreEqual(RequirementWay.Up, RequirementDiagram.Read("requirementDiagram\n  direction BT\n  a - traces -> b").Way);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFrontMatterSaysHowSmallABoxMayBe()
    {
        var config = RequirementDiagram
            .Read("---\nconfig:\n  requirement:\n    rect_min_width: 140\n    rect_padding: 6\n---\nrequirementDiagram\n  a - traces -> b")
            .Config;

        Assert.AreEqual(140, config.MinWidth);
        Assert.AreEqual(6, config.Padding);
        Assert.AreEqual(RequirementConfig.Shortest, config.MinHeight, "and leaves the rest as they are");
    }
}
