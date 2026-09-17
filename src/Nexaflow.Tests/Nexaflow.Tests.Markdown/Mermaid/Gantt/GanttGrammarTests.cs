using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Gantt;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Gantt;

/// <summary>
/// What a <c>gantt</c> block is read into: its settings, sections, tasks with their tags, ids, starts and ends, and clicks — and
/// what is wrong with a schedule that only the whole chart shows.
/// </summary>
[TestClass]
[CoversNode("gantt-ast")]
public class GanttGrammarTests : MermaidGrammarContract
{
    /// <summary>The chart the Mermaid documentation opens with.</summary>
    public const string First =
        """
        gantt
            title A Gantt Diagram
            dateFormat YYYY-MM-DD
            section Section
                A task          :a1, 2014-01-01, 30d
                Another task    :after a1, 20d
            section Another
                Task in Another :2014-01-12, 12d
                another task    :24d
        """;

    /// <summary>The documentation's full syntax example.</summary>
    public const string Syntax =
        """
        gantt
            dateFormat  YYYY-MM-DD
            title       Adding GANTT diagram functionality to mermaid
            excludes    weekends
            %% (`excludes` accepts specific dates in YYYY-MM-DD format, days of the week ("sunday") or "weekends", but not the word "weekdays".)

            section A section
            Completed task            :done,    des1, 2014-01-06,2014-01-08
            Active task               :active,  des2, 2014-01-09, 3d
            Future task               :         des3, after des2, 5d
            Future task2              :         des4, after des3, 5d

            section Critical tasks
            Completed task in the critical line :crit, done, 2014-01-06,24h
            Implement parser and jison          :crit, done, after des1, 2d
            Create tests for parser             :crit, active, 3d
            Future task in critical line        :crit, 5d
            Create tests for renderer           :2d
            Add to mermaid                      :until isadded
            Functionality added                 :milestone, isadded, 2014-01-25, 0d

            section Documentation
            Describe gantt syntax               :active, a1, after des1, 3d
            Add gantt diagram to demo page      :after a1  , 20h
            Add another diagram to demo page    :doc1, after a1  , 48h

            section Last section
            Describe gantt syntax               :after doc1, 3d
            Add gantt diagram to demo page      :20h
            Add another diagram to demo page    :48h
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Gantt;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        First,
        Syntax,
        "gantt\n    apple :a, 2017-07-20, 1w\n    banana :crit, b, 2017-07-23, 1d\n    cherry :active, c, after b a, 1d\n    kiwi   :d, 2017-07-20, until b c",
        "gantt\n    dateFormat DD-MM-YYYY\n    excludes weekends\n    %% week 7 is winter break\n    excludes 10-02-2025 11-02-2025 12-02-2025 13-02-2025 14-02-2025\n    %% workers holiday 1 maj\n    excludes 01-05-2025\n    section Work\n    Build :b1, 03-02-2025, 20d",
        "gantt\n    title A Gantt Diagram Excluding Fri - Sat weekends\n    dateFormat YYYY-MM-DD\n    excludes weekends\n    weekend friday\n    section Section\n        A task          :a1, 2024-01-01, 30d\n        Another task    :after a1, 20d",
        "gantt\n    dateFormat HH:mm\n    axisFormat %H:%M\n    Initial milestone : milestone, m1, 17:49, 2m\n    Task A : 10m\n    Task B : 5m\n    Final milestone : milestone, m2, 18:08, 4m",
        "gantt\n    dateFormat HH:mm\n    axisFormat %H:%M\n    Initial vert : vert, v1, 17:30, 2m\n    Task A : 3m\n    Task B : 8m\n    Final vert : vert, v2, 17:58, 4m",
        "gantt\n  dateFormat YYYY-MM-DD\n  tickInterval 1week\n  weekday monday\n  Task : 2014-01-01, 30d",
        "---\ndisplayMode: compact\n---\ngantt\n    title A Gantt Diagram\n    dateFormat  YYYY-MM-DD\n\n    section Section\n    A task           :a1, 2014-01-01, 30d\n    Another task     :a2, 2014-01-20, 25d\n    Another one      :a3, 2014-02-10, 20d",
        "gantt\n    title Git Issues - days since last update\n    dateFormat X\n    axisFormat %s\n    section Issue19062\n    71   : 0, 71\n    section Issue19401\n    36   : 0, 36",
        "gantt\n  dateFormat  YYYY-MM-DD\n\n  section Clickable\n  Visit mermaidjs         :active, cl1, 2014-01-07, 3d\n  Print arguments         :cl2, after cl1, 3d\n  Print task              :cl3, after cl2, 3d\n\n  click cl1 href \"https://mermaidjs.github.io/\"\n  click cl2 call printArguments(\"test1\", \"test2\", test3)\n  click cl3 call printTask()",
        "gantt\n  todayMarker stroke-width:5px,stroke:#0f0,opacity:0.5\n  inclusiveEndDates\n  topAxis\n  includes 2014-01-04\n  Task : 2014-01-01, 2014-01-05",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("a comment on a task's line", "gantt\n  Task :a1, 2014-01-01, 3d %% three days"),
        ("accessibility lines", "gantt\n  accTitle: Plan\n  accDescr: The plan\n  Task : 2014-01-01, 3d"),
        ("written on Windows", "gantt\r\n  section One  \r\n  Task : 2014-01-01, 3d\r\n"),
        ("a link and a call in either order", "gantt\n  Task :a1, 2014-01-01, 3d\n  click a1 call show(a1) href \"https://x.y\""),
        // Half written.
        ("nothing but the keyword", "gantt"),
        ("a setting word alone", "gantt\n  dateFormat "),
        ("a section word alone", "gantt\n  section "),
        ("a task with nothing after its colon", "gantt\n  Task : 2014-01-01, 3d\n  Next :"),
        ("a task with no name yet", "gantt\n  : 2014-01-01, 3d"),
        ("after with no id yet", "gantt\n  Task :a1, 2014-01-01, 3d\n  Next :after , 2d"),
        ("a trailing comma", "gantt\n  Task :a1, 2014-01-01,"),
        ("a click with nothing to do", "gantt\n  Task :a1, 2014-01-01, 3d\n  click a1 href "),
        // What nobody means to write.
        ("a line with no colon", "gantt\n  Task"),
        ("a date in another format", "gantt\n  dateFormat DD-MM-YYYY\n  Task : 2014-01-01T00, 3dX"),
        ("an id no task has", "gantt\n  Task :after nobody, 3d"),
        ("too much in a schedule", "gantt\n  Task :a1, 2014-01-01, 3d, 4d"),
        ("a first task with no start", "gantt\n  Task : 3d"),
        ("a wrong interval, weekday and weekend", "gantt\n  tickInterval 1decade\n  weekday funday\n  weekend sunday\n  excludes weekdays"),
        ("a tag alone", "gantt\n  Task :milestone"),
    ];

    [TestMethod]
    public void TheDocumentedChartsLinesAreEachRead()
    {
        var tree = MermaidParser.Read(Syntax);

        Assert.AreEqual(2, Nodes(tree, GanttKinds.Setting).Count);
        Assert.AreEqual(4, Nodes(tree, GanttKinds.Section).Count);
        Assert.AreEqual(17, Nodes(tree, GanttKinds.Task).Count);
        Assert.AreEqual(11, Nodes(tree, GanttKinds.Tag).Count);
        Assert.AreEqual(1, Nodes(tree, GanttKinds.Until).Count);
    }

    [TestMethod]
    public void WhatIsStillBeingWrittenIsNoComplaint()
    {
        foreach (var source in new[] { "gantt\n  dateFormat ", "gantt\n  section ", "gantt\n  Task : 2014-01-01, 3d\n  Next :", "gantt\n  : 2014-01-01, 3d", "gantt\n  Task :a1, 2014-01-01, 3d\n  Next :after , 2d" })
            Assert.AreEqual(0, Trouble(source).Count, $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, reason) in new[]
                 {
                     ("gantt\n  dateFormat DD-MM-YYYY\n  Task : someday, 3d", "no date written as DD-MM-YYYY"),
                     ("gantt\n  Task : 2014-01-01, 3dX", "nor a length of time"),
                     ("gantt\n  Task :after nobody, 3d", "No task has the id nobody"),
                     ("gantt\n  Task :a1, 2014-01-01, 3d, 4d", "at most"),
                     ("gantt\n  Task : 3d", "first task"),
                     ("gantt\n  tickInterval 1decade", "whole number and a unit"),
                     ("gantt\n  weekend sunday", "friday or saturday"),
                     ("gantt\n  excludes weekdays", "'weekdays' is no date"),
                     ("gantt\n  Task :milestone", "says when it ends"),
                     ("gantt\n  Task", "A task is its name"),
                 })
            Assert.IsTrue(Trouble(source).Any(said => said.Contains(reason, StringComparison.Ordinal)), $"{source}: {string.Join(" | ", Trouble(source))}");
    }

    [TestMethod]
    public void ANewLineUnderATaskOrASectionIsATask_AndUnderASettingNothing()
    {
        var grammar = new GanttGrammar();
        var tree = MermaidParser.Parse("gantt\n  dateFormat YYYY\n  section A\n  Task : 2014, 3d");

        Assert.AreEqual((": 1d", 0), grammar.Blank(Nodes(tree, GanttKinds.Task).Single()));
        Assert.AreEqual((": 1d", 0), grammar.Blank(Nodes(tree, GanttKinds.Section).Single()));
        Assert.IsNull(grammar.Blank(Nodes(tree, GanttKinds.Setting).Single()));
    }

    [TestMethod]
    public void AnIdRenamedWhereItIsDeclaredIsRenamedWhereverAfterUntilOrClickNameIt()
    {
        var names = new GanttGrammar().Names(ContentReading.Of(MermaidParser.Read(Syntax)).Root);

        Assert.AreEqual(3, names.Single(name => name.Name == "des1").Uses.Count + names.Single(name => name.Name == "isadded").Uses.Count);
        Assert.AreEqual("two_words", new GanttGrammar().Naming("two words"));
    }

    [TestMethod]
    public void AColonTypedIntoATasksNameIsNotWritten()
    {
        const string source = "gantt\n  Design : 2014-01-01, 3d";
        var name = ContentReading.Of(MermaidParser.Read(source)).Root.SelfAndDescendants().First(part => part.Kind == MermaidKinds.Words && part.Text == "Design");

        Assert.AreEqual(string.Empty, new GanttGrammar().Escaping(name, name.End, ":")!.Value.Text);
    }

    private static List<ContentNode> Nodes(ContentNode tree, string kind) => [.. tree.SelfAndDescendants().Where(node => node.Kind == kind)];

    private static List<string> Trouble(string source) =>
        [.. MermaidParser.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>()];
}
