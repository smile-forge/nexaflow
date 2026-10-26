using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Compressed.FileActions;

/// <summary>Opens an archive file in the Compressed inspector tab. Matched to archive files via the
/// <c>/archive</c> experience (see default-filemap.json).</summary>
public sealed class OpenAsArchiveAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/archive";
    public string ExperienceId => "/archive";
    public string ExperienceDescription => Str.Get("Compressed.Experience.Archive");

    public string DisplayName => Str.Get("Compressed.Action.AsArchive");
    public string Icon => "📦";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        shell.OpenTab(CompressedTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    // SupportsMultipleFiles is false, so a selection opens the first archive and nothing else — this used to
    // open a tab per file (contradicting the flag the file browser reads) and to return true for an empty
    // selection, flashing the action strip's success tick over nothing.
    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    => filePaths.FirstOrDefault() is { } first ? PerformActionAsync(first, ct) : Task.FromResult(false);
}
