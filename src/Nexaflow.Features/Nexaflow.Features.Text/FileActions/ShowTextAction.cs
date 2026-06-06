using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.Text.FileActions;

/// <summary>
/// Opens a text (.txt) file in a new TextView tab.
/// </summary>
public sealed class ShowTextAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowTextAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "📄";
    public string DisplayName            => "As Text";
    public static string? StaticExperienceId => "/text";
    public string ExperienceId           => "/text";
    public string ExperienceDescription  => "Lightweight Text editor";
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("Text", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Text", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}
