using System.Text.Json.Nodes;

using Nexaflow.Core.Services.Generation;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Documents.Processing;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Documents;

/// <summary>
/// Summarises a markdown document as a whole with the workspace's Documents model: what the document is, said first, then
/// each section from the deepest up (<see cref="DocumentSummarizer"/>), written as a document of its own the reader goes down
/// into from its abstract, a heading per section leading back to it (<see cref="SummaryMarkdown"/>). A long document takes a
/// while, so it runs in the background; doing it again asks only about the sections that changed.
/// </summary>
internal sealed class SummarizeDocumentTool(DocumentJob job) : IClientTool
{
    public string Name => "summarize_document";

    public string Description =>
        "Summarises a whole markdown document, section by section, with an abstract, keeping to the way it is written; the " +
        "summary opens as a new document, each heading leading back to its section. Runs in the background; a long document " +
        "takes a while. For a short passage, summarise it yourself instead.";

    public IReadOnlyList<ClientToolParameter> Parameters => [DocumentJob.Document, new("path", GenerationTools.SavedWhere, Required: false)];

    public ToolSafety Safety => ToolSafety.RequiresApproval;

    public bool Parallelizable => false;

    public ToolCallAnnouncement? Announce(JsonObject arguments)
    {
        var named = DocumentJob.Named(arguments);
        return new(Str.Get("Shell.Documents.Announce.Summarize"), named, Str.Format("Shell.Documents.Announce.SummarizingFormat", named),
                   Str.Get("Shell.Documents.Announce.SummaryMade"), Str.Get("Shell.Documents.Announce.NoSummary"),
                   Str.Get("Shell.Documents.Announce.InBackground"));
    }

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct) =>
        job.RunAsync(arguments, name => Str.Format("Shell.Documents.SummaryNameFormat", name) + ".md",
                     [DocumentGistPass.Shelf, DocumentSummarizer.Shelf],
                     async work =>
                     {
                         var gist = await new DocumentGistPass(work.Model, work.Notes, work.Progress).SayAsync(work.Reading, ct);
                         var summary = await new DocumentSummarizer(work.Model, work.Notes, work.Progress).SummarizeAsync(work.Reading, gist, ct);

                         return SummaryMarkdown.Write(summary, gist, System.IO.Path.GetFileNameWithoutExtension(work.Path), work.Path);
                     },
                     ct);

    /// <summary>A whole document takes minutes or hours: it is summarised in the background.</summary>
    public bool RunsInBackground(JsonObject arguments) => true;
}
