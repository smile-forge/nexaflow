using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Deep links into content (<see cref="ContentPath"/>), followed and made by the engine showing it, which holds its outline: a heading
/// by the name it answers to and what is under it in its section; a heading by the titles of the headings holding it; and a heading a
/// list of them handed on, found wherever it now stands.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-text")]
public class ContentDeepLinkTests
{
    private const string Doc =
        "# Getting Started\n\nSome words about chrome.\n\n"
        + "```mermaid\npie\n  \"chrome\" : 40\n  \"firefox\" : 12\n```\n\n"
        + "## Notes\n\n- one\n- two\n- three\n";

    [TestMethod]
    public void AReferenceSaysWhatSomethingIsRatherThanWhereItHappensToSit() => UiThread.Run(() =>
    {
        var engine = Laid(Doc);

        StringAssert.StartsWith(engine.PathTo(Doc.IndexOf("three")).ToString(), "heading:notes/list/item#2",
                                "the third item of the list under the heading called Notes");
    });

    [TestMethod]
    public void AndFollowingItLandsBackOnTheSameThing() => UiThread.Run(() =>
    {
        var engine = Laid(Doc);
        var path = engine.PathTo(Doc.IndexOf("three"));

        StringAssert.Contains(Said(Doc, engine.Located(path)), "three");
    });

    [TestMethod]
    public void AndItStillLandsThereAfterTheDocumentHasBeenWrittenInAbove() => UiThread.Run(() =>
    {
        // The whole point of not saving a line number: everything has moved and the third item of the list is still the third item.
        var path = Laid(Doc).PathTo(Doc.IndexOf("three"));
        var edited = "Another paragraph entirely.\n\n## Notes elsewhere\n\n- not this\n\n" + Doc;

        StringAssert.Contains(Said(edited, Laid(edited).Located(path)), "three");
    });

    [TestMethod]
    public void AHeadingIsReachedByTheNameItAnswersTo_WhateverItsCase() => UiThread.Run(() =>
    {
        var engine = Laid(Doc);

        Assert.AreEqual("## Notes", Said(Doc, engine.Located(ContentPath.Read("heading:Notes"))));
    });

    [TestMethod]
    public void UnderAHeading_AHeadingIsNamedAsItIsCalledThere() => UiThread.Run(() =>
    {
        // The second Setup answers to setup-1 across the document; under B it is simply what is called setup.
        const string Source = "# A\n\n## Setup\n\none\n\n# B\n\n## Setup\n\ntwo\n";
        var engine = Laid(Source);

        Assert.AreEqual(Source.LastIndexOf("## Setup"), engine.Located(ContentPath.Read("heading:b/heading:setup"))?.Start);
        Assert.AreEqual(Source.LastIndexOf("## Setup"), engine.Located(ContentPath.Read("heading:setup-1"))?.Start);
    });

    [TestMethod]
    public void AReferenceThatNoLongerLeadsAllTheWay_LandsAsFarAsItDoes() => UiThread.Run(() =>
    {
        // A deep link into a section somebody has reorganised should still land in the section, which is the difference between a
        // reference that ages and one that breaks.
        var engine = Laid(Doc);

        StringAssert.StartsWith(Said(Doc, engine.Located(ContentPath.Read("heading:notes/list/item#40"))), "- one", "as far as it got, which is the list");
        Assert.IsNull(engine.Located(ContentPath.Read("nothing-is-called-this")), "and nothing at all where it never started");
    });

    [TestMethod]
    public void ItReachesInsideATableAndToADiagram() => UiThread.Run(() =>
    {
        const string Table = "| a | b |\n|---|---|\n| one | two |\n";
        StringAssert.Contains(Said(Table, Laid(Table).Located(ContentPath.Read("table/row#1/cell#1"))), "two");

        var engine = Laid(Doc);
        var fence = engine.PathTo(Doc.IndexOf("```mermaid"));

        StringAssert.StartsWith(Said(Doc, engine.Located(ContentPath.Read(fence.ToString()))), "```mermaid", "the fence a diagram is written in");
    });

    [TestMethod]
    public void AHeadingAListHandedOn_IsFoundWhereverItNowStands() => UiThread.Run(() =>
    {
        var engine = Laid(Doc);
        var notes = engine.Headings.Single(heading => heading.Title == "Notes");

        CollectionAssert.AreEqual(new[] { (1, "Getting Started"), (2, "Notes") }, engine.Headings.Select(heading => (heading.Rank, heading.Title)).ToArray());
        Assert.AreEqual(Doc.IndexOf("## Notes"), engine.Placed(notes)?.Start);

        var edited = "A paragraph written above.\n\n" + Doc;
        engine.Lay(null, EditState.For(edited), WpfStyles.Dark, 480, readOnly: true);

        Assert.AreEqual(edited.IndexOf("## Notes"), engine.Placed(notes)?.Start, "the heading the list holds, where it now stands");
    });

    [TestMethod]
    [CoversNode("markdown-outline")]
    public void TheOutlineSaysItChanged_OnlyWhenAHeadingDid() => UiThread.Run(() =>
    {
        var engine = new ContentEngine();
        var said = 0;
        engine.OutlineChanged += (_, _) => said++;

        Lay(engine, "# One\n\nwords\n\n## Two\n");
        Assert.AreEqual(1, said, "the first laying out has headings to list");

        Lay(engine, "# One\n\nwords typed\n\n## Two\n");
        Assert.AreEqual(1, said, "words typed under a heading change no heading, though the one after them moved");

        Lay(engine, "# One\n\nwords typed\n\n## Two too\n");
        Assert.AreEqual(2, said, "a heading saying something else does");

        Lay(engine, "# One\n\nwords typed\n\n### Two too\n");
        Assert.AreEqual(3, said, "and so does one ranked otherwise");
    });

    private static ContentEngine Laid(string source) => Laying.LaidBy(null, source, 480).Engine;

    private static void Lay(ContentEngine engine, string source) => engine.Lay(null, EditState.For(source), WpfStyles.Dark, 480, readOnly: true);

    private static string Said(string source, (int Start, int Length)? place) =>
        place is { } at ? source.Substring(at.Start, at.Length).TrimEnd('\r', '\n') : "nowhere";
}
