using System.Text.Json.Nodes;

using Nexaflow.Core.Services.Generation;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Documents.Processing;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Documents;

/// <summary>
/// Translates a markdown document as a whole with the workspace's Documents model: what the document is, said first, its names
/// and terms gathered and given one way (<see cref="DocumentGlossaryPass"/>), then passage by passage keeping to its register,
/// tone and voice, everything but its words written as it was (<see cref="DocumentTranslator"/>). Runs in the background;
/// doing it again asks only about the passages that changed.
/// </summary>
internal sealed class TranslateDocumentTool(DocumentJob job) : IClientTool
{
    public const string LanguageParameter = "language";

    public string Name => "translate_document";

    public string Description =>
        "Translates a whole markdown document into another language, keeping to the way it is written and giving its names and " +
        "terms the same way throughout; its markup, code and diagrams stay as they are. The translation opens as a new document. " +
        "Runs in the background; a long document takes a while. For a short passage, translate it yourself instead.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        DocumentJob.Document,
        new(LanguageParameter, "The language to translate it into, named in English: French, Japanese, Brazilian Portuguese…"),
        new("path", GenerationTools.SavedWhere, Required: false),
    ];

    public ToolSafety Safety => ToolSafety.RequiresApproval;

    public bool Parallelizable => false;

    public ToolCallAnnouncement? Announce(JsonObject arguments)
    {
        var named = DocumentJob.Named(arguments);
        var language = Language(arguments) ?? string.Empty;

        return new(Str.Get("Shell.Documents.Announce.Translate"), Str.Format("Shell.Documents.Announce.IntoFormat", named, language),
                   Str.Format("Shell.Documents.Announce.TranslatingFormat", named, language),
                   Str.Get("Shell.Documents.Announce.TranslationMade"), Str.Get("Shell.Documents.Announce.NoTranslation"),
                   Str.Get("Shell.Documents.Announce.InBackground"));
    }

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (Language(arguments) is not { } language)
            return Task.FromResult(ToolResult.Error("no language", $"Give the '{LanguageParameter}' to translate it into."));

        return job.RunAsync(arguments, DocumentWorkKind.Translation, language, name => Str.Format("Shell.Documents.TranslationNameFormat", name, language) + ".md",
                            [DocumentGistPass.Shelf, DocumentGlossaryPass.Shelf, DocumentTranslator.ShelfFor(language)],
                            async work =>
                            {
                                var gist = await new DocumentGistPass(work.Model, work.Notes, work.Progress).SayAsync(work.Reading, ct);
                                var glossary = await new DocumentGlossaryPass(work.Model, work.Notes, work.Progress).GatherAsync(work.Reading, gist, language, ct);

                                var translation = await new DocumentTranslator(work.Model, work.Notes, work.Progress)
                                    .TranslateAsync(work.Reading, gist, glossary, language, Str.Get("Shell.Documents.Untranslated"), ct);

                                return new DocumentMade(translation.Text, translation.Untranslated.Count == 0 ? null
                                    : "These parts are kept in the document's own language, a note before each, as no usable translation of them was had: "
                                      + string.Join("; ", translation.Untranslated.Select(where => $"“{where}”")) + ". Translating it again tries them again.");
                            },
                            ct);
    }

    private static string? Language(JsonObject arguments) => ToolArgs.Str(arguments, LanguageParameter) is { Length: > 0 } said ? said.Trim() : null;

    /// <summary>A whole document takes minutes or hours: it is translated in the background.</summary>
    public bool RunsInBackground(JsonObject arguments) => true;
}
