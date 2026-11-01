using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Rtf.FileActions;

/// <summary>
/// Opens a rich-text document in the RTF page. The bundled file-type map points <c>/document/rtf</c> at <c>*.rtf</c>, so this
/// is what a double-click does. One document per tab — opening several files opens the first.
/// </summary>
public sealed class ShowRtfAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/document/rtf";
    public string ExperienceId => "/document/rtf";
    public string ExperienceDescription => Str.Get("Rtf.Action.AsRtfDescription");

    public string DisplayName => Str.Get("Rtf.Action.AsRtf");
    public IconRef Icon => IconRef.Emoji("📝");
    public string? Tooltip => Str.Get("Rtf.Action.AsRtfTip");

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool CanPerformAction => true;
    public bool RequiresRefresh => false;

    /// <summary>Opens an internal viewer tab, so the "Define New" wizard lists it as a viewer target.</summary>
    public bool OpensViewer => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        shell.OpenTab(RtfTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default) => filePaths.FirstOrDefault() is { } path ? PerformActionAsync(path, ct) : Task.FromResult(false);
}
