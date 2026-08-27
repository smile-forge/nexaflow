using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.VirtualDisk.FileActions;

/// <summary>Opens a disk image in the "As Disk" inspector tab. Matched to disk images via the <c>/disk</c>
/// experience (see default-filemap.json). Mountable images (iso/vhd/vhdx) map to the child
/// <c>/disk/mountable</c> experience, which still satisfies <c>/disk</c>, so this action offers on all of them.</summary>
public sealed class OpenAsDiskAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/disk";
    public string ExperienceId => "/disk";
    public string ExperienceDescription => "Virtual disk image";

    public string DisplayName => "As Disk";
    public string Icon => "💽";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public bool PerformAction(string filePath)
    {
        shell.OpenTab(VirtualDiskTabRegistration.StaticPageKind,
            new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    // SupportsMultipleFiles is false, so a selection opens the first image and nothing else — this used to
    // open a tab per file (contradicting the flag the file browser reads) and to return true for an empty
    // selection, flashing the action strip's success tick over nothing.
    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var first = filePaths.FirstOrDefault();
        return first is not null && PerformAction(first);
    }
}
