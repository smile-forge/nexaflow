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

    [TestMethod]
    public void AnOutlineNestsEachItemUnderTheNearestItemIndentedLess()
    {
        var items = (IReadOnlyList<(int, string)>)[(0, "root"), (2, "a"), (4, "b"), (3, "c"), (2, "d")];
        var nested = MermaidOutline.Nested(items);

        CollectionAssert.AreEqual(new int?[] { null, 0, 1, 1, 0 }, nested.Select(one => one.Parent).ToArray(),
                                  "c is indented between b and a, so it hangs off a, as Mermaid reads it");
    }

    [TestMethod]
    public void AnItemIndentedNoFurtherThanTheRootHangsOffNothing_OrOffTheRootWhereTheOutlineSaysSo()
    {
        var items = (IReadOnlyList<(int, string)>)[(4, "event"), (0, "a"), (2, "sub"), (0, "b")];

        CollectionAssert.AreEqual(new int?[] { null, null, 1, null }, MermaidOutline.Nested(items).Select(one => one.Parent).ToArray(),
                                  "nothing but the root may be written left of it");
        CollectionAssert.AreEqual(new int?[] { null, 0, 1, 0 }, MermaidOutline.Nested(items, floor: true).Select(one => one.Parent).ToArray(),
                                  "with a floor, the first item after the root is where the children start");
    }

    [TestMethod]
    public void AWordAnArrowClosesIsStillThatWord_WhereWhatCarriesAWordOnSaysSo()
    {
        Assert.AreEqual("complex", MermaidLine.Keyword("complex-->clear", Letter, "complex"));
        Assert.IsNull(MermaidLine.Keyword("complex-->clear", "complex"), "a hyphen carries a keyword on, for x-axis");
        Assert.IsNull(MermaidLine.Keyword("complexity", Letter, "complex"));

        var line = MermaidLine.Of("complex-->clear");

        Assert.IsTrue(line.Word("complex", letter: Letter));
        Assert.AreEqual("-->clear", line.Rest);

        static bool Letter(char character) => char.IsLetterOrDigit(character) || character == '_';
    }

    [TestMethod]
    public void AKeySetToAListIsReadInBracketsOrAsTheLinesUnderIt()
    {
        var brackets = MermaidConfig.Read("config:\n  journey:\n    actorColours: [\"#ff0000\", \"#00ff00\"]").Diagram("journey");
        var dashes = MermaidConfig.Read("config:\n  journey:\n    actorColours:\n      - \"#ff0000\"\n      - \"#00ff00\"\n    width: 200").Diagram("journey");

        CollectionAssert.AreEqual(new[] { "#ff0000", "#00ff00" }, brackets.List("actorColours").ToArray());
        CollectionAssert.AreEqual(new[] { "#ff0000", "#00ff00" }, dashes.List("actorColours").ToArray());
        Assert.AreEqual(200, dashes.Size("width"), "and what follows the list is still the diagram's");
        Assert.AreEqual(0, brackets.List("sectionFills").Count, "a key set to no list at all");
    }

    [TestMethod]
    public void OptionsWrittenOneAfterAnotherAreEachAPropertyOfTheirOwn()
    {
        var line = MermaidLine.Of("id: \"Alpha one\" type: HIGHLIGHT tag: \"v1.0\"");

        Assert.IsTrue(line.Properties(["id", "type", "tag"], what: "A commit", spaced: true));
        Assert.IsTrue(line.Done);

        var properties = line.Read(MermaidKinds.Statement).SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Property).ToList();

        Assert.AreEqual(3, properties.Count);
        CollectionAssert.AreEqual(
            new[] { "id", "type", "tag" },
            properties.Select(property => property.Children.First(child => child.Kind == MermaidKinds.Key).Text).ToArray());
        Assert.AreEqual("\"Alpha one\"", properties[0].Children.First(child => child.Kind == MermaidKinds.Setting).Text, "a quoted value holds its space");
    }
}
