using System.IO;
using System.Text.Json.Nodes;

using Nexaflow.Core.Services.Agent;
using Nexaflow.Core.Services.Generation;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.IO.Documents.Processing;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Services.Documents;

/// <summary>
/// What a whole-document tool does round its own work: finds the document the call names — only one the user has given the
/// assistant, a Markdown file or a Markdown Package — and what it says now, holds the workspace's Documents model for the whole
/// job, keeps what is worked out under the workspace's document notes, says how far the job has got on its row and to whatever
/// follows the document's work (<see cref="DocumentWorkLedger"/>), and keeps and opens what was made.
/// </summary>
/// <param name="model">The workspace's Documents model, held for one job; null where it has none now.</param>
/// <param name="notes">Where the workspace keeps what whole-document work works out (<see cref="Models.Workspace.DocumentNotesDir"/>).</param>
/// <param name="scope">What the user has given the assistant: the documents a tool may read.</param>
/// <param name="written">
/// What a document says, and where the files it names are read from, where that is not as it is saved — the edits the user set
/// it going with, the pictures added since — or null to read it where it is.
/// </param>
/// <param name="files">Where what is made goes, and how it is shown.</param>
/// <param name="activities">Where how far a job has got is said; inside a job, on the job's own row.</param>
/// <param name="ledger">The workspace's work under way on each document, so the same work is not set going twice on one and a page can show it.</param>
internal sealed class DocumentJob(Func<CancellationToken, Task<IDocumentModel?>> model, string notes, PathScope scope,
                                  Func<string, DocumentAsked?> written, GeneratedFiles files, IBackgroundActivityManager? activities,
                                  DocumentWorkLedger ledger)
{
    /// <summary>What names the document a tool works on.</summary>
    public const string DocumentParameter = "document";

    /// <summary>The document parameter, as a tool lists it.</summary>
    public static ClientToolParameter Document => new(DocumentParameter,
        "Full path of the markdown document, a Markdown file (.md) or a Markdown Package (.mdz): one open in a tab, or in a folder open in one.");

    /// <summary>What the call asked to be worked on, as it said it — for a tool's announcement.</summary>
    public static string Named(JsonObject arguments) =>
        ToolArgs.Str(arguments, DocumentParameter) is { Length: > 0 } asked ? Path.GetFileName(asked.Trim()) : Str.Get("Shell.Documents.TheDocument");

    /// <summary>
    /// Does <paramref name="kind"/> of work to the document the call names with <paramref name="work"/> — into
    /// <paramref name="language"/>, for a translation — lets go of whatever notes on <paramref name="shelves"/> it no longer needed,
    /// then keeps and opens what it made under the name <paramref name="named"/> gives the document's own; what is wrong with the
    /// call, or why the work could not finish, said instead.
    /// </summary>
    public Task<ToolResult> RunAsync(JsonObject arguments, DocumentWorkKind kind, string? language, Func<string, string> named,
                                     IReadOnlyList<string> shelves, Func<DocumentWorkContext, Task<DocumentMade>> work, CancellationToken ct)
    {
        var save = ToolArgs.Str(arguments, "path");
        if (files.Refusal(save) is { } refused) return Task.FromResult(ToolResult.Error("not saved there", refused));

        return WorkAsync(arguments, kind, language, async context =>
        {
            var made = await work(context);
            context.Notes.Settle([.. shelves]);

            var saved = await files.KeepDocumentAsync(made.Markdown, save, named(Path.GetFileNameWithoutExtension(context.Path)), context.Path, context.Folder, ct);
            var result = GeneratedFiles.Made(saved, null, null);
            return made.Caveat is { } caveat ? result with { ModelText = $"{result.ModelText}\n\n{caveat}" } : result;
        }, ct);
    }

    /// <summary>
    /// Does <paramref name="kind"/> of work to the document the call names with <paramref name="work"/>, which says what came of it
    /// — and lets go of the notes it no longer needs (<see cref="DocumentNotesFolder.Settle"/>); what is wrong with the call, or why
    /// the work could not finish, said instead. The same work under way on the document already is not set going again.
    /// </summary>
    public async Task<ToolResult> WorkAsync(JsonObject arguments, DocumentWorkKind kind, string? language,
                                            Func<DocumentWorkContext, Task<ToolResult>> work, CancellationToken ct)
    {
        if (ToolArgs.Str(arguments, DocumentParameter) is not { Length: > 0 } asked)
            return ToolResult.Error("no document", $"Give the '{DocumentParameter}' to work on: the full path of a markdown document.");
        if (scope.Resolve(asked) is not { } path) return ToolResult.Error("not given", RunScope.Outside(asked));
        if (!MarkdownDocuments.Names(path))
            return ToolResult.Error("not markdown", $"'{Path.GetFileName(path)}' is not a markdown document: a Markdown file (.md) or a Markdown Package (.mdz).");

        DocumentAsked? document;
        try { document = written(path) ?? await Task.Run(() => Read(path), ct); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ToolResult.Error("could not read", ex.Message);
        }
        if (document is null) return ToolResult.Error("no file", $"There is no markdown document at '{asked}'.");

        if (await model(ct) is not { } held) return ToolResult.Error("no model", "This workspace has no model in its Documents row.");

        using var underway = ledger.Begin(path, kind, language);
        if (underway is null)
            return ToolResult.Error("already under way", $"'{Path.GetFileName(path)}' is being {Done(kind, language)} already: it is listed with the background tasks.");

        var told = activities?.StartActivity(Str.Get("Shell.Documents.Starting"));

        try
        {
            var result = await work(new DocumentWorkContext(DocumentReading.Of(document.Source), held, new DocumentNotesFolder(notes, path),
                                                            new Telling(told, underway), path, document.Folder));
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
        if (!MarkdownDocuments.Names(path)) return false;

        try
        {
            var source = written(path)?.Source ?? MarkdownDocuments.ReadText(path);
            return source is not null && DocumentRecord.Held(new DocumentNotesFolder(notes, path), shelf, source);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The document at <paramref name="path"/> as it is saved — a package's own read inside it — or null where there is none.</summary>
    private static DocumentAsked? Read(string path) =>
        MarkdownDocuments.ReadText(path) is { } source ? new DocumentAsked(path, source, MarkdownDocuments.FolderOf(path)) : null;

    /// <summary>What a document undergoing <paramref name="kind"/> of work is being, as a refusal says it.</summary>
    private static string Done(DocumentWorkKind kind, string? language) => kind switch
    {
        DocumentWorkKind.Summary     => "summarised",
        DocumentWorkKind.Translation => $"translated into {language}",
        _                            => "asked about",
    };

    /// <summary>How far a job has got, said on its row as the step and the count, and to whatever follows the document's work.</summary>
    private sealed class Telling(IActivityHandle? told, DocumentWorkLedger.Underway underway) : IProgress<DocumentProgress>
    {
        public void Report(DocumentProgress value)
        {
            var said = Str.Format($"Shell.Documents.Step.{value.Step}", Math.Min(value.Done + 1, value.Of), value.Of);
            told?.Update(said);
            underway.Report(said);
        }
    }
}

/// <summary>
/// What a whole-document tool's work is handed: the document read, the model held for the job, its notes, where to say how far
/// it has got, where it is, and where the files it names are read from — beside it, or inside its package.
/// </summary>
internal sealed record DocumentWorkContext(DocumentReading Reading, IDocumentModel Model, DocumentNotesFolder Notes, IProgress<DocumentProgress> Progress,
                                           string Path, string? Folder);

/// <summary>What a whole-document tool made: the document, and what the user and the assistant are told of it besides — the
/// sections a translation kept as written — or null for nothing.</summary>
internal sealed record DocumentMade(string Markdown, string? Caveat = null);
