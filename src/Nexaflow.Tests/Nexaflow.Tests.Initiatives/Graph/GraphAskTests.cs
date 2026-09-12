using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Asking one question in several steps. What matters here is not that each stage works on its own but that
/// they compose — the reason this exists at all is that "find it and show me it" was two calls, and two
/// calls is what makes a repo-wide search lose to a blanket grep.
/// </summary>
[TestClass]
[CoversNode("graph-queries")]
public class GraphAskTests
{
    private const string ReaderCs = """
        namespace App;

        public class Reader
        {
            public int Read() => Depth;

            public int Depth => 1;
        }
        """;

    private const string CallerCs = """
        namespace App;

        public class Caller
        {
            public int Go() => new Reader().Read();
        }
        """;

    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal)
    {
        ["src/Reader.cs"] = ReaderCs,
        ["src/Caller.cs"] = CallerCs,
    };

    private static string[]? Read(string rel) =>
        Files.TryGetValue(rel, out var text) ? text.Replace("\r\n", "\n").Split('\n') : null;

    private static GraphNode Code(string file, string ast, int line, string type = NodeType.Member) => new()
    {
        Id = $"code:{file}#{ast}",
        Type = type,
        Label = ast[(ast.LastIndexOf(':') + 1)..],
        FilePath = file,
        Language = "c-sharp",
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["line"] = line.ToString(),
            ["ast"] = ast,
        },
    };

    private static GraphNode FileNode(string file) =>
        new() { Id = $"file:{file}", Type = NodeType.File, Label = file, FilePath = file };

    private static GraphEdge E(string from, string to, string rel) =>
        new() { Source = from, Target = to, Relationship = rel };

    private static KnowledgeGraph Repo() => new()
    {
        Nodes =
        [
            FileNode("src/Reader.cs"),
            Code("src/Reader.cs", "T:Reader", 3, NodeType.Type),
            Code("src/Reader.cs", "T:Reader/M:Read", 5),
            Code("src/Reader.cs", "T:Reader/P:Depth", 7),
            FileNode("src/Caller.cs"),
            Code("src/Caller.cs", "T:Caller", 3, NodeType.Type),
            Code("src/Caller.cs", "T:Caller/M:Go", 5),
        ],
        Edges =
        [
            E("file:src/Reader.cs", "code:src/Reader.cs#T:Reader", EdgeRelationship.Contains),
            E("code:src/Reader.cs#T:Reader", "code:src/Reader.cs#T:Reader/M:Read", EdgeRelationship.Contains),
            E("code:src/Reader.cs#T:Reader", "code:src/Reader.cs#T:Reader/P:Depth", EdgeRelationship.Contains),
            E("file:src/Caller.cs", "code:src/Caller.cs#T:Caller", EdgeRelationship.Contains),
            E("code:src/Caller.cs#T:Caller", "code:src/Caller.cs#T:Caller/M:Go", EdgeRelationship.Contains),
            E("code:src/Caller.cs#T:Caller/M:Go", "code:src/Reader.cs#T:Reader/M:Read", EdgeRelationship.Calls),
        ],
    };

    private static GraphAsk.Answer Ask(string question) => GraphAsk.Run(Repo(), question, Read);

    // ── Composing ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void FindingSomethingAndReadingItIsOneQuestion()
    {
        var answer = Ask("search Depth | source");

        Assert.IsTrue(answer.Ok, answer.Text);
        StringAssert.Contains(answer.Text, "public int Depth => 1;",
                              "the point of the pipeline is that the block arrives with the search that found it");
    }

    [TestMethod]
    public void WhoCallsThis_AndContainmentIsNotAnAnswer()
    {
        var answer = Ask("node code:src/Reader.cs#T:Reader/M:Read | callers | ids");

        StringAssert.Contains(answer.Text, "code:src/Caller.cs#T:Caller/M:Go");
        StringAssert.Contains(answer.Text, "1 node(s)",
                              "the type declaring a member is not one of its callers - left in, "
                            + "containment buries the calls the question was about");
    }

    [TestMethod]
    public void MembersWalksDownIntoWhatSomethingHolds()
    {
        var answer = Ask("node code:src/Reader.cs#T:Reader | members | ids");

        StringAssert.Contains(answer.Text, "T:Reader/M:Read");
        StringAssert.Contains(answer.Text, "T:Reader/P:Depth");
        StringAssert.Contains(answer.Text, "2 node(s)");
    }

    [TestMethod]
    public void AGrepAfterSomethingSearchesThatAndNothingElse()
    {
        // Scoping a search is what --from/--hops/--scope are for on the grep verb, and choosing between them
        // is a decision made before you know what you are looking at. A stage that narrows what the NEXT
        // stage sees needs no such choice: whatever the question has found so far is the scope.
        var everywhere = Ask("grep public | files");
        StringAssert.Contains(everywhere.Text, "src/Reader.cs");
        StringAssert.Contains(everywhere.Text, "src/Caller.cs");

        var narrowed = Ask("search Caller | grep public | files");
        StringAssert.Contains(narrowed.Text, "src/Caller.cs");
        Assert.IsFalse(narrowed.Text.Contains("src/Reader.cs"), "the stage before it is the scope");
    }

    [TestMethod]
    public void LikeAndLimitNarrowWhatIsAlreadyFound()
    {
        StringAssert.Contains(Ask("search Reader | like P: | ids").Text, "T:Reader/P:Depth");
        Assert.IsFalse(Ask("search Reader | like P: | ids").Text.Contains("T:Reader/M:Read"));
        StringAssert.Contains(Ask("search Reader | limit 1 | count").Text, "1 node(s)");
    }

    [TestMethod]
    public void SeveralQuestionsComeBackAsOneAnswer()
    {
        var answer = Ask("search Depth | ids\nsearch Go | ids");

        Assert.IsTrue(answer.Ok, answer.Text);
        StringAssert.Contains(answer.Text, "T:Reader/P:Depth");
        StringAssert.Contains(answer.Text, "T:Caller/M:Go");
    }

    // ── Printing ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void CountIsTheWholeAnswerWhenTheQuestionIsWhetherAnythingStillDoesThis()
    {
        var answer = Ask("grep Depth | count");

        Assert.AreEqual(1, answer.Text.Split('\n').Length, "a count that costs a screenful is not a count");
        StringAssert.Contains(answer.Text, "matching line(s)");
    }

    [TestMethod]
    public void AFilesSourceIsWhatItHolds_NotTheFile()
    {
        // Reading a whole file to find one declaration is the habit this verb exists to replace, so the one
        // node that stands for a whole file answers with its outline instead.
        var answer = Ask("node file:src/Reader.cs | source");

        StringAssert.Contains(answer.Text, "code:src/Reader.cs#T:Reader");
        StringAssert.Contains(answer.Text, "M:Read");
        Assert.IsFalse(answer.Text.Contains("namespace App;"), "the outline, not the text");
    }

    // ── Refusing ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void AQuotedAlternationIsOnePatternAndNotTwoStages()
    {
        var answer = Ask("""grep "Read|Depth" | count""");

        Assert.IsTrue(answer.Ok, answer.Text);
        StringAssert.Contains(answer.Text, "node(s)");
    }

    [TestMethod]
    public void AnUnquotedAlternationIsRefused_RatherThanHalfSearched()
    {
        // The stray half reads as a stage of its own, which is the only place it can be caught - so the
        // refusal for a word that is not a stage also names the reason it is most often not one.
        var answer = Ask("grep Read|Depth | count");

        Assert.IsFalse(answer.Ok);
        StringAssert.Contains(answer.Text, "'depth' is not a stage");
        StringAssert.Contains(answer.Text, "quot");
    }

    [TestMethod]
    public void AStageThatIsNotOneNamesTheWholeVocabulary()
    {
        var answer = Ask("search Reader | wibble");

        Assert.IsFalse(answer.Ok);
        StringAssert.Contains(answer.Text, "'wibble' is not a stage");
        StringAssert.Contains(answer.Text, "start:");
    }

    [TestMethod]
    public void APrintingStageIsTheLastThingAQuestionSays()
    {
        var answer = Ask("search Reader | count | callers");

        Assert.IsFalse(answer.Ok);
        StringAssert.Contains(answer.Text, "nothing can follow it");
    }

    [TestMethod]
    public void AQuestionThatStartsFromNothingSaysWhatItCouldStartFrom()
    {
        var answer = Ask("callers | ids");

        Assert.IsFalse(answer.Ok);
        StringAssert.Contains(answer.Text, "search, grep or node first");
    }

    [TestMethod]
    public void AnIdTheGraphDoesNotHaveOffersTheSearchThatWouldFindIt()
    {
        var answer = Ask("node code:src/Nope.cs#T:Nope | ids");

        Assert.IsFalse(answer.Ok);
        StringAssert.Contains(answer.Text, "search code:src/Nope.cs#T:Nope");
    }
}
