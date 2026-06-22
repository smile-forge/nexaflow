using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.Text.FileActions;

/// <summary>
/// Opens a file in the read-only raw text viewer with the split panel pre-opened, so a file that's too
/// large to edit can be broken into smaller, editable pieces.
/// </summary>
public sealed class SplitFileAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public SplitFileAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "⧉";
    public string DisplayName            => "Split File…";
    public static string? StaticExperienceId => "/text";
    public string ExperienceId           => "/text";
    public string ExperienceDescription  => "Split a large text file into smaller files";
    public bool   RequiresRefresh        => true;   // new part files appear next to the source
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => false;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("Text", new Dictionary<string, string> { ["path"] = filePath, ["split"] = "1" });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Text", new Dictionary<string, string> { ["path"] = path, ["split"] = "1" });
            return true;
        }
        return false;
    }
}
