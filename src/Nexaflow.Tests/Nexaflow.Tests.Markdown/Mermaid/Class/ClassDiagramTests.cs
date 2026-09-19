using Nexaflow.Markdown.Mermaid.Class;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Class;

/// <summary>
/// What a <c>classDiagram</c> block says once its lines are read together: the classes and the members in each band of them, the
/// relations with what either end draws, the namespaces they are boxed into, and what styles them.
/// </summary>
[TestClass]
[CoversNode("class-diagram")]
public class ClassDiagramTests
{
    [TestMethod, TestCategory("Unit")]
    public void AClassIsDeclaredWhereItIsFirstWrittenAndUsedWhereverItIsWrittenAgain()
    {
        var diagram = ClassDiagram.Read("classDiagram\n  Animal <|-- Duck\n  Animal : +int age\n  class Duck");

        CollectionAssert.AreEqual(new[] { "Animal", "Duck" }, diagram.Nodes.Select(node => node.Id).ToArray());
        Assert.AreEqual("+int age", diagram.Find("Animal")!.Members.Single().Says);
    }

    [TestMethod, TestCategory("Unit")]
    public void AMemberWithBracketsAfterItsNameIsAMethodAndEverythingElseIsAField()
    {
        var node = ClassDiagram.Read("classDiagram\n  class A {\n    +int age\n    +grow()\n  }").Find("A")!;

        CollectionAssert.AreEqual(new[] { "+int age" }, node.Fields.Select(member => member.Says).ToArray());
        CollectionAssert.AreEqual(new[] { "+grow()" }, node.Methods.Select(member => member.Says).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void TypeParametersAreDrawnBetweenAngleBracketsAndAClassifierSaysHowTheMemberIsDrawn()
    {
        var node = ClassDiagram.Read("classDiagram\n  class Square~Shape~ {\n    List~int~ position\n    +count()$\n    +draw()*\n  }")
            .Find("Square")!;

        Assert.AreEqual("Shape", node.Generic);
        Assert.AreEqual("List<int> position", node.Fields.Single().Says);

        Assert.IsTrue(node.Methods.First(member => member.Says == "+count()").Fixed);
        Assert.IsTrue(node.Methods.First(member => member.Says == "+draw()").Abstract);
    }

    [TestMethod, TestCategory("Unit")]
    public void APackageVisibilityTildeIsNotATypeParameter()
    {
        var node = ClassDiagram.Read("classDiagram\n  class A {\n    ~int shared\n  }").Find("A")!;

        Assert.AreEqual("~int shared", node.Fields.Single().Says);
    }

    [TestMethod, TestCategory("Unit")]
    public void NestedTypeParametersCloseProperly()
    {
        var node = ClassDiagram.Read("classDiagram\n  Square : +getDistanceMatrix() List~List~int~~").Find("Square")!;

        Assert.AreEqual("+getDistanceMatrix() : List<List<int>>", node.Methods.Single().Says);
    }

    [TestMethod, TestCategory("Unit")]
    public void WhatAMethodGivesBackIsDrawnAfterAColonAndOneGivingNothingBackIsDrawnAsWritten()
    {
        var node = ClassDiagram.Read("classDiagram\n  Square : getId() int\n  Square : setId(int id)").Find("Square")!;

        Assert.AreEqual("getId() : int", node.Methods.First(member => member.Says.StartsWith("getId")).Says);
        Assert.AreEqual("setId(int id)", node.Methods.First(member => member.Says.StartsWith("setId")).Says);
    }

    [TestMethod, TestCategory("Unit")]
    public void AnInterfaceWrittenWithBracketsIsALollipopOnTheClassAndNeitherAClassNorARelation()
    {
        var diagram = ClassDiagram.Read("classDiagram\n  Class01 --() bar\n  foo ()-- Class01");

        Assert.IsNull(diagram.Find("bar"));
        Assert.IsNull(diagram.Find("foo"));
        Assert.AreEqual(0, diagram.Relations.Count);

        var lollipops = diagram.Find("Class01")!.Lollipops;
        Assert.AreEqual(2, lollipops.Count);
        Assert.IsTrue(lollipops.Any(one => one is { Name: "bar", Below: true }), "Class01 --() bar hangs below");
        Assert.IsTrue(lollipops.Any(one => one is { Name: "foo", Below: false }), "foo ()-- Class01 sits above");
    }

    [TestMethod, TestCategory("Unit")]
    public void ARelationWrittenBothWaysDrawsAHeadAtEitherEnd()
    {
        var relation = ClassDiagram.Read("classDiagram\n  Animal <|--|> Zebra").Relations.Single();

        Assert.AreEqual("Animal", relation.From);
        Assert.AreEqual("Zebra", relation.To);
        Assert.AreEqual(ClassEnd.Extension, relation.Head);
        Assert.AreEqual(ClassEnd.Extension, relation.Tail);
    }

    [TestMethod, TestCategory("Unit")]
    public void InheritanceDrawsItsHollowTriangleAtTheClassWrittenFirst()
    {
        // Animal <|-- Duck: the left operand is the parent, and the triangle sits at that end of the line.
        var relation = ClassDiagram.Read("classDiagram\n  Animal <|-- Duck").Relations.Single();

        Assert.AreEqual("Animal", relation.From);
        Assert.AreEqual(ClassEnd.Extension, relation.Head);
        Assert.AreEqual(ClassEnd.None, relation.Tail);
    }

    [TestMethod, TestCategory("Unit")]
    public void SeveralMembersWrittenAfterColonsGatherOnTheOneClass()
    {
        var node = ClassDiagram.Read("classDiagram\n  A : +int age\n  A : +String name\n  A : +grow()").Find("A")!;

        Assert.AreEqual(3, node.Members.Count);
        Assert.AreEqual(2, node.Fields.Count());
        Assert.AreEqual(1, node.Methods.Count());
    }

    [TestMethod, TestCategory("Unit")]
    public void AMemberEndingInTheLinkTokenPointsSomewhereAndIsNoLongerTheCharactersWritten()
    {
        var node = ClassDiagram.Read("classDiagram\n  class A {\n    +draw() @@file:///c:/a.cs#ast=T%3AA\n  }").Find("A")!;
        var member = node.Methods.Single();

        Assert.AreEqual("+draw()", member.Says);
        Assert.AreEqual("file:///c:/a.cs#ast=T%3AA", member.Href);
        Assert.IsFalse(member.Written);
    }

    [TestMethod, TestCategory("Unit")]
    public void AMemberWrittenAsItIsDrawnIsTypedInto()
    {
        var node = ClassDiagram.Read("classDiagram\n  class A {\n    +int age\n  }").Find("A")!;

        Assert.IsTrue(node.Fields.Single().Written);
    }

    [TestMethod, TestCategory("Unit")]
    public void AnAnnotationSaysWhatAClassIsWhereverItIsWritten()
    {
        Assert.AreEqual("interface", ClassDiagram.Read("classDiagram\n  class Shape <<interface>>").Find("Shape")!.Kind?.Text);
        Assert.AreEqual("interface", ClassDiagram.Read("classDiagram\n  class Shape\n  <<interface>> Shape").Find("Shape")!.Kind?.Text);
        Assert.AreEqual("enumeration",
                        ClassDiagram.Read("classDiagram\n  class Colour {\n    <<enumeration>>\n    RED\n  }").Find("Colour")!.Kind?.Text);
    }

    [TestMethod, TestCategory("Unit")]
    public void EachRelationSaysWhatItsEndsDrawAndWhetherItsLineIsDotted()
    {
        var diagram = ClassDiagram.Read(ClassGrammarTests.Related);
        var drawn = diagram.Relations.Select(relation => (relation.Head, relation.Tail, relation.Dotted)).ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                (ClassEnd.Extension, ClassEnd.None, false),
                (ClassEnd.Composition, ClassEnd.None, false),
                (ClassEnd.Aggregation, ClassEnd.None, false),
                (ClassEnd.None, ClassEnd.Association, false),
                (ClassEnd.None, ClassEnd.None, false),
                (ClassEnd.None, ClassEnd.Association, true),
                (ClassEnd.None, ClassEnd.Extension, true),
                (ClassEnd.None, ClassEnd.None, true),
            },
            drawn);
    }

