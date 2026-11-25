using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// What a command prints when it has said a lot: what it concluded, in full, and its detail folded into a line naming where
/// all of it is kept. The point is that the caller never has to cut the output down itself - a cut throws away what the
/// next call then asks for - and never has to run the command again to see what was folded.
/// </summary>
[TestClass]
[CoversNode("graph-ask-paging")]
public class AnswerWriterTests
{
    private static AnswerWriter LongEdit()
    {
        var answer = new AnswerWriter();
        answer.WriteLine("note: matched ignoring indentation");
        for (var i = 0; i < 400; i++) answer.Detail.WriteLine($"+ added line {i:D3} of the new file");
        answer.WriteLine("compile: no new errors in 1 file(s)");
        answer.Detail.WriteLine("--- a short one");
        answer.Detail.WriteLine("+ two lines");
        answer.WriteLine("1 edit to 1 file(s).");
        return answer;
    }

    [TestMethod]
    public void AShortAnswer_PrintsWhole_DetailAndAll_AndKeepsNothing()
    {
        var answer = new AnswerWriter();
        answer.WriteLine("1 edit to 1 file(s).");
        answer.Detail.WriteLine("+ the line added");
        var asked = 0;

        var said = answer.Render(_ => { asked++; return 7; });

        Assert.AreEqual(0, asked, "nothing is folded, so nothing needs keeping");
        StringAssert.Contains(said, "+ the line added");
    }

    [TestMethod]
    public void ALongAnswer_FoldsEachLongRunOfDetail_IntoOneLineNamingWhereAllOfItIsKept()
    {
        IReadOnlyList<string>? kept = null;

        var said = LongEdit().Render(lines => { kept = lines; return 7; });

        Assert.AreEqual(405, kept!.Count, "every line is kept, what it concluded and its detail alike");
        StringAssert.Contains(said, "… 400 line(s) kept: nfi more @7 reads them");
        Assert.IsFalse(said.Contains("added line 200", StringComparison.Ordinal), said);
        StringAssert.Contains(said, "compile: no new errors", "what the command concluded prints in full");
        StringAssert.Contains(said, "1 edit to 1 file(s).");
        StringAssert.Contains(said, "+ two lines", "a run of detail too short to be worth a line naming it is printed");
        Assert.IsTrue(said.Length < 400, said);
    }

    [TestMethod]
    public void ALongAnswerWithNowhereToKeepIt_PrintsWhole()
    {
        var said = LongEdit().Render(_ => null);

        StringAssert.Contains(said, "added line 200", "detail nobody could get back would be detail lost");
    }

    [TestMethod]
    public void ALineWrittenInPieces_IsOneLine_AndStaysAConclusionWhenAPieceOfItWasDetail()
    {
        var answer = new AnswerWriter();
        answer.Write("build: ");
        answer.Detail.Write("App");
        answer.WriteLine(" - ok");
        for (var i = 0; i < 400; i++) answer.Detail.WriteLine($"  warning {i:D3}: a long enough warning to fold away");

        var said = answer.Render(_ => 3);

        StringAssert.StartsWith(said, "build: App - ok");
        StringAssert.Contains(said, "… 400 line(s) kept: nfi more @3");
    }

    [TestMethod]
    public void ALiveAnswer_PrintsWhatItConcludesAsItGoes_AndOnlyNamesItsDetail()
    {
        var live   = new StringWriter();
        var answer = new AnswerWriter(live);

        answer.WriteLine("run:   Suite - 1 passed, 1 failed");
        answer.Detail.WriteLine("    at Widget.Go()");
        Assert.AreEqual("run:   Suite - 1 passed, 1 failed" + Environment.NewLine, live.ToString(), "out before the run ends");

        var end = answer.FinishLive(lines => lines.Count == 2 ? 9 : null);

        StringAssert.Contains(end, "1 line(s) kept: nfi more @9");
        Assert.IsFalse(live.ToString().Contains("Widget.Go", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ALiveAnswerWithNowhereToKeepIt_EndsWithItsDetail()
    {
        var answer = new AnswerWriter(new StringWriter());
        answer.Detail.WriteLine("    at Widget.Go()");

        StringAssert.Contains(answer.FinishLive(_ => null), "at Widget.Go()");
    }

    [TestMethod]
    public void ALongEdit_FoldsItsDiff_AndMoreReadsItBack()
    {
        var root = TestTemp.CreateSubdirectory("nexa-kept-").FullName;
        try
        {
            new ProductStore(root).Initialize("P");
            var script = Path.Combine(root, "plan.edits");
            var body   = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line {i:D4} of a long new file"));
            File.WriteAllText(script, $"create big.md\n<<<\n{body}\n>>>\n");

            string[] args   = ["graph", "edit", "script", "--file", script, root, "--no-check"];
            var      answer = new AnswerWriter();
            using var scope = RequestScope.Begin(answer, new StringWriter(), new RequestContext(root));

            Assert.AreEqual(0, Program.Execute(args));
            var said   = answer.Render(lines => Program.KeepAnswer(root, args, lines));
            var folded = Regex.Match(said, @"… 501 line\(s\) kept: nfi more @(\d+)");
            Assert.IsTrue(folded.Success, said);
            Assert.IsFalse(said.Contains("line 0250", StringComparison.Ordinal), said);

            var number = int.Parse(folded.Groups[1].Value);
            Assert.AreEqual(0, Program.Execute(["more", $"@{number}", "--grep", "line 0250"]), "more reads it back by its handle");
            var (found, ok) = Program.HistoryFor(root, main: false)!.Read(number, new Regex("line 0250"), whole: false, budget: 20_000);
            Assert.IsTrue(ok, found);
            StringAssert.Contains(found, "+ line 0250 of a long new file");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}
