using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// Universal "Open With…" action — shows the Windows <c>SHOpenWithDialog</c> picker for the
/// selected file so the user can choose any installed app. Applies to every file (experience
/// "/") and is never the double-click default (universal specificity sits below any viewer or
/// shell verb), so it only ever surfaces as an explicit action button / context-menu item.
/// </summary>
public sealed class OpenWithAction : IFileAction, ICacheable
{
    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public string Icon                  => "↗";
    public string DisplayName           => "Open With";
    public static string? StaticExperienceId => "/";
    public string ExperienceId          => "/";
    public string ExperienceDescription => "All files";
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   ShowsSuccessTick      => false;   // a chooser, not a completed operation
    public string? Tooltip              => "Choose an app to open this file";

    public bool PerformAction(string filePath) => NativeMethods.ShowOpenWithDialog(filePath);

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var first = filePaths.FirstOrDefault();
        return first is not null && NativeMethods.ShowOpenWithDialog(first);
    }
}
