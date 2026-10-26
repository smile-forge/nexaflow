using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Executable.FileActions;

/// <summary>
/// "Inspect" — opens a Windows binary in the PE inspector.
/// <para>
/// Claimed through <c>OptionalExtension</c> criteria in the bundled filemap, which is deliberate:
/// the action appears on the file-list action strip and can be set as an explicit per-extension
/// default, but it is hidden from the right-click menu and never wins the automatic double-click
/// decision. Double-clicking an <c>.exe</c> therefore still runs it.
/// </para>
/// </summary>
public sealed class InspectPeAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/binary/pe";

    public string ExperienceId => "/binary/pe";
    public string ExperienceDescription =>
        Str.Get("Executable.Action.InspectDescription");

    public string DisplayName => Str.Get("Executable.Inspector.Inspect");
    public string Icon        => "🔬";
    public string? Tooltip    => "Inspect this binary's PE structure";

    public bool IsDestructive         => false;
    public bool SupportsMultipleFiles => false;
    public bool RequiresRefresh       => false;
    public bool CanPerformAction      => true;
    public bool OpensViewer           => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        shell.OpenTab(ExecutableTabRegistration.StaticPageKind,
                      new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths) return PerformActionAsync(path, ct);
        return Task.FromResult(false);
    }
}
