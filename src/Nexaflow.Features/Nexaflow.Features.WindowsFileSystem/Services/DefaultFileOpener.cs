using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
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

    /// <summary>The bundled experiences whose formats browse <b>in place</b> as a folder: real archives
    /// (<c>/archive</c>) and virtual-disk images (<c>/disk</c>, and its descendants like
    /// <c>/disk/mountable</c>) — see default-filemap.json.</summary>
    private static readonly string[] BrowseInPlaceExperiences = ["/archive", "/disk"];

    /// <summary>
    /// Whether a browsable container should open <b>in place</b> (browse its contents as a folder) rather
    /// than in its own viewer, by default. True only when a browse-in-place experience is the container's
    /// <i>extension-level</i> experience — a real archive (<c>.zip</c>, <c>.tar.gz</c>, <c>.7z</c>) or a disk
    /// image (<c>.vhd</c>, <c>.iso</c>, …). A format mapped to <c>/archive</c> only by
    /// <see cref="FileActions.CriteriaType.OptionalExtension"/> (<c>.docx</c>, <c>.odt</c>, <c>.epub</c>) — or
    /// not mapped there at all (<c>.eml</c>) — keeps its own viewer, so <see cref="GetMatchSpecificity"/>
    /// scores it below the extension level (4) and this is false. A disk's secondary actions ("As Disk",
    /// "Mount") stay on the action strip / context menu, exactly as "As Archive" does for an archive.
    /// Works for a virtual (in-archive) path too: <see cref="FileInfo"/> yields the name/extension without
    /// the file existing, and the magic-number probe inside <see cref="FileMapManager.GetMatchSpecificity"/>
    /// is guarded by <c>file.Exists</c>.
    /// </summary>
    public static bool ExpandsInPlace(string path)
    {
        var fi = new FileInfo(path);
        return BrowseInPlaceExperiences.Any(exp =>
            FileMapManager.Instance.GetMatchSpecificity(fi, exp) >= 4);
    }

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

        // An explicit per-extension default (Options → Default Actions) wins over automatic resolution.
        // A stale override (removed viewer/app, failed verb) returns false so we fall through below.
        var overrideEntry = DefaultActionRegistry.Instance.Resolve(fileInfo);
        if (overrideEntry is not null &&
            TryPerformOverride(overrideEntry, filePath, internalActions, out bool overrideRefresh))
            return overrideRefresh;

        // Pick the highest match-specificity; break ties by the deeper experience so the specific viewer
        // wins over a broader ancestor (e.g. "As Code"/"As Markdown" at "/text/…" beat "As Text" at "/text",
        // which the file also matches at extension level via ancestor propagation).
        IFileAction? bestInternal      = null;
        int          bestInternalSpec  = -1;
        int          bestInternalDepth = -1;
        foreach (var action in internalActions)
        {
            int spec  = FileMapManager.Instance.GetMatchSpecificity(fileInfo, action.ExperienceId);
            int depth = ExperienceDepth(action.ExperienceId);
            if (spec > bestInternalSpec || (spec == bestInternalSpec && depth > bestInternalDepth))
            {
                bestInternal      = action;
                bestInternalSpec  = spec;
                bestInternalDepth = depth;
            }
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
                Process.Start(new ProcessStartInfo(RealizeForShell(filePath))
                {
                    Verb            = shellOpenVerb.Verb,
                    UseShellExecute = true
                });
            }
            catch { }
        }
        return false;
    }

    /// <summary>A real, launchable path for the shell. A virtual (in-archive) path — e.g. a PDF opened from
    /// an email or a zip — has no real file, so <see cref="Process.Start(ProcessStartInfo)"/> would silently
    /// fail; materialise it to a temp copy first. Real paths pass straight through.</summary>
    private static string RealizeForShell(string path)
    {
        if (File.Exists(path)) return path;
        try { return VirtualFileSystem.Instance.MaterializeFile(path); }
        catch { return path; }
    }

    /// <summary>
    /// Runs an explicit Default Actions override. Returns false (so the caller falls back to automatic
    /// resolution) when the target no longer applies: a removed viewer/app or a shell verb that failed.
    /// </summary>
    private static bool TryPerformOverride(
        DefaultActionOverride ov, string filePath,
        IReadOnlyList<IFileAction> internalActions, out bool requiresRefresh)
    {
        requiresRefresh = false;
        switch (ov.Kind)
        {
            case DefaultActionKind.InternalViewer:
                var action = internalActions.FirstOrDefault(a =>
                    string.Equals(a.ExperienceId, ov.TargetId, System.StringComparison.OrdinalIgnoreCase));
                if (action is null) return false;
                action.PerformAction(filePath);
                requiresRefresh = action.RequiresRefresh;
                return true;

            case DefaultActionKind.ExternalApp:
                var def = ExternalAppRegistry.Instance.FindById(ov.TargetId);
                if (def is null) return false;
                new CustomAction(def, ExternalAppRegistry.ResolveIcon(def)).PerformAction(RealizeForShell(filePath));
                return true;

            case DefaultActionKind.WindowsVerb:
                try
                {
                    Process.Start(new ProcessStartInfo(RealizeForShell(filePath)) { Verb = ov.TargetId, UseShellExecute = true });
                    return true;
                }
                catch { return false; }

            default:
                return false;
        }
    }

    /// <summary>Number of path segments in a hierarchical experience id ("/text/code" → 2, "/text" → 1,
    /// "/" → 0) — the tie-breaker that lets a specific viewer win over a broader ancestor.</summary>
    private static int ExperienceDepth(string experienceId) =>
        experienceId.Trim('/').Split('/', System.StringSplitOptions.RemoveEmptyEntries).Length;
}
