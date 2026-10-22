using System.IO;
using System.Text.Json.Nodes;

using Nexaflow.Core.Services.Agent;
using Nexaflow.Core.Services.Generation;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.IO.Documents.Processing;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Documents;

/// <summary>
/// What a whole-document tool does round its own work: finds the document the call names — only one the user has given the
/// assistant — and what it says now, holds the workspace's Documents model for the whole job, keeps what is worked out under
/// the workspace's document notes, says how far the job has got on its row, and keeps and opens what was made.
/// </summary>
/// <param name="model">The workspace's Documents model, held for one job; null where it has none now.</param>
/// <param name="notes">Where the workspace keeps what whole-document work works out (<see cref="Models.Workspace.DocumentNotesDir"/>).</param>
/// <param name="scope">What the user has given the assistant: the documents a tool may read.</param>
/// <param name="written">What a document says where that is not what is saved — the edits the user set it going with — or null to read its file.</param>
/// <param name="files">Where what is made goes, and how it is shown.</param>
/// <param name="activities">Where how far a job has got is said; inside a job, on the job's own row.</param>
internal sealed class DocumentJob(Func<CancellationToken, Task<IDocumentModel?>> model, string notes, PathScope scope,
                                  Func<string, string?> written, GeneratedFiles files, IBackgroundActivityManager? activities)
{
    /// <summary>What names the document a tool works on.</summary>
    public const string DocumentParameter = "document";

    /// <summary>The kinds of file a whole-document tool reads.</summary>
    private static readonly string[] Markdown = [".md", ".markdown"];

    /// <summary>The document parameter, as a tool lists it.</summary>
    public static ClientToolParameter Document => new(DocumentParameter, "Full path of the markdown document: one open in a tab, or in a folder open in one.");

    /// <summary>What the call asked to be worked on, as it said it — for a tool's announcement.</summary>
    public static string Named(JsonObject arguments) =>
        ToolArgs.Str(arguments, DocumentParameter) is { Length: > 0 } asked ? Path.GetFileName(asked.Trim()) : Str.Get("Shell.Documents.TheDocument");

    /// <summary>
    /// Works through the document the call names with <paramref name="work"/>, lets go of whatever notes on <paramref name="shelves"/>
    /// it no longer needed, then keeps and opens what it made under the name <paramref name="named"/> gives the document's own; what
    /// is wrong with the call, or why the work could not finish, said instead.
    /// </summary>
    public Task<ToolResult> RunAsync(JsonObject arguments, Func<string, string> named, IReadOnlyList<string> shelves,
                                     Func<DocumentWorkContext, Task<string>> work, CancellationToken ct)
    {
        var save = ToolArgs.Str(arguments, "path");
        if (files.Refusal(save) is { } refused) return Task.FromResult(ToolResult.Error("not saved there", refused));

        return WorkAsync(arguments, async context =>
        {
            var made = await work(context);
            context.Notes.Settle([.. shelves]);

            var saved = await files.KeepDocumentAsync(made, save, named(Path.GetFileNameWithoutExtension(context.Path)), context.Path, ct);
            return GeneratedFiles.Made(saved, null, null);
        }, ct);
    }

    /// <summary>
    /// Works through the document the call names with <paramref name="work"/>, which says what came of it — and lets go of the
    /// notes it no longer needs (<see cref="DocumentNotesFolder.Settle"/>); what is wrong with the call, or why the work could not
    /// finish, said instead.
    /// </summary>
    public async Task<ToolResult> WorkAsync(JsonObject arguments, Func<DocumentWorkContext, Task<ToolResult>> work, CancellationToken ct)
    {
        if (ToolArgs.Str(arguments, DocumentParameter) is not { Length: > 0 } asked)
            return ToolResult.Error("no document", $"Give the '{DocumentParameter}' to work on: the full path of a markdown file.");
        if (scope.Resolve(asked) is not { } path) return ToolResult.Error("not given", RunScope.Outside(asked));
        if (!Markdown.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return ToolResult.Error("not markdown", $"'{Path.GetFileName(path)}' is not a markdown document.");

        var source = written(path) ?? (File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null);
        if (source is null) return ToolResult.Error("no file", $"There is no file at '{asked}'.");

        if (await model(ct) is not { } held) return ToolResult.Error("no model", "This workspace has no model in its Documents row.");

        var told = activities?.StartActivity(Str.Get("Shell.Documents.Starting"));

        try
        {
            var result = await work(new DocumentWorkContext(DocumentReading.Of(source), held, new DocumentNotesFolder(notes, path), new Telling(told), path));
            told?.Complete();
            return result;
        }
        catch (DocumentWorkException ex)
        {
            told?.Fail(ex.Message);
            return ToolResult.Error("could not finish", ex.Message);
        }
    }

    /// <summary>
    /// Whether what the document the call names says now has a record kept on <paramref name="shelf"/> (<see cref="DocumentRecord"/>),
    /// so a tool working from it need not work through the whole document first. Said at once, for deciding how a call runs.
    /// </summary>
    public bool Recorded(JsonObject arguments, string shelf)
    {
        if (ToolArgs.Str(arguments, DocumentParameter) is not { Length: > 0 } asked || scope.Resolve(asked) is not { } path) return false;

        try
        {
            var source = written(path) ?? (File.Exists(path) ? File.ReadAllText(path) : null);
            return source is not null && DocumentRecord.Held(new DocumentNotesFolder(notes, path), shelf, source);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>How far a job has got, said on its row as the step and the count.</summary>
    private sealed class Telling(IActivityHandle? told) : IProgress<DocumentProgress>
    {
        public void Report(DocumentProgress value) =>
            told?.Update(Str.Format($"Shell.Documents.Step.{value.Step}", Math.Min(value.Done + 1, value.Of), value.Of));
    }
}

/// <summary>What a whole-document tool's work is handed: the document read, the model held for the job, its notes, where to say how far it has got, and where it is.</summary>
internal sealed record DocumentWorkContext(DocumentReading Reading, IDocumentModel Model, DocumentNotesFolder Notes, IProgress<DocumentProgress> Progress, string Path);
