using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Font.FileActions;

/// <summary>Opens font file(s) in the Font viewer tab. A multi-selection opens ONE tab comparing all the
/// chosen fonts (not one tab each). Matched to font files via the <c>/font</c> experience (see
/// default-filemap.json).</summary>
public sealed class OpenAsFontAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/font";
    public string ExperienceId => "/font";
    public string ExperienceDescription => "Font file";

    public string DisplayName => "As Font";
    public string Icon => "🔤";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => true;
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public bool PerformAction(string filePath) => Open([filePath]);

    public bool PerformAction(IEnumerable<string> filePaths) => Open(filePaths);

    private bool Open(IEnumerable<string> filePaths)
    {
        var fonts = filePaths.Where(FontFileTypes.IsFont).ToList();
        if (fonts.Count == 0) return false;
        shell.OpenTab(FontTabRegistration.StaticPageKind,
            new Dictionary<string, string> { ["paths"] = string.Join('|', fonts) });
        return true;
    }
}
