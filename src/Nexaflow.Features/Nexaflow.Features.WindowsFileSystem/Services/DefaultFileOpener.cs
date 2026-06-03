using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Resolves and executes the default "open" action for a file — the logic behind
/// double-clicking a file in the file list, extracted so it can be reused outside the
/// <see cref="FileSystemViewModel"/> (e.g. the cross-feature <see cref="FileSystemObjectHandler"/>).
///
/// Applies the rule: FileExtension &gt; MagicNumber &gt; PerceivedType &gt; ContentType, and at the
/// same specificity level an internal action beats a shell "open" verb. Holds no UI state —
/// <see cref="FileMapManager"/> and <see cref="ShellTypeResolver"/> are process singletons.
/// </summary>
public sealed class DefaultFileOpener
{
    private readonly FileActionManager _actions;

    public DefaultFileOpener(FileSystemFeatureRegistry registry)
        => _actions = new FileActionManager(registry);

    /// <summary>
    /// Opens <paramref name="filePath"/> with its highest-specificity internal action, else the
    /// shell "open" verb. Returns true when the caller should refresh its view (the chosen
    /// internal action mutated the file system). Must be called on the STA UI thread
    /// (<see cref="FileActionManager.SnapshotCanPerform"/> touches the OLE clipboard).
    /// </summary>
    public async Task<bool> OpenAsync(string filePath)
    {
        var entry    = new FileSystemEntry { Name = Path.GetFileName(filePath), FullPath = filePath, IsDirectory = false };
        var fileInfo = new FileInfo(filePath);
        var ext      = Path.GetExtension(filePath);

        var canPerform = _actions.SnapshotCanPerform();

        var (internalActions, shellOpenVerb) = await Task.Run(() =>
        {
            var actions  = _actions.FilterActions([entry], canPerform.File);
            var typeInfo = ShellTypeResolver.Resolve(ext);
            var openVerb = typeInfo?.Verbs.FirstOrDefault(v =>
                string.Equals(v.Verb, "open", System.StringComparison.OrdinalIgnoreCase));
            return (actions, openVerb);
        });

        IFileAction? bestInternal     = null;
        int          bestInternalSpec = -1;
        foreach (var action in internalActions)
        {
            int spec = FileMapManager.Instance.GetMatchSpecificity(fileInfo, action.ExperienceId);
            if (spec > bestInternalSpec) { bestInternal = action; bestInternalSpec = spec; }
        }

        // Shell "open" verb is Extension-level (4); encode priority as spec*2 + (internal?1:0)
        // so that internal wins over shell at the same specificity level.
        int internalPriority = bestInternal  is not null ? bestInternalSpec * 2 + 1 : -1;
        int shellPriority    = shellOpenVerb is not null ? 4 * 2 + 0               : -1;

        if (internalPriority >= shellPriority && bestInternal is not null)
        {
            bestInternal.PerformAction(filePath);
            return bestInternal.RequiresRefresh;
        }

        if (shellOpenVerb is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(filePath)
                {
                    Verb            = shellOpenVerb.Verb,
                    UseShellExecute = true
                });
            }
            catch { }
        }
        return false;
    }
}
