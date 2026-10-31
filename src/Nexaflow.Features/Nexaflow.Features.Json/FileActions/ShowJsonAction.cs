using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Json.FileActions;

public sealed class ShowJsonAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowJsonAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public IconRef Icon                 => IconRef.Emoji("{}");
    public string DisplayName           => Str.Get("Json.Action.AsJson");
    public static string? StaticExperienceId => "/text/json";
    public string ExperienceId          => "/text/json";
    public string ExperienceDescription => Str.Get("Json.Action.AsJsonDescription");
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   OpensViewer           => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Json", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Json", new Dictionary<string, string> { ["path"] = path });
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
