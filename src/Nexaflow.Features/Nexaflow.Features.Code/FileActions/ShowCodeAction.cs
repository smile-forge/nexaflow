using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.Code.FileActions;

/// <summary>
/// Opens a file in the "As Code" editor: a syntax-highlighted, foldable, editable view with a side panel of
/// code intelligence (dependency links + a clickable class diagram). The right context for working with a
/// file <i>as code</i> — distinct from "As Text" (the read-only raw viewer).
/// </summary>
public sealed class ShowCodeAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowCodeAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "⟨⟩";
    public string DisplayName            => "As Code";
    public static string? StaticExperienceId => "/text/code";
    public string ExperienceId           => "/text/code";
    public string ExperienceDescription  => "Editable code editor with syntax highlighting and a structure panel";
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("Code", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Code", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}
