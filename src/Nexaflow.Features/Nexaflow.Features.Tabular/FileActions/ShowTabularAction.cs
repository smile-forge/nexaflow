using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Tabular.FileActions;

public sealed class ShowTabularAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowTabularAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "▦";
    public string DisplayName            => Str.Get("Tabular.Action.AsTable");
    public static string? StaticExperienceId => "/tabular";
    public string ExperienceId           => "/tabular";
    public string ExperienceDescription  => Str.Get("Tabular.Action.ExperienceDescription");
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Tabular", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Tabular", new Dictionary<string, string> { ["path"] = path });
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
