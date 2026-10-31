using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    public IconRef Icon                  => IconRef.Emoji("⟨⟩");
    public string DisplayName            => Str.Get("Code.Action.AsCode");
    public static string? StaticExperienceId => "/text/code";
    public string ExperienceId           => "/text/code";
    public string ExperienceDescription  => Str.Get("Code.Action.AsCodeDescription");
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Code", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Code", new Dictionary<string, string> { ["path"] = path });
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
