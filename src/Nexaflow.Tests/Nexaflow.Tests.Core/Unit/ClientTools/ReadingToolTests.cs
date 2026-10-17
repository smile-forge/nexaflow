using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nexaflow.Core.Services.Agent;
using Nexaflow.Features.Common.Search;
using Nexaflow.IO.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.ClientTools;

/// <summary>
/// The machine's model reading for the larger one: a file that fits is read whole; a longer one in parts, each noted for the
/// task and the task done from the notes; a part with nothing for the task leaves no note. A format an extractor claims is read
/// through it, and a file that is not text is said to be so.
/// </summary>
[TestClass]
[CoversNode("agent-reading-delegate")]
public class ReadingToolTests
{
    /// <summary>A reader that answers by what it is asked to do, and keeps every request.</summary>
    private sealed class Reader(int context, Func<string, string>? part = null) : ILlmProvider
    {
        public List<string> Asked { get; } = [];
        public string Name => "Nexaflow";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public bool IsAvailable => true;

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            var asked = messages.Last().Text;
            Asked.Add(asked);
            return Said(asked.Contains("Do the task from these documents.") ? "answered whole"
                      : asked.Contains("Do the task from these notes.")     ? "answered from notes"
                      : asked.Contains("Merge them into one set of notes")  ? "merged notes"
                      : part?.Invoke(asked) ?? $"note on {Regex.Match(asked, @"part \d+ of \d+").Value}");
        }

        public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default) => Task.FromResult<ModelInfo?>(new ModelInfo(context));
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["m"]);

        private static async IAsyncEnumerable<LlmStreamEvent> Said(string text)
        {
            await Task.Yield();
            yield return new LlmTextDelta(text);
        }
    }

    private sealed class PdfText(string text) : IFileTextExtractor
    {
        public bool CanExtract(string path) => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        public Task<string?> ExtractAsync(string path, long maxBytes, CancellationToken ct) => Task.FromResult<string?>(text);
    }

    /// <summary>The tool, with the temp folder the files are saved in open in a tab — or <paramref name="scope"/>.</summary>
    private static ReadingTool Tool(Reader reader, IFileTextExtractor? extractor = null, PathScope? scope = null)
        => new(reader, path => extractor is not null && extractor.CanExtract(path) ? extractor : null, _ => { },
               scope ?? new PathScope([], [TestTemp.Root]));

    private static string Saved(string name, string text)
    {
        var path = Path.Combine(TestTemp.CreateSubdirectory("nexaflow-reading-").FullName, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static JsonObject Asking(string task, params string[] files)
        => new() { ["task"] = task, ["files"] = new JsonArray([.. files.Select(f => (JsonNode)f)]) };

    [TestMethod]
    public async Task AFileThatFits_IsReadWhole()
    {
        var reader = new Reader(context: 8192);
        var path   = Saved("notes.txt", "The meeting moved to Thursday.");

        var result = await Tool(reader).InvokeAsync(Asking("When is the meeting?", path), default);

        Assert.IsFalse(result.IsError, result.ModelText);
        Assert.AreEqual("answered whole", result.ModelText);
        StringAssert.Contains(reader.Asked.Single(), "The meeting moved to Thursday.");
        CollectionAssert.AreEqual(new[] { path }, result.Attachments!.ToArray());
    }

    [TestMethod]
    public async Task ALongerFile_IsReadInParts_AndTheTaskDoneFromTheNotes()
    {
        var reader = new Reader(context: 3000);   // parts of 3000 × 0.6 × 3 characters
        var text   = string.Join('\n', Enumerable.Range(1, 400).Select(i => $"Line {i} of the report."));
        var path   = Saved("report.txt", text);

        var result = await Tool(reader).InvokeAsync(Asking("Summarise it.", path), default);

        var parts = reader.Asked.Where(a => a.Contains("Note everything in this part")).ToList();
        Assert.IsTrue(parts.Count > 1, "it was read in parts");
        StringAssert.Contains(parts[0], "Line 1 of the report.");
        StringAssert.Contains(parts[^1], "Line 400 of the report.");
        StringAssert.Contains(reader.Asked.Last(), $"From report.txt, part {parts.Count} of {parts.Count}");
        Assert.AreEqual("answered from notes", result.ModelText);
    }

    [TestMethod]
    public async Task APartWithNothingForTheTask_LeavesNoNote()
    {
        var reader = new Reader(context: 3000, part: _ => "Nothing relevant.");
        var path   = Saved("report.txt", string.Join('\n', Enumerable.Range(1, 1000).Select(i => $"Line {i}.")));

        var result = await Tool(reader).InvokeAsync(Asking("Find the budget.", path), default);

        Assert.AreEqual("Nothing in the files bears on the task.", result.ModelText);
        Assert.IsFalse(reader.Asked.Any(a => a.Contains("Do the task from these notes.")), "with no notes there is nothing to write up");
    }

    [TestMethod]
    public async Task AFormatAnExtractorClaims_IsReadThroughIt()
    {
        var reader = new Reader(context: 8192);
        var path   = Saved("report.pdf", "%PDF-1.7 binary");

        await Tool(reader, new PdfText("Revenue grew 4%.")).InvokeAsync(Asking("What grew?", path), default);

        StringAssert.Contains(reader.Asked.Single(), "Revenue grew 4%.");
    }

    [TestMethod]
    public async Task AFileThatIsNotText_IsSaidSo_AndTheRestAreRead()
    {
        var reader = new Reader(context: 8192);
        var binary = Path.Combine(TestTemp.CreateSubdirectory("nexaflow-reading-").FullName, "image.bin");
        File.WriteAllBytes(binary, [0x89, 0x50, 0x00, 0x00, 0x01, 0x02]);
        var text   = Saved("notes.txt", "Kept.");

        var result = await Tool(reader).InvokeAsync(Asking("Read them.", binary, text), default);

        StringAssert.Contains(result.ModelText, "Not read: image.bin: it is not text");
        var none = await Tool(reader).InvokeAsync(Asking("Read it.", binary), default);
        Assert.IsTrue(none.IsError);
    }

    [TestMethod]
    public async Task OnlyAFileTheUserGave_IsRead_OpenInATab_OrInAFolderOpenInOne()
    {
        var reader = new Reader(context: 8192);
        var given  = Saved("notes.txt", "Kept.");
        var other  = Saved("secret.txt", "Not for you.");

        var result = await Tool(reader, scope: new PathScope([given], [])).InvokeAsync(Asking("Read them.", given, other), default);

        Assert.IsFalse(reader.Asked.Any(a => a.Contains("Not for you.")), "nothing outside what the user gave is read");
        StringAssert.Contains(result.ModelText, "outside what the user has given you");
        CollectionAssert.AreEqual(new[] { given }, result.Attachments!.ToArray());
        var none = await Tool(reader, scope: PathScope.None).InvokeAsync(Asking("Read it.", given), default);
        Assert.IsTrue(none.IsError, "with nothing given, nothing is read");
    }

    [TestMethod]
    public void Parts_EndAtLineBreaks_AndTogetherAreTheText()
    {
        var text  = new StringBuilder();
        for (var i = 0; i < 300; i++) text.Append("word ").Append(i).Append(i % 7 == 0 ? '\n' : ' ');

        var parts = ReadingTool.Split(text.ToString(), 500);

        Assert.AreEqual(text.ToString(), string.Concat(parts));
        Assert.IsTrue(parts.All(p => p.Length <= 500));
        Assert.IsTrue(parts.SkipLast(1).All(p => p.EndsWith('\n')), "each part but the last ends at a line break");
    }
}
