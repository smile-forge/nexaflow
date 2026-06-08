using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Windows.Media;

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
    public string       Icon                  => Inner.Icon;
    public string       DisplayName           => Inner.DisplayName;
    public static string? StaticExperienceId  => null;
    public string       ExperienceId          => string.Empty;
    public string       ExperienceDescription => string.Empty;
    public bool         RequiresRefresh       => Inner.RequiresRefresh;
    public bool         CanPerformAction      => Inner.CanPerformAction;
    public ImageSource? IconImage             => Inner.IconImage;
    public string?      Tooltip               => Inner.Tooltip;
    public bool         IsRibbonPinnable      => Inner.IsRibbonPinnable;

    public bool PerformAction(string filePath)
        => Inner.PerformAction(filePath);

    public bool PerformAction(IEnumerable<string> filePaths)
        => Inner.PerformAction(filePaths);

    public bool PerformAction(string filePath, bool force)
        => Inner.PerformAction(filePath, force);

    public bool PerformAction(IEnumerable<string> filePaths, bool force)
        => Inner.PerformAction(filePaths, force);

    /// <summary>
    /// Persists the inner action's type name so <c>FeatureManager.FindFileAction</c>
    /// can reconstruct this adapter on ribbon-button click.
    /// </summary>
    public Dictionary<string, string>? GetReinitParams() =>
        new() { ["innerType"] = Inner.GetType().FullName! };
}
