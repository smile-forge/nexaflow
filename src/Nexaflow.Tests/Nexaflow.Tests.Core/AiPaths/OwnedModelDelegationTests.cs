using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Core.Services.Agent;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.AiPaths;

/// <summary>
/// Nexaflow's own model in the two parts it plays beside a larger one, for real: as the judge, it sends everyday work to
/// itself and heavier work to the larger model; as the reader, it finds what a task asks for in a document several times
/// longer than it can read at once. What is checked is the routing and the fact found, never the wording.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("agent-routing")]
[CoversNode("agent-reading-delegate")]
public class OwnedModelDelegationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

    [ClassCleanup]
    public static Task Stop() => OwnedModelHarness.StopAsync();

    public static IEnumerable<object[]> Light =>
    [
        ["Open my Downloads folder."],
        ["Rename notes.txt to todo.txt."],
        ["What's the capital of Australia?"],
        ["Turn on dark mode."],
        ["Translate 'good morning' into Spanish."],
    ];

    public static IEnumerable<object[]> Heavy =>
    [
        ["Refactor the parser in this project into separate lexing, parsing and binding passes, with tests for each, and explain the design."],
        ["Write a detailed 2,000-word report comparing the transaction isolation levels of PostgreSQL, SQL Server and Oracle, with examples of the anomalies each allows."],
        ["Prove that there are infinitely many primes of the form 4k+3, then explain why the same argument fails for 4k+1."],
        ["Go through this year's contracts, work out which ones renew automatically in the next quarter, and draft a negotiation plan for each."],
    ];

    /// <summary>Voice turns answered at once: whatever is asked for done by a tool, however much it describes, and the plain
    /// exchanges of conversation.</summary>
    public static IEnumerable<object[]> Quick =>
    [
        ["Hello there."],
        ["Open my Downloads folder."],
        ["Draw a picture of a red fox curled up asleep in a snowy pine forest at sunset, soft light through the trees, in the style of a watercolour."],
        ["Can you make me an image of an old stone lighthouse on a cliff in a storm, with huge waves crashing against the rocks below?"],
        ["Play some relaxing piano music."],
        ["What's the capital of Australia?"],
    ];

    /// <summary>Voice turns whose answer is worth reasoning over first.</summary>
    public static IEnumerable<object[]> Thoughtful =>
    [
        ["What do you think of this product page? Is it convincing?"],
        ["Should I use PostgreSQL or SQLite for a desktop app that syncs its data to the cloud?"],
        ["Explain why the sky is blue but sunsets are red."],
        ["Plan me a three-day trip to Kyoto in autumn."],
    ];

    [TestMethod]
    [DynamicData(nameof(Light))]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task EverydayWork_TheJudgeKeepsOnThisMachine(string request)
    {
        var model = await OwnedModelHarness.ModelAsync();
        using var cts = new CancellationTokenSource(Limit);

        Assert.AreEqual(true, await RequestTriage.SuitsAsync(model, model, request, [], cts.Token));
    }

    [TestMethod]
    [DynamicData(nameof(Heavy))]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task HeavierWork_TheJudgeSendsToTheLargerModel(string request)
    {
        var model = await OwnedModelHarness.ModelAsync();
        using var cts = new CancellationTokenSource(Limit);

        Assert.AreEqual(false, await RequestTriage.SuitsAsync(model, model, request, [], cts.Token));
    }

    [TestMethod]
    [DynamicData(nameof(Quick))]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task AQuickTurn_TheJudgeLetsAnswerAtOnce(string request)
    {
        var model = await OwnedModelHarness.ModelAsync();
        using var cts = new CancellationTokenSource(Limit);

        Assert.IsFalse(await RequestTriage.NeedsThoughtAsync(model, request, [], cts.Token));
    }

    [TestMethod]
    [DynamicData(nameof(Thoughtful))]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task ATurnWorthThinkingOver_TheJudgeGivesThought(string request)
    {
        var model = await OwnedModelHarness.ModelAsync();
        using var cts = new CancellationTokenSource(Limit);

        Assert.IsTrue(await RequestTriage.NeedsThoughtAsync(model, request, [], cts.Token));
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task ADocumentLongerThanItReads_IsReadInParts_AndTheFactAskedForFound()
    {
        var model = await OwnedModelHarness.ModelAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var context = (await model.GetModelInfoAsync(cts.Token))?.ContextWindowTokens ?? 8192;

        // About three times what one part holds, the fact deep in the second part.
        var text   = Minutes(context * RequestTriage.CharsPerToken * 3, fact: "The new access code for the archive room is 4417.", at: 0.45);
        var folder = Directory.CreateTempSubdirectory("nexaflow-reading-").FullName;
        var path   = Path.Combine(folder, "minutes.txt");
        await File.WriteAllTextAsync(path, text, cts.Token);
        var progress = new List<string>();

        var result = await new ReadingTool(model, _ => null, progress.Add, new PathScope([path], [])).InvokeAsync(
            new JsonObject { ["task"] = "What is the new access code for the archive room?", ["files"] = new JsonArray(path) }, cts.Token);

        Assert.IsFalse(result.IsError, result.ModelText);
        Assert.IsTrue(progress.Count(p => p.Contains(" — part ")) >= 3, string.Join("\n", progress));
        StringAssert.Contains(result.ModelText, "4417");
    }

    /// <summary>Meeting minutes of about <paramref name="length"/> characters, every item different, with <paramref name="fact"/>
    /// among them <paramref name="at"/> of the way through.</summary>
    private static string Minutes(int length, string fact, double at)
    {
        string[] people = ["Ada", "Grace", "Alan", "Edsger", "Barbara", "Donald", "Frances", "John"];
        string[] topics = ["the quarterly budget", "the office move", "the hiring plan", "the supplier contract", "the website redesign",
                           "the safety audit", "the customer survey", "the training schedule"];
        string[] verbs  = ["reported on", "raised a concern about", "proposed changes to", "asked for an update on", "summarised"];

        var minutes = new StringBuilder();
        var placed  = false;
        for (var i = 0; minutes.Length < length; i++)
        {
            if (!placed && minutes.Length >= length * at) { minutes.Append(fact).Append('\n'); placed = true; }
            minutes.Append($"Item {i + 1}: {people[i % people.Length]} {verbs[i % verbs.Length]} {topics[i * 3 % topics.Length]}; ")
                   .Append($"the figure discussed was {(i * 37 % 900) + 100} and the follow-up is due in week {i % 52 + 1}.\n");
        }
        return minutes.ToString();
    }
}
