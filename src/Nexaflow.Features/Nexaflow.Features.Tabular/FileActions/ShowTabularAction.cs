using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Tabular.FileActions;

public sealed class ShowTabularAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowTabularAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "▦";
    public string DisplayName            => "As Table";
    public static string? StaticExperienceId => "/tabular";
    public string ExperienceId           => "/tabular";
    public string ExperienceDescription  => "Tabular viewer for CSV / TSV files";
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("Tabular", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Tabular", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}
