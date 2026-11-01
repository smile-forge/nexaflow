using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Icons;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// Wraps an <see cref="IFolderAction"/> as an <see cref="IFileAction"/> so that
/// folder actions can be placed in the same <c>ObservableCollection&lt;FileActionViewModel&gt;</c>
/// without changing the view layer. The <see cref="ExperienceId"/> is empty — folder actions
/// are matched structurally, not via the file mapping index.
/// </summary>
public sealed class FolderActionAdapter : IFileAction
{
    public IFolderAction Inner { get; }

    public FolderActionAdapter(IFolderAction inner) => Inner = inner;

    public bool         IsDestructive         => Inner.IsDestructive;
    public bool         SupportsMultipleFiles => Inner.SupportsMultipleFiles;
    public IconRef      Icon                  => Inner.Icon;
    public string       DisplayName           => Inner.DisplayName;

    public string       AutomationId          => Inner.AutomationId;
    public static string? StaticExperienceId  => null;
    public string       ExperienceId          => string.Empty;
    public string       ExperienceDescription => string.Empty;
    public bool         RequiresRefresh       => Inner.RequiresRefresh;
    public bool         CanPerformAction      => Inner.CanPerformAction;
    public ShellIconRef? IconResource         => Inner.IconResource;
    public string?      Tooltip               => Inner.Tooltip;
    public bool         IsRibbonPinnable      => Inner.IsRibbonPinnable;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => Inner.PerformActionAsync(filePath, ct);

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default) => Inner.PerformActionAsync(filePaths, ct);

    public Task<bool> PerformActionAsync(string filePath, bool force, CancellationToken ct = default) => Inner.PerformActionAsync(filePath, force, ct);

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, bool force, CancellationToken ct = default) => Inner.PerformActionAsync(filePaths, force, ct);

    /// <summary>
    /// Persists the inner action's type name so <c>FeatureManager.FindFileAction</c>
    /// can reconstruct this adapter on ribbon-button click.
    /// </summary>
    public Dictionary<string, string>? GetReinitParams() =>
        new() { ["innerType"] = Inner.GetType().FullName! };
}
