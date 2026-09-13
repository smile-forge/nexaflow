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
        ["docs/notes.md"] = "# Notes\n\nNXUI001 is what a button without an id is reported as.\n",
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

    /// <summary>The same repo with a feature that owns the reader through a snaplink, and a document.</summary>
    private static KnowledgeGraph WithFeature()
    {
        var g = Repo();
        g.Nodes = [.. g.Nodes,
                   new GraphNode { Id = "product:reading", Type = NodeType.Product, Label = "Reading feature" },
                   FileNode("docs/notes.md")];
        g.Edges = [.. g.Edges, E("product:reading", "code:src/Reader.cs#T:Reader/M:Read", EdgeRelationship.Tests)];
        return g;
    }

    private static GraphAsk.Answer AskFeature(string question) => GraphAsk.Run(WithFeature(), question, Read);

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

    /// <summary>Carrying on with the ids that did resolve answered a different question from the one asked,
    /// and nothing in the answer said so.</summary>
    [TestMethod]
    public void NodeWithUnresolvedId_IsRefused_EvenBesideOnesThatResolve()
    {
        var answer = Ask("node code:src/Reader.cs#T:Reader,code:src/Nope.cs#T:Nope | ids");

        Assert.IsFalse(answer.Ok);
        StringAssert.Contains(answer.Text, "no node 'code:src/Nope.cs#T:Nope'");
    }

    // ── Scope: what a feature owns, and what is near ─────────────────────────

    /// <summary>A feature has no source of its own; what it owns does. This is the stage for "grep this
    /// feature", which as a single verb was --scope owned.</summary>
    [TestMethod]
    public void Owned_ExpandsAProductToItsFiles()
    {
        var answer = AskFeature("node product:reading | owned | grep Read | files");

        Assert.IsTrue(answer.Ok, answer.Text);
        StringAssert.Contains(answer.Text, "src/Reader.cs");
        Assert.IsFalse(answer.Text.Contains("src/Caller.cs"),
                       "Caller.cs calls Read too, but the feature does not own it: " + answer.Text);
    }

    /// <summary>
    /// The flags a caller already knows from `graph grep`, inside a question. They used to be read as part of the
    /// pattern — `grep Read --scope owned` searched for that whole phrase, found nothing, and said so.
    /// </summary>
    [DataTestMethod]
    [DataRow("grep Read --from product:reading --scope owned | files")]
    [DataRow("node product:reading | grep Read --scope owned | files")]
    public void GrepScopeOwnedFlag_MatchesOwnedStage(string question)
    {
        var staged  = AskFeature("node product:reading | owned | grep Read | files");
        var flagged = AskFeature(question);

        Assert.IsTrue(flagged.Ok, flagged.Text);
        Assert.AreEqual(staged.Text, flagged.Text, "a flag and the stage it spells out are one answer");
    }

    [TestMethod]
    public void Near_WalksHops()
    {
        var answer = Ask("node code:src/Caller.cs#T:Caller/M:Go | near 1 | ids");

        Assert.IsTrue(answer.Ok, answer.Text);
        StringAssert.Contains(answer.Text, "code:src/Reader.cs#T:Reader/M:Read", "one call away");
        StringAssert.Contains(answer.Text, "code:src/Caller.cs#T:Caller", "one containment away");
    }

    [TestMethod]
    public void GrepFromFlag_SeedsLikeNode()
    {
        var flagged = Ask("grep Read --from code:src/Caller.cs#T:Caller/M:Go --hops 1 | files");
        var staged  = Ask("node code:src/Caller.cs#T:Caller/M:Go | near 1 | grep Read | files");

        Assert.IsTrue(flagged.Ok, flagged.Text);
        Assert.AreEqual(staged.Text, flagged.Text);
    }

    [TestMethod]
    public void UnknownFlagInStage_IsRefusedNamingOptions()
    {
        var grep = Ask("grep Read --mode content | count");
        Assert.IsFalse(grep.Ok, "a flag grep does not take must not become part of its pattern");
        StringAssert.Contains(grep.Text, "--scope owned|hops");

        var search = Ask("search Reader --type type | ids");
        Assert.IsFalse(search.Ok);
        StringAssert.Contains(search.Text, "takes no flags");

        var quoted = Ask("grep \"--scope\" | count");
        Assert.IsTrue(quoted.Ok, "a quoted word is a pattern, whatever it looks like: " + quoted.Text);
    }

    [TestMethod]
    public void GrepScopeFlags_KeepTheVerbsRules()
    {
        StringAssert.Contains(AskFeature("grep Read --from product:reading --scope owned --hops 1 | count").Text,
                              "ignores radius");
        StringAssert.Contains(Ask("grep Read --scope owned | count").Text, "relative to a node");
        StringAssert.Contains(Ask("node code:src/Reader.cs#T:Reader | grep Read --from code:src/Reader.cs#T:Reader").Text,
                              "cannot follow a |");
    }

    /// <summary>Grepping a feature node searched something with no source and found nothing — which reads as
    /// "this feature never mentions it".</summary>
    [TestMethod]
    public void GrepOverProductNode_PointsAtOwned()
    {
        var answer = AskFeature("node product:reading | grep Read | files");

        Assert.IsFalse(answer.Ok, "an answer of nothing here would be wrong, not empty");
        StringAssert.Contains(answer.Text, "| owned |");
    }

    // ── Searching for what is not a name ─────────────────────────────────────

    /// <summary>A diagnostic id, a message, a setting key: nothing is ever labelled with one, so a name search
    /// said "no nodes" and sent the caller to grep.</summary>
    [TestMethod]
    public void SearchWithNoNameMatch_FallsBackToSourceWithNote()
    {
        var answer = AskFeature("search NXUI001 | files");

        Assert.IsTrue(answer.Ok, answer.Text);
        StringAssert.Contains(answer.Text, "docs/notes.md");
        StringAssert.Contains(answer.Text, "nothing is named 'NXUI001'");
    }

    [TestMethod]
    public void AName_StillWinsOverTheSource()
    {
        var answer = Ask("search Depth | ids");

        Assert.IsFalse(answer.Text.Contains("nothing is named"), "a name was found, so no fallback: " + answer.Text);
    }

    /// <summary>A line inside a member is also inside its type and its file; counting it once per holder made
    /// every total two or three times the truth.</summary>
    [TestMethod]
    public void Grep_FindsMarkdown_AndCountsALineOnce()
    {
        StringAssert.Contains(AskFeature("grep NXUI001 | files").Text, "docs/notes.md");

        var depth = Ask("grep \"public int Depth\" | ids");
        StringAssert.Contains(depth.Text, "1 matching line(s)");
        StringAssert.Contains(depth.Text, "T:Reader/P:Depth", "credited to the innermost node that holds it");
    }

    [TestMethod]
    public void AnEmptyAnswer_SaysWhichStageLeftNothing()
    {
        var answer = Ask("search Reader | like Zzz | count");

        StringAssert.Contains(answer.Text, "`like Zzz` left nothing");
    }

    // ── Continuing an earlier answer ──────────────────────────────────────────

    /// <summary>Narrowing an answer used to mean asking the whole question again with a stage added.</summary>
    [TestMethod]
    [CoversNode("graph-ask")]
    public void AnEarlierAnswer_StartsANewQuestion_ByItsNumber()
    {
        var history = new AnswerHistory(_ => DateTime.UnixEpoch);

        var first = GraphAsk.Run(Repo(), "search Reader | members", Read, history);
        StringAssert.EndsWith(first.Text, "@1");

        var narrowed = GraphAsk.Run(Repo(), "@1 | like Depth | ids", Read, history);
        Assert.IsTrue(narrowed.Ok, narrowed.Text);
        StringAssert.Contains(narrowed.Text, "T:Reader/P:Depth");
        Assert.IsFalse(narrowed.Text.Contains("M:Read", StringComparison.Ordinal), narrowed.Text);
        StringAssert.EndsWith(narrowed.Text, "@2");

        var last = GraphAsk.Run(Repo(), "@ | count", Read, history);
        StringAssert.StartsWith(last.Text, "1 node(s)", "@ is the last answer - the narrowed one");
    }

    /// <summary>An answer continued after a file it came from changed is asked again rather than reused.</summary>
    [TestMethod]
    [CoversNode("graph-ask")]
    public void AnAnswerWhoseFilesHaveChanged_IsAskedAgain()
    {
        var written = DateTime.UnixEpoch;
        var history = new AnswerHistory(_ => written);

        GraphAsk.Run(Repo(), "search Reader | members", Read, history);
        written = written.AddMinutes(1);

        var continued = GraphAsk.Run(Repo(), "@1 | count", Read, history);
        StringAssert.Contains(continued.Text, "asked again");
    }

    [TestMethod]
    [CoversNode("graph-ask")]
    public void AnAnswerThatWasNeverGiven_IsRefused_NamingTheOnesThatWere()
    {
        var history = new AnswerHistory(_ => DateTime.UnixEpoch);
        GraphAsk.Run(Repo(), "search Reader", Read, history);

        var refused = GraphAsk.Run(Repo(), "@9 | ids", Read, history);
        Assert.IsFalse(refused.Ok);
        StringAssert.Contains(refused.Text, "@1 to @1");

        var nowhere = Ask("@ | ids");
        Assert.IsFalse(nowhere.Ok, "without a history there is nothing for @ to mean");
    }
}
