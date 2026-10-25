using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Hex.FileActions;

/// <summary>
/// Opens any binary file in the hex editor tab.
/// Experience "/binary" matches all file types mapped to the binary experience
/// (e.g. executables, firmware, unknown extensions).
/// </summary>
public sealed class ShowBinaryAction : IFileAction, ICacheable
{
    private readonly IShellServices _shell;

    public ShowBinaryAction(IShellServices shell) => _shell = shell;

    public static string? StaticExperienceId => "/binary";
    public string ExperienceId          => "/binary";
    public string ExperienceDescription => Str.Get("Hex.Action.OpenInHexEditor");
    public string DisplayName           => Str.Get("Hex.Action.AsHex");
    public string Icon                  => "⬡";
    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;
    public bool   OpensViewer           => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shell.OpenTab("Hex", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    => filePaths.FirstOrDefault() is { } first ? PerformActionAsync(first, ct) : Task.FromResult(false);
}
