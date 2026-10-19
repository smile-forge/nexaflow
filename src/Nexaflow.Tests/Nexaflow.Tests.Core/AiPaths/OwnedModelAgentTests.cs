using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.AiPaths;

/// <summary>
/// The agent loop on Nexaflow's own model, end to end. What is checked is what the harness owns — which tools ran with
/// which arguments, what was asked for approval, what the page holds afterwards, the language of the reply, and that
/// the run ended by answering rather than at the step cap, the repeat guard or a tool block it could not write —
/// never the wording, which varies between runs and models.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("chat-interface")]
public class OwnedModelAgentTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    public TestContext TestContext { get; set; } = null!;

    [ClassCleanup]
    public static Task Stop() => OwnedModelHarness.StopAsync();

    /// <summary>A notes page: what it holds is the state an action changes.</summary>
    private sealed class NotesPage : IPageViewModel
    {
        public List<string> Notes { get; } = ["Call the dentist"];
        public List<(string Tool, JsonObject Arguments)> Calls { get; } = [];
        public Dictionary<string, string> Contacts { get; } = new() { ["Ada"] = "ada@example.test", ["Grace"] = "grace@example.test" };
        public List<(string To, string Text)> Sent { get; } = [];

        public string GetContext() => "A notes page. Notes: " + string.Join("; ", Notes);

        public IReadOnlyList<IClientTool> GetClientTools() =>
        [
            Tool("get_weather", "Get today's weather for a city.", [new("city", "The city.")], ToolSafety.SafeOperation,
                 args => ToolResult.Ok("weather read", $"Weather in {args["city"]}: 18°C, light rain.")),
            Tool("add_note", "Add a note to the notes page.", [new("text", "The note's text.")], ToolSafety.RequiresApproval,
                 args => { Notes.Add((string)args["text"]!); return ToolResult.Ok("note added", "The note was added."); }),
            Tool("find_contact", "Find a contact's email address by name.", [new("name", "The contact's name.")], ToolSafety.SafeOperation,
                 args => Contacts.TryGetValue(((string?)args["name"] ?? "").Trim(), out var email)
                     ? ToolResult.Ok("contact found", $"{args["name"]}: {email}")
                     : ToolResult.Error("no such contact", $"No contact is named {args["name"]}.")),
            Tool("send_message", "Send a message to an email address.", [new("email", "The address."), new("text", "The message.")], ToolSafety.RequiresApproval,
                 args => { Sent.Add(((string)args["email"]!, (string)args["text"]!)); return ToolResult.Ok("message sent", "Sent."); }),
        ];

        private IClientTool Tool(string name, string description, IReadOnlyList<ClientToolParameter> parameters, ToolSafety safety,
                                 Func<JsonObject, ToolResult> run)
            => new DelegateClientTool(name, description, parameters, safety, (args, _) =>
            {
                Calls.Add((name, args));
                return Task.FromResult(run(args));
            });

        public IEnumerable<string> Called => Calls.Select(c => c.Tool);
    }

    private sealed record Run(AiResponse? Response, RecordingModel Model, Approver Approver, IReadOnlyCollection<string> ToolNames)
    {
        /// <summary>Tool blocks the harness may have to send back before the model writes one it can read — Gemma's
        /// first try at a call is often its own syntax.</summary>
        private const int Corrections = 2;

        public string Text => Response?.Text ?? "";

        /// <summary>The run answered: it did not stop at a guard, any tool block the model could not write was put
        /// right within <see cref="Corrections"/> tries, and the answer itself holds no call left unread.</summary>
        public void EndedCleanly()
        {
            Assert.IsNotNull(Response, "the run was cancelled or refused\n" + Model.Transcript);
            Assert.IsFalse(Text.Contains("_(stopped:"), $"the run stopped at a guard: {Text}\n{Model.Transcript}");
            Assert.IsTrue(Model.MalformedBlocks <= Corrections,
                $"the model needed {Model.MalformedBlocks} tries to write a tool block the harness could read\n{Model.Transcript}");
            Assert.AreEqual(0, Nexaflow.Core.Services.Agent.ClientBlockParser.Parse(Text, ToolNames).ParseErrors.Count,
                "the answer holds a tool call nobody ran\n" + Model.Transcript);
            Assert.IsFalse(string.IsNullOrWhiteSpace(Text), "the run ended with nothing said\n" + Model.Transcript);
        }
    }

    /// <summary>Runs the agent on <paramref name="input"/>. With <c>NEXAFLOW_TEST_TRANSCRIPTS</c> set, every turn of the
    /// run is written to that folder, one file a test — what the model was asked and what it wrote.</summary>
    private async Task<Run> AskAsync(IPageViewModel page, string input, bool approve = true)
    {
        var model = new RecordingModel(await OwnedModelHarness.ModelAsync());
        var approver = new Approver(approve);
        using var cts = new CancellationTokenSource(Limit);
        var response = await OwnedModelHarness.Service(model).RunAgentAsync(page, input, true, approver, cts.Token);

        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_TRANSCRIPTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var name = string.Concat($"{TestContext.TestName}-{input}".Where(char.IsLetterOrDigit).Take(80));
            File.WriteAllText(Path.Combine(folder, name + ".txt"), model.Transcript + "\n=== final\n" + response?.Text);
        }
        return new Run(response, model, approver, [.. page.GetClientTools().Select(t => t.Name)]);
    }

    [TestMethod]
    [CoversNode("owned-llm")]
    public async Task AQuestionNoToolAnswers_IsAnsweredWithoutCallingOne()
    {
        var page = new NotesPage();

        var run = await AskAsync(page, "What is seven times six?");

        run.EndedCleanly();
        Assert.AreEqual(0, page.Calls.Count, $"no tool helps with arithmetic, yet {string.Join(", ", page.Called)} ran\n{run.Model.Transcript}");
    }

    [TestMethod]
    [CoversNode("owned-llm")]
    public async Task AQuestionAToolAnswers_CallsItWithWhatTheUserAsked()
    {
        var page = new NotesPage();

        var run = await AskAsync(page, "What's the weather in Lyon today?");

        run.EndedCleanly();
        var weather = page.Calls.Where(c => c.Tool == "get_weather").ToList();
        Assert.AreEqual(1, weather.Count, run.Model.Transcript);
        StringAssert.Contains(((string?)weather[0].Arguments["city"])?.ToLowerInvariant(), "lyon");
    }

    [TestMethod]
    [CoversNode("owned-llm")]
    public async Task AnAction_IsAskedForApproval_AndChangesThePage()
    {
        var page = new NotesPage();

        var run = await AskAsync(page, "Add a note that says: buy milk.");

        run.EndedCleanly();
        Assert.IsTrue(run.Approver.Batches.Any(b => b.Contains("add_note")), "adding a note is approved first\n" + run.Model.Transcript);
        Assert.AreEqual(1, page.Notes.Count(n => n.Contains("milk", StringComparison.OrdinalIgnoreCase)), string.Join(" | ", page.Notes));
    }

    [TestMethod]
    [CoversNode("owned-llm")]
    public async Task ADeclinedAction_ChangesNothing_AndTheRunStillEnds()
    {
        var page = new NotesPage();

        var run = await AskAsync(page, "Add a note that says: buy milk.", approve: false);

        run.EndedCleanly();
        CollectionAssert.AreEqual(new[] { "Call the dentist" }, page.Notes);
    }

    [TestMethod]
    [CoversNode("owned-llm")]
    public async Task ATaskOfTwoSteps_FindsTheAddressFirst_ThenSendsToIt()
    {
        var page = new NotesPage();

        var run = await AskAsync(page, "Send Ada a message saying the meeting moved to 3pm.");

        run.EndedCleanly();
        var order = page.Called.ToList();
        Assert.IsTrue(order.IndexOf("find_contact") >= 0 && order.IndexOf("find_contact") < order.IndexOf("send_message"),
                      $"tools ran as {string.Join(", ", order)}\n{run.Model.Transcript}");
        Assert.AreEqual("ada@example.test", page.Sent.Single().To);
        StringAssert.Contains(page.Sent.Single().Text, "3");
    }

    [TestMethod]
    [CoversNode("owned-llm")]
    [DataRow("Quelle est la capitale de l'Italie ? Réponds en une phrase.", "fr")]
    [DataRow("Was ist die Hauptstadt von Spanien? Antworte in einem Satz.", "de")]
    [DataRow("What is the capital of Portugal? Answer in one sentence.", "en")]
    public async Task TheReply_IsInTheLanguageTheUserWrote(string question, string language)
    {
        var run = await AskAsync(new NotesPage(), question);

        run.EndedCleanly();
        Assert.AreEqual(language, ReplyLanguage.Of(run.Text), run.Text);
    }

    /// <summary>A page with more tools than are shown unranked: the model ranks them before it answers.</summary>
    private sealed class ToolboxPage : IPageViewModel
    {
        public List<(string Tool, JsonObject Arguments)> Calls { get; } = [];

        public string GetContext() => "A toolbox page of everyday utilities.";

        public IReadOnlyList<IClientTool> GetClientTools() =>
        [
            Tool("convert_currency", "Convert an amount of money between currencies.",
                 [new("amount", "How much."), new("from", "The currency it is in, e.g. EUR."), new("to", "The currency wanted, e.g. USD.")],
                 args => $"{args["amount"]} {args["from"]} is {double.Parse((string?)args["amount"]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture) * 1.08:0.00} {args["to"]}."),
            Tool("convert_units", "Convert a length, weight or temperature between units.", [new("value", "The value."), new("from", "Its unit."), new("to", "The unit wanted.")], _ => "Converted."),
            Tool("get_time_zone", "Get the time zone of a city.", [new("city", "The city.")], _ => "UTC+1."),
            Tool("spell_check", "Check the spelling of a passage.", [new("text", "The passage.")], _ => "No mistakes."),
            Tool("word_count", "Count the words in a passage.", [new("text", "The passage.")], _ => "12 words."),
            Tool("define_word", "Give the dictionary definition of a word.", [new("word", "The word.")], _ => "A definition."),
            Tool("random_number", "Pick a random whole number in a range.", [new("min", "Lowest."), new("max", "Highest.")], _ => "7."),
            Tool("days_between", "Count the days between two dates.", [new("from", "First date."), new("to", "Second date.")], _ => "30 days."),
        ];

        private IClientTool Tool(string name, string description, IReadOnlyList<ClientToolParameter> parameters, Func<JsonObject, string> run)
            => new DelegateClientTool(name, description, parameters, ToolSafety.SafeOperation, (args, _) =>
            {
                Calls.Add((name, args));
                return Task.FromResult(ToolResult.Ok($"{name} ran", run(args)));
            });
    }

    [TestMethod]
    [CoversNode("owned-llm")]
    public async Task OnAPageOfManyTools_TheRightOneIsFoundAndCalled()
    {
        var page = new ToolboxPage();

        var run = await AskAsync(page, "Convert 50 euros to US dollars.");

        run.EndedCleanly();
        var converted = page.Calls.Where(c => c.Tool == "convert_currency").ToList();
        Assert.AreEqual(1, converted.Count, $"tools ran: {string.Join(", ", page.Calls.Select(c => c.Tool))}\n{run.Model.Transcript}");
        Assert.AreEqual(50d, double.Parse(converted[0].Arguments["amount"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A page whose tools hand the model an image and a recording.</summary>
    private sealed class MediaPage : IPageViewModel
    {
    public const string Voicemail = "Hi, it's Ada. The project meeting has moved to Thursday afternoon.";

        public string GetContext() => "A media page with a chart and a voicemail.";

        /// <summary><paramref name="text"/> spoken by the machine's own voice, as a WAV.</summary>
        private static byte[] Spoken(string text)
        {
            using var voice = new System.Speech.Synthesis.SpeechSynthesizer();
            using var wav = new MemoryStream();
            voice.SetOutputToWaveStream(wav);
            voice.Speak(text);
            return wav.ToArray();
        }

        public IReadOnlyList<IClientTool> GetClientTools() =>
        [
            new DelegateClientTool("capture_chart", "Capture the page's chart as an image to look at.", [], ToolSafety.SafeOperation, (_, _) =>
                Task.FromResult(ToolResult.Ok("chart captured", "The chart is attached.")
                    with { Images = [new ContextImage(File.ReadAllBytes(TestSampleData.Files("images").First(f => f.EndsWith(".png"))), "image/png", "chart")] })),
            new DelegateClientTool("play_voicemail", "Hand over the page's voicemail recording to listen to.", [], ToolSafety.SafeOperation, (_, _) =>
                Task.FromResult(ToolResult.Ok("voicemail handed over", "The recording is attached.")
                    with { Audio = [new ContextAudio(Spoken(Voicemail), "the voicemail")] })),
        ];
    }

    [TestMethod]
    [CoversNode("owned-llm")]
    public async Task AnImageAToolCaptured_ReachesTheModel_AndTheRunGoesOn()
    {
        var run = await AskAsync(new MediaPage(), "Capture the chart and tell me what colour it is.");

        run.EndedCleanly();
        Assert.IsTrue(run.Model.Turns.Any(t => t.Asked[^1].Attachments?.Any(a => a.IsImage) == true),
                      "the captured image rode the turn after the tool\n" + run.Model.Transcript);
    }

    /// <summary>What the voicemail says is the one fact the model can only have from hearing it — the single place this
    /// suite looks at what a reply says.</summary>
    [TestMethod]
    [CoversNode("owned-llm")]
    public async Task ARecordingAToolHandedOver_IsHeard()
    {
        var run = await AskAsync(new MediaPage(), "Listen to the voicemail and tell me which day the meeting is now.");

        run.EndedCleanly();
        Assert.IsTrue(run.Model.Turns.Any(t => t.Asked[^1].Attachments?.Any(a => a.IsAudio) == true),
                      "the recording rode the turn after the tool\n" + run.Model.Transcript);
        StringAssert.Contains(run.Text.ToLowerInvariant(), "thursday", "the day is only in the recording\n" + run.Model.Transcript);
    }
}
