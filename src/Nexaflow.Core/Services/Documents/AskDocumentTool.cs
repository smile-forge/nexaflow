using System.IO;
using System.Text;
using System.Text.Json.Nodes;

using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Documents.Processing;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Documents;

/// <summary>
/// Answers a question about a whole markdown document with the workspace's Documents model: down what its summary says of each
/// section to the parts that bear on the question (<see cref="DocumentAnswerer"/>), which are read, and cited in the answer,
/// each leading back to its section. It works from the record the document's summary left (<see cref="DocumentRecord"/>); a
/// document with none of what it says now is summarised first, which takes a while — so that call is approved first and runs
/// in the background, and one with a record is answered at once.
/// </summary>
internal sealed class AskDocumentTool(DocumentJob job) : IClientTool
{
    public const string QuestionParameter = "question";

    public string Name => "ask_document";

    public string Description =>
        "Answers a question about a whole markdown document too long to read yourself — which sections deal with something, what " +
        "it says of someone or something all through it — by going down its section summaries to the parts that bear on the " +
        "question and reading those. The answer cites the parts by number, each with a link to its section. A document not yet " +
        "summarised as it stands is summarised first, in the background.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
        [DocumentJob.Document, new(QuestionParameter, "The question to answer from the document, as the user asked it.")];

    public ToolSafety Safety => ToolSafety.RequiresApproval;

    public bool Parallelizable => false;

    /// <summary>A document whose summary is kept is only read, so it is answered without asking; one summarised first is approved.</summary>
    public ToolSafety SafetyOf(JsonObject arguments) => Recorded(arguments) ? ToolSafety.SafeOperation : ToolSafety.RequiresApproval;

    /// <summary>A document whose summary is kept is answered while the request waits; one summarised first, in the background.</summary>
    public bool RunsInBackground(JsonObject arguments) => !Recorded(arguments);

    public ToolCallAnnouncement? Announce(JsonObject arguments)
    {
        var named = DocumentJob.Named(arguments);
        return new(Str.Get("Shell.Documents.Announce.Ask"), named, Str.Format("Shell.Documents.Announce.AskingFormat", named),
                   Str.Get("Shell.Documents.Announce.Answered"), Str.Get("Shell.Documents.Announce.NoAnswer"),
                   Recorded(arguments) ? null : Str.Get("Shell.Documents.Announce.SummarizingFirst"));
    }

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (ToolArgs.Str(arguments, QuestionParameter) is not { Length: > 0 } asked)
            return Task.FromResult(ToolResult.Error("no question", $"Give the '{QuestionParameter}' to answer from the document."));

        return job.WorkAsync(arguments, DocumentWorkKind.Question, null, async work =>
        {
            var record = await DocumentRecord.ReadAsync(work.Notes, SummaryQuestion.Shelf, work.Reading, ct);
            if (record is null)
            {
                var gist = await new DocumentGistPass(work.Model, work.Notes, work.Progress).SayAsync(work.Reading, ct);
                record = new DocumentRecord(gist, await new SectionPass(work.Model, work.Notes, new SummaryQuestion(), work.Progress).RunAsync(work.Reading, gist, ct));
                work.Notes.Settle(DocumentGistPass.Shelf, SummaryQuestion.Shelf);
            }

            return Answered(await new DocumentAnswerer(work.Model, work.Progress).AnswerAsync(work.Reading, record, asked.Trim(), ct), work.Path);
        }, ct);
    }

    private bool Recorded(JsonObject arguments) => job.Recorded(arguments, SummaryQuestion.Shelf);

    /// <summary>The answer as the model is told it: what it says, then each part it cites, linked to its section.</summary>
    private static ToolResult Answered(DocumentAnswer answer, string path)
    {
        var name = Path.GetFileName(path);
        if (answer.Parts.Count == 0)
            return ToolResult.Ok(Str.Format("Shell.Documents.NothingAnswersFormat", name), $"No part of '{name}' bears on the question.");

        var cited = answer.Parts.Where(part => part.Cited).ToList();
        var listed = cited.Count > 0 ? cited : answer.Parts;

        var told = new StringBuilder(answer.Said).Append("\n\n")
            .Append(cited.Count > 0 ? "The parts it cites" : "The parts it was read from").Append(", each linked to its section of the document:\n");
        foreach (var part in listed)
            told.Append('[').Append(part.Number).Append("] [").Append(part.Where.Length > 0 ? part.Where : "Before its first heading").Append("](<")
                .Append(part.Section?.Anchor is { } anchor ? SummaryMarkdown.Back(path, anchor) : new Uri(Path.GetFullPath(path)).AbsoluteUri).Append(">)\n");

        if (answer.InBrief) told.Append("\nThe parts were read from their summaries, being too much to read in full.");

        return ToolResult.Ok(Str.Format("Shell.Documents.AnsweredFormat", answer.Parts.Count, name), told.ToString());
    }
}
