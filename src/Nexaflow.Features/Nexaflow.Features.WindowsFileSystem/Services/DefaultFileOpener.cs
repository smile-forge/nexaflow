using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Features.WindowsFileSystem.Model;

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
    private readonly IVirtualFileSystem _files;
    private readonly ShellPath _paths;

    public DefaultFileOpener(FileSystemFeatureRegistry registry)
    {
        _actions = new FileActionManager(registry);
        _files   = registry.Vfs;
        _paths   = new ShellPath(_files);
    }

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
    /// Works for any path — in an archive, on a location — because only its name is matched.
    /// </summary>
    public static bool ExpandsInPlace(string path)
    {
        // Extension level and above only, so the file's bytes are never asked for.
        var file = FileSubject.ByName(path);
        return BrowseInPlaceExperiences.Any(exp =>
            FileMapManager.Instance.GetMatchSpecificity(file, exp) >= 4);
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
        var file     = FileSubject.Of(_actions.Vfs, filePath);
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
        var overrideEntry = DefaultActionRegistry.Instance.Resolve(filePath);
        if (overrideEntry is not null &&
            await TryPerformOverrideAsync(overrideEntry, filePath, internalActions) is { Handled: true } overridden)
            return overridden.RequiresRefresh;

        var (bestInternal, bestInternalSpec) = MostSpecific(file, internalActions);

        // Shell "open" verb is Extension-level (4); encode priority as spec*2 + (internal?1:0)
        // so that internal wins over shell at the same specificity level.
        int internalPriority = bestInternal  is not null ? bestInternalSpec * 2 + 1 : -1;
        int shellPriority    = shellOpenVerb is not null ? 4 * 2 + 0               : -1;

        if (internalPriority >= shellPriority && bestInternal is not null)
        {
            // Deliberately unguarded: this class holds no shell handle, so it cannot report anything. The
            // caller (FileSystemViewModel.OpenFileDefaultAsync) catches and names the file — see the note
            // there for why a throw must not be allowed to reach the dispatcher.
            await bestInternal.PerformActionAsync(filePath);
            return bestInternal.RequiresRefresh;
        }

        if (shellOpenVerb is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(await _paths.RealizeAsync(filePath))
                {
                    Verb            = shellOpenVerb.Verb,
                    UseShellExecute = true
                });
            }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// Shows <paramref name="filePath"/> in the viewer for its type: the user's default where that is a viewer, else
    /// the most specific viewer that takes it. Never the shell's verb nor an external app — this is looking, and what is
    /// looked at may be a script out of a web page (<see cref="FileViewRequest"/>). A type nothing is mapped to — a
    /// script, whose only action is to run it — is shown as text or as bytes, whichever it is. False where no viewer
    /// takes it.
    /// </summary>
    public async Task<bool> OpenInViewerAsync(string filePath)
    {
        var entry      = new FileSystemEntry { Name = Path.GetFileName(filePath), FullPath = filePath, IsDirectory = false };
        var file       = FileSubject.Of(_actions.Vfs, filePath);
        var canPerform = _actions.SnapshotCanPerform();

        var viewers = await Task.Run(() =>
            _actions.FilterActions([entry], canPerform.File).Where(action => action.OpensViewer).ToList());

        var chosen = DefaultActionRegistry.Instance.Resolve(filePath) is { Kind: DefaultActionKind.InternalViewer } preferred
            ? viewers.FirstOrDefault(v => string.Equals(v.ExperienceId, preferred.TargetId, System.StringComparison.OrdinalIgnoreCase))
            : null;
        chosen ??= MostSpecific(file, viewers).Action ?? await ViewerForContentAsync(filePath);
        if (chosen is null) return false;

        await chosen.PerformActionAsync(filePath);
        return true;
    }

    /// <summary>The file map's two roots every file is one of, for a file whose type is mapped to nothing.</summary>
    private const string TextExperience = "/text", BytesExperience = "/binary";

    /// <summary>The viewer of text or of bytes, by what the file's start holds.</summary>
    private async Task<IFileAction?> ViewerForContentAsync(string filePath)
    {
        var head = new byte[EncodingDetector.TextSniffBytes];
        int read;
        await using (var stream = await _actions.Vfs.OpenReadAsync(filePath, ReadIntent.Sequential))
            read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false);

        var experience = EncodingDetector.HoldsText(head[..read]) ? TextExperience : BytesExperience;
        return _actions.ViewerOf(experience);
    }

    /// <summary>
    /// The highest match-specificity, ties broken by the deeper experience so the specific viewer wins over a broader
    /// ancestor (e.g. "As Code"/"As Markdown" at "/text/…" beat "As Text" at "/text", which the file also matches at
    /// extension level via ancestor propagation).
    /// </summary>
    private static (IFileAction? Action, int Specificity) MostSpecific(FileSubject file, IEnumerable<IFileAction> actions)
    {
        IFileAction? best = null;
        int bestSpec = -1, bestDepth = -1;
        foreach (var action in actions)
        {
            int spec  = FileMapManager.Instance.GetMatchSpecificity(file, action.ExperienceId);
            int depth = ExperienceDepth(action.ExperienceId);
            if (spec > bestSpec || (spec == bestSpec && depth > bestDepth))
                (best, bestSpec, bestDepth) = (action, spec, depth);
        }
        return (best, bestSpec);
    }

    /// <summary>
    /// Runs an explicit Default Actions override. Not handled (so the caller falls back to automatic resolution) when the
    /// target no longer applies: a removed viewer/app or a shell verb that failed.
    /// </summary>
    private async Task<(bool Handled, bool RequiresRefresh)> TryPerformOverrideAsync(
        DefaultActionOverride ov, string filePath, IReadOnlyList<IFileAction> internalActions)
    {
        switch (ov.Kind)
        {
            case DefaultActionKind.InternalViewer:
                var action = internalActions.FirstOrDefault(a =>
                    string.Equals(a.ExperienceId, ov.TargetId, System.StringComparison.OrdinalIgnoreCase));
                if (action is null) return (false, false);
                await action.PerformActionAsync(filePath);
                return (true, action.RequiresRefresh);

            case DefaultActionKind.ExternalApp:
                var def = ExternalAppRegistry.Instance.FindById(ov.TargetId);
                if (def is null) return (false, false);
                await new CustomAction(def, _files).PerformActionAsync(filePath);
                return (true, false);

            case DefaultActionKind.WindowsVerb:
                try
                {
                    Process.Start(new ProcessStartInfo(await _paths.RealizeAsync(filePath)) { Verb = ov.TargetId, UseShellExecute = true });
                    return (true, false);
                }
                catch { return (false, false); }

            default:
                return (false, false);
        }
    }

    /// <summary>Number of path segments in a hierarchical experience id ("/text/code" → 2, "/text" → 1,
    /// "/" → 0) — the tie-breaker that lets a specific viewer win over a broader ancestor.</summary>
    private static int ExperienceDepth(string experienceId) =>
        experienceId.Trim('/').Split('/', System.StringSplitOptions.RemoveEmptyEntries).Length;
}
