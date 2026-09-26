using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Class;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Class;

/// <summary>
/// What a <c>classDiagram</c> block's stages write into its tree: each namespace gathered with what is written in it, what
/// each member draws, what styles each class, and what its front matter asks for.
/// </summary>
[TestClass]
[CoversNode("class-diagram")]
public class ClassStagesTests
{
    /// <summary>What each member draws, in the order they are written.</summary>
    private static List<MemberNode> Members(string source) => [.. MermaidStaged.Read(source).SelfAndDescendants().OfType<MemberNode>()];

    /// <summary>What styles each class, by its name.</summary>
    private static Dictionary<string, MermaidStyle> Styles(string source) =>
        MermaidStaged.Read(source).SelfAndDescendants().OfType<StyledNode>().ToDictionary(named => named.Words()!.Text, named => named.Style);

    [TestMethod, TestCategory("Unit")]
    public void AMemberWithBracketsAfterItsNameIsAMethodAndEverythingElseIsAField()
    {
        var members = Members("classDiagram\n  class A {\n    +int age\n    +grow()\n  }");

        CollectionAssert.AreEqual(new[] { ("+int age", false), ("+grow()", true) }, members.Select(member => (member.Says, member.Method)).ToArray());
        Assert.IsTrue(members[0].Written, "a member drawn as it is written is typed into");
    }

    [TestMethod, TestCategory("Unit")]
    public void TypeParametersAreDrawnBetweenAngleBracketsAndAClassifierSaysHowTheMemberIsDrawn()
    {
        var members = Members("classDiagram\n  class Square~Shape~ {\n    List~int~ position\n    +count()$\n    +draw()*\n  }");

        Assert.AreEqual("List<int> position", members[0].Says);
        Assert.IsFalse(members[0].Written, "and so is drawn as it says rather than typed into");
        Assert.AreEqual(("+count()", true, false), (members[1].Says, members[1].Fixed, members[1].Abstract));
        Assert.AreEqual(("+draw()", false, true), (members[2].Says, members[2].Fixed, members[2].Abstract));
    }

    [TestMethod, TestCategory("Unit")]
    public void APackageVisibilityTildeIsNotATypeParameter() =>
        Assert.AreEqual("~int shared", Members("classDiagram\n  class A {\n    ~int shared\n  }").Single().Says);

    [TestMethod, TestCategory("Unit")]
    public void NestedTypeParametersCloseProperly_AndWhatAMethodGivesBackIsDrawnAfterAColon()
    {
        var members = Members("classDiagram\n  Square : +getDistanceMatrix() List~List~int~~\n  Square : getId() int\n  Square : setId(int id)");

        CollectionAssert.AreEqual(new[] { "+getDistanceMatrix() : List<List<int>>", "getId() : int", "setId(int id)" },
                                  members.Select(member => member.Says).ToArray(), "and one giving nothing back is drawn as written");
    }

    [TestMethod, TestCategory("Unit")]
    public void AMemberEndingInTheLinkTokenPointsSomewhereAndIsNoLongerTheCharactersWritten()
    {
        var member = Members("classDiagram\n  class A {\n    +draw() @@file:///c:/a.cs#ast=T%3AA\n  }").Single();

        Assert.AreEqual(("+draw()", "file:///c:/a.cs#ast=T%3AA", false), (member.Says, member.Href, member.Written));
    }

    [TestMethod, TestCategory("Unit")]
    public void EachNamespaceIsGatheredWithWhatIsWrittenInIt()
    {
        const string source = "classDiagram\n  namespace Outer {\n    namespace Inner {\n      class One\n    }\n    class Two\n  }";
        var tree = MermaidStaged.Read(source);
        var outer = tree.Children.Single(child => child.Kind == MermaidKinds.Group);

        Assert.AreEqual(1, outer.Children.Count(child => child.Kind == MermaidKinds.Group), "Inner inside Outer");
        Assert.AreEqual(source, tree.Print());
    }

    [TestMethod, TestCategory("Unit")]
    public void AClassIsStyledByTheClassesItIsGivenAndByTheStyleWrittenForIt()
    {
        var styles = Styles("classDiagram\n  class A:::blue,bold\n  class B\n  classDef blue fill:#00f\n  classDef bold stroke-width:3px\n"
                            + "  cssClass \"B\" blue,bold\n  style A stroke:#f00");

        Assert.AreEqual(("#00f", 3.0, "#f00"), (styles["A"].Fill, styles["A"].StrokeWidth, styles["A"].Stroke), "every class given at once, and the style over them");
        Assert.AreEqual(("#00f", 3.0), (styles["B"].Fill, styles["B"].StrokeWidth), "and a cssClass line gives every class it names too");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFrontMatterSaysWhetherAClassWithNoMembersKeepsABoxForThem()
    {
        static ClassConfig Config(string source) => ((ConfiguredNode<ClassConfig>)MermaidStaged.Read(source)).Config;

        Assert.IsFalse(Config("classDiagram\n  class A").HideEmptyMembers);
        Assert.IsTrue(Config("---\nconfig:\n  class:\n    hideEmptyMembersBox: true\n---\nclassDiagram\n  class A").HideEmptyMembers);
        Assert.IsFalse(Config("---\nconfig:\n  class:\n    hierarchicalNamespaces: false\n---\nclassDiagram\n  class A").Hierarchical);
    }
}
