using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    public string DisplayName            => Str.Get("Text.Viewer.Action.AsText");
    public static string? StaticExperienceId => "/text";
    public string ExperienceId           => "/text";
    public string ExperienceDescription  => Str.Get("Text.Viewer.Action.AsTextDescription");
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Text", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Text", new Dictionary<string, string> { ["path"] = path });
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
