using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Nexaflow.Core.AI;
using Nexaflow.Core.Services.Documents;
using Nexaflow.Core.Services.Generation;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.IO.Documents.Processing;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Documents;

/// <summary>
/// The whole-document tools: each works only on a markdown document the user has given the assistant — what their editor holds
/// where they set it going — with one model held for the whole job; keeps what it works out under the workspace's document notes;
/// and opens what it made as a draft beside copies of what its links name, its Save offering the document's folder.
/// </summary>
[TestClass]
[CoversNode("agent-document-tools")]
public class DocumentToolsTests
{
    private string _home = "";
    private readonly List<object> _opened = [];

    [TestInitialize]
    public void Setup()
    {
        _home = Path.Combine(TestTemp.Root, "nexaflow-documents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_home, recursive: true);

    /// <summary>The folder the document is in, given to the assistant.</summary>
    private string Folder => Path.Combine(_home, "docs");

    private string Notes => Path.Combine(_home, "notes");

    private string Document(string text, string name = "pets.md")
    {
        var path = Path.Combine(Folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private GeneratedFiles Files(PathScope scope) =>
        new(scope, Path.Combine(_home, "drafts"), _ => Path.Combine(_home, "offered"), made => { _opened.Add(made); return Task.CompletedTask; });

    private DocumentTools Tools(IDocumentModel? model, Func<string, string?>? written = null, PathScope? scope = null)
    {
        scope ??= new PathScope([], [Folder]);
        return new DocumentTools(new DocumentJob(_ => Task.FromResult(model), Notes, scope, written ?? (_ => null), Files(scope), activities: null));
    }

    [TestMethod]
    public async Task ADocumentTheUserHasNotGiven_IsRefused()
    {
        var elsewhere = Path.Combine(_home, "elsewhere.md");
        File.WriteAllText(elsewhere, "# Hidden\n");

        var result = await Tools(new Shouting()).Summary.InvokeAsync(new JsonObject { ["document"] = elsewhere }, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.ModelText, "outside what the user has given you");
    }

    [TestMethod]
    public async Task AFileThatIsNotMarkdown_IsRefused()
    {
        var result = await Tools(new Shouting()).Summary.InvokeAsync(new JsonObject { ["document"] = Document("x", "notes.txt") }, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.ModelText, "not a markdown document");
    }

    [TestMethod]
    public async Task WithNoDocumentsModel_NothingIsDone()
    {
        var result = await Tools(model: null).Summary.InvokeAsync(new JsonObject { ["document"] = Document("# Pets\n") }, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.ModelText, "Documents row");
        Assert.AreEqual(0, _opened.Count);
    }

    [TestMethod]
    public async Task ATranslation_OpensAsADraftBesideCopiesOfItsPictures_ItsSaveOfferingTheDocumentsFolder()
    {
        File.WriteAllBytes(Path.Combine(Folder, "dog.png"), [1, 2, 3]);
        var path = Document("# Pets\n\nDogs bark.\n\n![A dog](dog.png)\n");

        var result = await Tools(new Shouting()).Translation.InvokeAsync(new JsonObject { ["document"] = path, ["language"] = "Shouting" }, CancellationToken.None);

        Assert.IsFalse(result.IsError, result.ModelText);
        var draft = _opened.OfType<DraftFile>().Single();
        Assert.AreEqual(Folder, draft.OfferedFolder, "Save starts where the document is");
        StringAssert.Contains(File.ReadAllText(draft.Path), "DOGS BARK.");
        Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(draft.Path)!, "dog.png")), "the picture it names still shows");
        Assert.IsTrue(Directory.EnumerateFiles(Notes, "*", SearchOption.AllDirectories).Any(), "what it worked out is kept");
    }

    [TestMethod]
    public async Task WhatTheEditorHolds_IsWhatIsWorkedOn_NotWhatIsSaved()
    {
        var path = Document("# Saved\n");

        await Tools(new Shouting(), written: asked => asked == path ? "# Edited\n" : null)
            .Translation.InvokeAsync(new JsonObject { ["document"] = path, ["language"] = "Shouting" }, CancellationToken.None);

        StringAssert.Contains(File.ReadAllText(_opened.OfType<DraftFile>().Single().Path), "# EDITED");
    }

    [TestMethod]
    public async Task ASummary_OpensAsADocumentWhoseHeadingsLeadBack()
    {
        var path = Document("# Pets\n\nDogs bark.\n\n## Cats\n\nCats purr.\n");

        var result = await Tools(new Shouting()).Summary.InvokeAsync(new JsonObject { ["document"] = path }, CancellationToken.None);

        Assert.IsFalse(result.IsError, result.ModelText);
        var written = File.ReadAllText(_opened.OfType<DraftFile>().Single().Path);
        StringAssert.Contains(written, "# Pets [↗]", "a document under one title is that title, written once");
        StringAssert.Contains(written, "## Cats");
        StringAssert.Contains(written, new Uri(path).AbsoluteUri + "#heading%3Apets");
    }

    [TestMethod]
    public async Task AQuestion_IsAnsweredFromTheSummaryKept_CitingItsPartsWithLinksBack()
    {
        var path = Document("# Pets\n\nDogs bark.\n\n## Cats\n\nCats purr.\n");
        var tools = Tools(new Shouting());
        var asked = new JsonObject { ["document"] = path, ["question"] = "What do cats do?" };

        Assert.IsTrue(tools.Question.RunsInBackground(asked), "a document not yet summarised is summarised first, in the background");
        Assert.AreEqual(ToolSafety.RequiresApproval, tools.Question.SafetyOf(asked));

        var result = await tools.Question.InvokeAsync(asked, CancellationToken.None);

        Assert.IsFalse(result.IsError, result.ModelText);
        StringAssert.Contains(result.ModelText, "They purr [1].");
        StringAssert.Contains(result.ModelText, $"[1] [Pets](<{new Uri(path).AbsoluteUri}#heading%3Apets>)");
        Assert.AreEqual(0, _opened.Count, "an answer opens nothing");

        Assert.IsFalse(tools.Question.RunsInBackground(asked), "its summary kept, it is answered at once");
        Assert.AreEqual(ToolSafety.SafeOperation, tools.Question.SafetyOf(asked));

        File.WriteAllText(path, "# Pets\n\nDogs howl.\n");
        Assert.IsTrue(tools.Question.RunsInBackground(asked), "an edited document is summarised again first");
    }

    [TestMethod]
    public async Task AJobThatCannotFinish_SaysWhy_AndOpensNothing()
    {
        var result = await Tools(new Mumbling()).Summary.InvokeAsync(new JsonObject { ["document"] = Document("# Pets\n\nDogs.\n") }, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.ModelText, "did not say what the document is");
        Assert.AreEqual(0, _opened.Count);
    }

    [TestMethod]
    public void AGridSavedBeforeTheDocumentsRow_StartsItWithGeneralIntelligencesChain()
    {
        var config = new AiConfig();
        config.Assignments[nameof(AiAbility.GeneralIntelligence)] = AbilityChain.Of("local", "claude");

        Assert.IsTrue(config.AddDefaultDocuments());
        CollectionAssert.AreEqual(new[] { "local", "claude" }, config.Assignments[nameof(AiAbility.Documents)].ColumnIds.ToArray());

        config.Assignments[nameof(AiAbility.Documents)] = AbilityChain.Of();
        Assert.IsFalse(config.AddDefaultDocuments(), "a chain the user emptied is left alone");
        Assert.AreEqual(0, config.Assignments[nameof(AiAbility.Documents)].ColumnIds.Count);
    }

    [TestMethod]
    public async Task TheModelSaysHowEachAnswerEnded()
    {
        var model = new WorkspaceDocumentModel(new Streaming([new LlmTextDelta("half"), new LlmReplyEnded(LlmReplyEnd.LengthLimit)]), "m", 8192);
        var cut = await model.AnswerAsync("i", "a", CancellationToken.None);

        Assert.AreEqual("half", cut.Text);
        Assert.IsTrue(cut.RanOut);

        var failing = new WorkspaceDocumentModel(new Streaming([], fails: true), "m", 8192);
        Assert.AreEqual(DocumentReplyEnd.Failed, (await failing.AnswerAsync("i", "a", CancellationToken.None)).Ended);
    }

    /// <summary>A model that says every document is the same one, summarises a part as "S", and translates by shouting.</summary>
    private sealed class Shouting : IDocumentModel
    {
        public string Key => "shouting";
        public int ContextTokens => 8192;
        public int CharsPerToken => 3;

        public Task<DocumentReply> AnswerAsync(string instructions, string asked, CancellationToken ct) => Task.FromResult(new DocumentReply(
            instructions.StartsWith("You say what a whole document is", StringComparison.Ordinal)
                ? "GENRE: non-fiction\nTOPIC: pets\nAUDIENCE: owners\nINTENT: to inform\nREGISTER: neutral\nLANGUAGE: English"
          : instructions.StartsWith("You summarise", StringComparison.Ordinal) ? "S"
          : instructions.StartsWith("You translate a document", StringComparison.Ordinal) ? Translated(asked)
          : instructions.StartsWith("You find the parts", StringComparison.Ordinal) ? "1"
          : instructions.StartsWith("You answer a question", StringComparison.Ordinal) ? "They purr [1]."
          : string.Empty,
            DocumentReplyEnd.Finished));

        private static string Translated(string asked)
        {
            var from = asked.IndexOf("Translate each of these into", StringComparison.Ordinal);
            var to = asked.IndexOf("Answer with the same @@ lines", StringComparison.Ordinal);
            var pieces = Regex.Split(asked[asked.IndexOf('\n', from)..to], @"^@@ \d+\r?$", RegexOptions.Multiline).Skip(1).Select(piece => piece.Trim()).ToList();

            return string.Concat(pieces.Select((piece, at) =>
                $"@@ {at + 1}\n{Regex.Replace(piece, @"⟦\d+⟧|[^⟦]+", match => match.Value.StartsWith('⟦') ? match.Value : match.Value.ToUpperInvariant())}\n\n"));
        }
    }

    /// <summary>A model that never answers in the lines it is asked for.</summary>
    private sealed class Mumbling : IDocumentModel
    {
        public string Key => "mumbling";
        public int ContextTokens => 8192;
        public int CharsPerToken => 3;

        public Task<DocumentReply> AnswerAsync(string instructions, string asked, CancellationToken ct) =>
            Task.FromResult(new DocumentReply("Something about pets.", DocumentReplyEnd.Finished));
    }

    /// <summary>A language model that streams what it is given, or fails.</summary>
    private sealed class Streaming(IReadOnlyList<LlmStreamEvent> said, bool fails = false) : ILlmProvider
    {
        public string Name => "Streaming";
        public DataResidency Residency => DataResidency.StaysOnMachine;

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (fails) throw new LlmProviderException("gone");

            foreach (var item in said)
            {
                await Task.Yield();
                yield return item;
            }
        }

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
