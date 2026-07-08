using Nexaflow.Features.Common;

namespace Nexaflow.Features.Json.FileActions;

public sealed class ShowJsonAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowJsonAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public string Icon                  => "{}";
    public string DisplayName           => "As Json";
    public static string? StaticExperienceId => "/text/json";
    public string ExperienceId          => "/text/json";
    public string ExperienceDescription => "JSON file viewer and editor";
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   OpensViewer           => true;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("Json", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Json", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}