    [TestMethod, TestCategory("Unit")]
    public void ARelationCarriesWhatIsWrittenOnItAndHowManyOfEachClassTheOtherHas()
    {
        var relation = ClassDiagram.Read("classDiagram\n  Customer \"1\" --> \"*\" Ticket : raises").Relations.Single();

        Assert.AreEqual("Customer", relation.From);
        Assert.AreEqual("Ticket", relation.To);
        Assert.AreEqual("1", relation.Near?.Text);
        Assert.AreEqual("*", relation.Far?.Text);
        Assert.AreEqual("raises", relation.Said?.Text);
    }

    [TestMethod, TestCategory("Unit")]
    public void QuotesPastTheOperatorAreTheClassWhereNoneFollowsThem()
    {
        var relation = ClassDiagram.Read("classDiagram\n  A --> \"Far away\"").Relations.Single();

        Assert.AreEqual("Far away", relation.To);
        Assert.IsNull(relation.Far);
    }

    [TestMethod, TestCategory("Unit")]
    public void AClassBelongsToTheNamespaceItIsFirstWrittenIn()
    {
        var diagram = ClassDiagram.Read(ClassGrammarTests.Spaces);
        var space = diagram.Spaces.Single();

        Assert.AreEqual("BaseShapes", space.Name);
        Assert.IsNull(space.Parent);
        CollectionAssert.AreEqual(new[] { "Triangle", "Rectangle" }, diagram.Inside(space.Key).Select(node => node.Id).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void ANamespaceWrittenWithDotsIsABoxForEachPartOfIt()
    {
        var diagram = ClassDiagram.Read("classDiagram\n  namespace A.B.C {\n    class One\n  }");

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, diagram.Spaces.Select(space => space.Name).ToArray());
        Assert.AreEqual("C", diagram.Spaces.Single(space => diagram.Inside(space.Key).Any()).Name);
    }

    [TestMethod, TestCategory("Unit")]
    public void NamespacesSharingTheirOuterNamesShareTheBoxesForThem()
    {
        var diagram = ClassDiagram.Read("classDiagram\n  namespace A.B {\n    class One\n  }\n  namespace A.C {\n    class Two\n  }");

        Assert.AreEqual(1, diagram.Spaces.Count(space => space.Name == "A"));
        Assert.AreEqual(3, diagram.Spaces.Count);
    }

    [TestMethod, TestCategory("Unit")]
    public void OneBoxHoldsTheWholeDottedNameWhereTheFrontMatterAsksForNoNesting()
    {
        var diagram = ClassDiagram.Read("---\nconfig:\n  class:\n    hierarchicalNamespaces: false\n---\n"
                                        + "classDiagram\n  namespace A.B {\n    class One\n  }");

        Assert.AreEqual("A.B", diagram.Spaces.Single().Name);
    }

    [TestMethod, TestCategory("Unit")]
    public void ANamespaceIsBoxedInsideTheOneItIsWrittenIn()
    {
        var diagram = ClassDiagram.Read("classDiagram\n  namespace Outer {\n    namespace Inner {\n      class One\n    }\n  }");
        var outer = diagram.Within(null).Single();

        Assert.AreEqual("Outer", outer.Name);
        Assert.AreEqual("Inner", diagram.Within(outer.Key).Single().Name);
    }

    [TestMethod, TestCategory("Unit")]
    public void ANoteSaysWhatItSaysAndWhichClassItIsBeside()
    {
        var diagram = ClassDiagram.Read("classDiagram\n  class A\n  note for A \"About A\"\n  note \"About nothing\"");

        Assert.AreEqual(("A", "About A"), (diagram.Notes[0].Of, diagram.Notes[0].Said?.Text));
        Assert.AreEqual((string.Empty, "About nothing"), (diagram.Notes[1].Of, diagram.Notes[1].Said?.Text));
    }

    [TestMethod, TestCategory("Unit")]
    public void ADirectionLineLaysTheDiagramOut()
    {
        Assert.AreEqual(ClassWay.Right, ClassDiagram.Read("classDiagram\n  direction LR\n  A --> B").Way);
        Assert.AreEqual(ClassWay.Down, ClassDiagram.Read("classDiagram\n  A --> B").Way);
    }

    [TestMethod, TestCategory("Unit")]
    public void AClassIsStyledByTheClassItIsGivenAndByTheStyleWrittenForIt()
    {
        var diagram = ClassDiagram.Read("classDiagram\n  class A:::blue\n  class B\n  classDef blue fill:#00f\n"
                                        + "  cssClass \"B\" blue\n  style A stroke:#f00");

        Assert.AreEqual("#00f", diagram.Find("A")!.Style.Fill);
        Assert.AreEqual("#f00", diagram.Find("A")!.Style.Stroke);
        Assert.AreEqual("#00f", diagram.Find("B")!.Style.Fill);
    }

    [TestMethod, TestCategory("Unit")]
    public void AClickLineSaysWhereAClassLeadsAndWhatItSaysWhilePointedAt()
    {
        var node = ClassDiagram.Read("classDiagram\n  class A\n  click A href \"https://example.com\" \"Go there\"").Find("A")!;

        Assert.AreEqual("https://example.com", node.Href);
        Assert.AreEqual("Go there", node.Tip);
    }

    [TestMethod, TestCategory("Unit")]
    public void AClassDrawnWithALabelIsCalledByItsIdAndDrawnWithItsLabel()
    {
        var node = ClassDiagram.Read("classDiagram\n  class Animal[\"Animal with a label\"]").Find("Animal")!;

        Assert.AreEqual("Animal with a label", node.Said?.Text);
    }

    [TestMethod, TestCategory("Unit")]
    public void AClassNamedInBackticksMayBeCalledAnythingAtAll()
    {
        var diagram = ClassDiagram.Read("classDiagram\n  class `Car Class!`\n  Animal --> `Car Class!`");

        Assert.IsNotNull(diagram.Find("Car Class!"));
        Assert.AreEqual("Car Class!", diagram.Relations.Single().To);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFrontMatterSaysWhetherAClassWithNoMembersKeepsABoxForThem()
    {
        Assert.IsFalse(ClassDiagram.Read("classDiagram\n  class A").Config.HideEmptyMembers);
        Assert.IsTrue(ClassDiagram.Read("---\nconfig:\n  class:\n    hideEmptyMembersBox: true\n---\nclassDiagram\n  class A")
                          .Config.HideEmptyMembers);
    }

    [TestMethod, TestCategory("Unit")]
    public void ABlockWithNothingInItReadsIntoNothingRatherThanThrowing()
    {
        Assert.AreEqual(0, ClassDiagram.Read("classDiagram").Nodes.Count);
        Assert.AreEqual(0, ClassDiagram.Read(null).Nodes.Count);
    }
}
