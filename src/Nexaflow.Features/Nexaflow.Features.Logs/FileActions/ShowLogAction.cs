using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Logs.FileActions;

/// <summary>
/// File-tree context action that opens a log file in a new Logs tab.
/// </summary>
public sealed class ShowLogAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowLogAction(IShellServices shellServices) => _shellServices = shellServices;

    public string DisplayName           => Str.Get("Logs.Action.AsLog");
    public string Icon                  => "📋";
    public static string? StaticExperienceId => "/text/log";
    public string ExperienceId          => "/text/log";
    public string ExperienceDescription => Str.Get("Logs.Action.AsLogDescription");
    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   OpensViewer           => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Logs", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Logs", new Dictionary<string, string> { ["path"] = path });
            return Task.FromResult(true); // open first file only (SupportsMultipleFiles = false)
        }
        return Task.FromResult(false);
    }
}
