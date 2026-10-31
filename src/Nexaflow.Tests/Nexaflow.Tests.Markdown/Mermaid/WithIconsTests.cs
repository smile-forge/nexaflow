using Nexaflow.Icons;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// Every way a diagram names an icon, read as the icon the app draws (<see cref="WithIcons"/>): each grammar says what names
/// one as it reads it, and the one stage says which icon it is and the glyph that draws it — or leaves it as written, where
/// the app draws no icon of that name.
/// </summary>
[TestClass]
[CoversNode("mermaid-block-ast")]
public class WithIconsTests
{
    private static List<RenderedIconNode> Rendered(string source) => [.. MermaidStaged.Read(source).SelfAndDescendants().OfType<RenderedIconNode>()];

    [TestMethod]
    [DataRow("mindmap\n  root\n    ::icon(fa fa-book)", "book")]
    [DataRow("kanban\n  Todo\n    task[Write it]\n    ::icon(fa:fa-user)", "person")]
    [DataRow("kanban\n  Todo\n    task[Write it]@{ icon: 'mdi:account' }", "person")]
    [DataRow("architecture-beta\n  service db(database)[Database]", "database")]
    [DataRow("architecture-beta\n  service web(internet)[Web]", "globe")]
    [DataRow("flowchart TD\n  A@{ icon: \"fa:user\", form: \"square\", label: \"User\" }", "person")]
    [DataRow("mindmap\n  root\n    ::icon(fluent:rocket)", "rocket")]
    public void EveryWayADiagramNamesAnIconIsTheFluentIconDrawingIt(string source, string fluent)
    {
        Assert.AreEqual(IconRef.Fluent(fluent), Rendered(source).Single().Icon);
    }

    [TestMethod]
    public void AnEmojiIsItsOwnIcon() => Assert.AreEqual(IconRef.Emoji("🚀"), Rendered("mindmap\n  root\n    ::icon(🚀)").Single().Icon);

    [TestMethod]
    public void APackShippedWithTheAppIsDrawnAsItself_AndANameItHasNoneOfIsNoIcon()
    {
        Assert.AreEqual(IconRef.Iconify("material-icon-theme", "rust"), MermaidIcons.Of("material-icon-theme:rust"));
        Assert.IsNull(MermaidIcons.Of("material-icon-theme:no-such-icon"), "the pack is here, and has no icon of that name");
        Assert.AreEqual(IconRef.Iconify("material-icon-theme", "react-ts"), Rendered("kanban\n  Todo\n    task[Write it]@{ icon: 'material-icon-theme:react-ts' }").Single().Icon);
    }

    [TestMethod]
    public void ABareNameIsAnIconOfTheDefaultPack_AfterTheIconsMermaidDrawsItself()
    {
        Assert.AreEqual(IconRef.Iconify("material-icon-theme", "rust"), MermaidIcons.Of("rust", "material-icon-theme"));
        Assert.AreEqual(IconRef.Fluent("folder"), MermaidIcons.Of("folder", "material-icon-theme"), "folder is Mermaid's own, though the pack has one too");
        Assert.AreEqual(IconRef.Fluent("person"), MermaidIcons.Of("user", "material-icon-theme"), "a name the pack has none of is read as before");
    }

    [TestMethod]
    public void AnIconTheAppDrawsNoneOfIsLeftAsWritten()
    {
        var tree = MermaidStaged.Read("architecture-beta\n  service a(logos:aws-lambda)[A]");

        Assert.AreEqual(0, tree.SelfAndDescendants().OfType<RenderedIconNode>().Count());
        Assert.AreEqual(1, tree.SelfAndDescendants().Count(node => node.Kind == MermaidKinds.Icon), "it still names an icon");
    }

    [TestMethod]
    public void AnIconPrintsAsItWasWritten()
    {
        const string source = "flowchart TD\n  A@{ icon: \"fa:user\", form: \"square\", label: \"User\" }";

        Assert.AreEqual(source, MermaidStaged.Read(source).Print());
    }

    [TestMethod]
    [DataRow("fa fa-book", "book", false)]
    [DataRow("fa-solid fa-user", "person", false)]
    [DataRow("fa:fa-user", "person", false)]
    [DataRow("mdi:account", "person", false)]
    [DataRow("disk", "hard_drive", false)]
    [DataRow("fluent-filled:home", "home", true)]
    public void AnotherPacksNameIsTheFluentIconDrawingTheSameThing(string written, string fluent, bool filled) =>
        Assert.AreEqual(IconRef.Fluent(fluent, filled), MermaidIcons.Of(written));

    [TestMethod]
    public void ANameNothingDrawsIsNoIcon()
    {
        Assert.IsNull(MermaidIcons.Of("logos:aws-lambda"));
        Assert.IsNull(MermaidIcons.Of(""));
    }
}
