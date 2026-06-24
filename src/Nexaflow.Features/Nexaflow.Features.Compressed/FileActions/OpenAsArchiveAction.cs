using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Compressed.FileActions;

/// <summary>Opens an archive file in the Compressed inspector tab. Matched to archive files via the
/// <c>/archive</c> experience (see default-filemap.json).</summary>
public sealed class OpenAsArchiveAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/archive";
    public string ExperienceId => "/archive";
    public string ExperienceDescription => "Compressed archive";

    public string DisplayName => "As Archive";
    public string Icon => "📦";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public bool PerformAction(string filePath)
    {
        shell.OpenTab(CompressedTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths) PerformAction(path);
        return true;
    }
}
