using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Answers kept to be read back rather than worked out again: a build's errors, an edit's diff, a test run's traces. What
/// matters is that what was said can all be had again - in parts, whole, or searched - and that keeping it costs a bounded
/// amount however much is said.
/// </summary>
[TestClass]
[CoversNode("graph-ask-paging")]
public class AnswerHistoryTests
{
    private static AnswerHistory History() => new(_ => null);

    private static string[] Numbered(int count) => [.. Enumerable.Range(1, count).Select(i => $"line {i:D4}")];

    [TestMethod]
    public void AKeptAnswer_ReadsBackPartByPart_ThenSaysItHasAllBeenRead()
    {
        var history = History();
        var number  = history.Keep("nfi build App.slnx", Numbered(3000));

        var parts = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            var (text, ok) = history.Read(number, null, whole: false, budget: 2_000);
            Assert.IsTrue(ok, text);
            if (text.StartsWith("all of", StringComparison.Ordinal)) break;
            Assert.IsTrue(text.Length < 2_200, "a part is no longer than was asked for");
            parts.Add(text);
        }

        var read = string.Join("", parts);
        foreach (var n in new[] { 1, 1500, 3000 })
            Assert.AreEqual(1, read.Split($"line {n:D4}\n").Length - 1, $"line {n} is read once, across the parts");
        StringAssert.Contains(history.Read(number, null, whole: false, budget: 2_000).Text, "has been read");
    }

    [TestMethod]
    public void AKeptAnswer_IsSearched_WithTwoLinesEitherSideOfEachMatch()
    {
        var history = History();
        var lines   = Numbered(100);
        lines[49]   = "a.cs(3,1): error CS0246: the type Widget could not be found";
        var number  = history.Keep("nfi build App.slnx", lines);

        var (text, ok) = history.Read(number, new Regex("CS0246"), whole: false, budget: 20_000);

        Assert.IsTrue(ok, text);
        StringAssert.Contains(text, "1 of its 100 line(s) match");
        StringAssert.Contains(text, "    50  a.cs(3,1): error CS0246");
        StringAssert.Contains(text, "line 0048");
        StringAssert.Contains(text, "line 0052");
        Assert.IsFalse(text.Contains("line 0047", StringComparison.Ordinal), "two lines either side, and no more");
    }

    [TestMethod]
    public void AKeptAnswer_ReadWhole_IsEveryLineOfIt_HoweverLong()
    {
        var history = History();
        var number  = history.Keep("nfi test graph-ask", Numbered(3000));

        var (text, ok) = history.Read(number, null, whole: true, budget: 100);

        Assert.IsTrue(ok);
        Assert.AreEqual(3000, text.Split('\n').Count(l => l.StartsWith("line ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void WithNoneNamed_TheNewestKeptAnswerIsRead()
    {
        var history = History();
        history.Keep("nfi build A.slnx", ["from A"]);
        history.Keep("nfi build B.slnx", ["from B"]);

        StringAssert.Contains(history.Read(null, null, whole: true, budget: 1_000).Text, "from B");
    }

    [TestMethod]
    public void PastFiftyAnswers_TheOldestGo_AndAskingForOneOfThemNamesTheOnesKept()
    {
        var history = History();
        for (var i = 0; i < 55; i++) history.Keep($"nfi build {i}", ["x"]);

        var (text, ok) = history.Read(1, null, whole: true, budget: 1_000);

        Assert.IsFalse(ok);
        StringAssert.Contains(text, "@6 to @55");
        Assert.IsTrue(history.Read(6, null, whole: true, budget: 1_000).Ok);
    }

    [TestMethod]
    public void PastTheCapOnKeptText_TheOldestGo_ButNeverTheNewest()
    {
        var history = History();
        // One line of a thousand characters, listed over and over: the text counted is large, the memory is not.
        var line  = new string('x', 1023);
        var six   = Enumerable.Repeat(line, 6 * 1024).ToArray();
        var first = history.Keep("first", six);
        history.Keep("second", six);
        var third = history.Keep("third", six);

        Assert.IsFalse(history.Read(first, null, whole: true, budget: 0).Ok, "the oldest went to make room");
        Assert.IsTrue(history.Read(third, null, whole: true, budget: 0).Ok);

        var huge = history.Keep("huge", Enumerable.Repeat(line, 20 * 1024).ToArray());
        Assert.IsTrue(history.Read(huge, null, whole: false, budget: 100).Ok, "the newest stays, however long it is");
        Assert.IsFalse(history.Read(third, null, whole: true, budget: 0).Ok);
    }

    [TestMethod]
    public void AnAnswerNeverGiven_IsRefused_SayingNoneHasBeen()
    {
        var history = History();

        var (text, ok) = history.Read(3, null, whole: false, budget: 1_000);

        Assert.IsFalse(ok);
        StringAssert.Contains(text, "none has been given here yet");
    }
}
