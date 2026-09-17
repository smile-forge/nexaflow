using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// The line reader's pieces as any diagram reads with them: a list whose items are more than their names, a number or a
/// setting written among others, a piece closed with what is wrong with it — and what a number and the front matter are held to.
/// </summary>
[TestClass]
[CoversNode("mermaid-diagram-kit")]
public class MermaidLineTests
{
    /// <summary>An item that is a name, and a label in brackets where one follows it.</summary>
    private static bool Item(MermaidLine line)
    {
        if (!line.Name("id", char.IsLetter)) return false;
        if (line.Past != '[') return true;

        line.Space();
        return line.Label("[", "]", "label");
    }

    [TestMethod]
    public void AListsItemsThatAreMoreThanTheirNamesAreEachAPieceOfTheirOwn()
    {
        var line = MermaidLine.Of("a[\"Alpha\"], b");
        Assert.IsTrue(line.Names(Item, Roles.Element, "id", item: "item"));

        var list = line.Read("line").Children.Single();

        Assert.AreEqual(2, list.Children.Count(child => child.Kind == "item"));
        CollectionAssert.AreEqual(new[] { "a", "b" }, list.SaidNames().ToArray(), "and their names are still the list's names");
        CollectionAssert.AreEqual(new[] { "a", "b" }, ContentReading.Of(list).Root.Named().Select(name => name.Words()!.Text).ToArray());
    }

    [TestMethod]
    public void ANameStillToWriteIsAnItemToo_WhetherNothingIsWrittenYetOrASeparatorIsLast()
    {
        foreach (var (text, count) in new[] { ("", 1), ("a, ", 2) })
        {
            var line = MermaidLine.Of(text);
            Assert.IsTrue(line.Names(Item, Roles.Element, "id", item: "item"), text);

            var items = line.Read("line").Children.Single().Children.Where(child => child.Kind == "item").ToList();

            Assert.AreEqual(count, items.Count, text);
            Assert.AreEqual(string.Empty, items[^1].Inner(MermaidKinds.Words)!.Text, $"'{text}': the last is a name still to write");
        }
    }

    [TestMethod]
    public void ANumberAmongOthersEndsWhereItDoes_TheSpaceBeforeTheNextNotItsOwn()
    {
        var line = MermaidLine.Of("12 , 3}");
        line.Amount("value", MermaidNumber.Positive("more than nought"), until: ",}");

        Assert.AreEqual("12", line.Read("line").Inner(MermaidKinds.Number)!.Text);
        Assert.AreEqual(" , 3}", line.Rest);
    }

    [TestMethod]
    public void ASettingIsWhatIsWrittenUpToTheNext_WithWhatIsWrongWithIt()
    {
        var line = MermaidLine.Of("polygn, x");
        line.Setting("value", value => value == "polygon" ? null : "no such shape", until: ",");

        var setting = line.Read("line").Inner(MermaidKinds.Setting)!;
        Assert.AreEqual("polygn", setting.Text);
        Assert.AreEqual("no such shape", setting.Trouble);

        var empty = MermaidLine.Of(string.Empty);
        empty.Setting("value", _ => "never asked");
        Assert.IsNull(empty.Read("line").Inner(MermaidKinds.Setting)!.Trouble, "nothing written yet is still to come");
    }

    [TestMethod]
    public void APieceClosedWithWhatIsWrongWithItCarriesIt()
    {
        var line = MermaidLine.Of("{1");
        line.Open();
        line.Token("{", Roles.Open);

        Assert.AreEqual("never closed", line.Close("values", trouble: "never closed").Trouble);
    }

    [TestMethod]
    public void ANumberIsHeldToWhatADiagramAllows()
    {
        var whole = MermaidNumber.Where(number => number == Math.Floor(number), "a whole number");

        Assert.IsNull(whole("3"));
        Assert.AreEqual("a whole number", whole("2.5"));
        StringAssert.Contains(whole("lots"), "not a number");
    }

    [TestMethod]
    public void SwatchesMayBeNumberedFromNought_AndADiagramsThemeVariablesAreASectionOfTheirOwn()
    {
        var config = MermaidConfig.Read("config:\n  themeVariables:\n    cScale0: red\n    cScale12: blue\n    radar:\n      axisColor: green\n");
        var swatches = config.Theme.Swatches("cScale", 12, first: 0);

        Assert.AreEqual("red", swatches[0]);
        Assert.IsFalse(swatches.ContainsKey(12), "twelve of them: nought to eleven");
        Assert.AreEqual("green", config.DiagramTheme("radar").Value("axisColor"));
        Assert.AreSame(MermaidConfig.None, config.DiagramTheme("pie"), "and a diagram whose section is not written has none");
    }

    [TestMethod]
    public void WhatConfigSaysForEveryDiagramIsItsOwn_BesideEachDiagramsSection()
    {
        var config = MermaidConfig.Read("config:\n  fontSize: 18\n  pie:\n    textPosition: 0.5");

        Assert.AreEqual(18, config.Shared.Size("fontSize"));
        Assert.AreEqual("0.5", config.Shared.Section("pie")!.Value("textPosition"));
        Assert.AreSame(MermaidConfig.None, MermaidConfig.Read("title: x").Shared, "and front matter with no config has none");
    }

    [TestMethod]
    public void PropertiesInBracesEndAtTheBrace_AndAQuotedCommaIsPartOfTheValue()
    {
        var line = MermaidLine.Of("@{ assigned: 'Smith, J', priority: High }");
        line.Token("@{");
        line.Space();

        Assert.IsTrue(line.Properties(["assigned", "priority"], ends: '}', what: "Metadata"));
        line.Space();
        Assert.AreEqual('}', line.Next, "the brace is left for the line to close");

        var values = line.Read("line").SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Setting).Select(node => node.Text).ToArray();
        CollectionAssert.AreEqual(new[] { "'Smith, J'", "High" }, values);
    }

    [TestMethod]
    public void APropertyNobodySetsIsSaidAsWhatThePropertiesAre()
    {
        var line = MermaidLine.Of("colour: red");
        line.Properties(["assigned"], what: "Metadata");

        StringAssert.StartsWith(line.Read("line").SelfAndDescendants().Single(node => node.Trouble is not null).Trouble, "Metadata sets");
    }

    [TestMethod]
    public void ALinesIndentIsTheSpaceBeforeWhatItStates()
    {
        var lines = MermaidParser.Parse("kanban\n  Todo\n\t\ta[Card]").SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line).ToList();

        CollectionAssert.AreEqual(new[] { 0, 2, 2 }, lines.Select(line => line.Indent()).ToArray(), "a tab counts as one");
    }
}
