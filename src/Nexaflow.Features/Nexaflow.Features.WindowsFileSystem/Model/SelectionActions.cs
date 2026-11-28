using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;

using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Model;

/// <summary>What the actions for a selection come to: the actions in the order they are offered, whether "New" goes first,
/// and the paths they act on.</summary>
/// <param name="Actions">The built-in actions — "Open With" last of them — then the shell's own verbs for the file's type,
/// then the user's external apps.</param>
/// <param name="OffersNew">Nothing is selected in a folder items can be made in, so "New" is offered before the rest.</param>
/// <param name="Targets">The selected paths, or the open folder when nothing is selected.</param>
public sealed record SelectionActionSet(IReadOnlyList<IFileAction> Actions, bool OffersNew, IReadOnlyList<string> Targets);

/// <summary>
/// Which actions apply to what is selected — files, folders, This PC's places or nothing at all (the open folder itself) —
/// for the action strip and for a right-click menu.
/// </summary>
public sealed class SelectionActions
{
    private readonly FolderLocation _location;
    private readonly FileActionManager _actions;
    private readonly IVirtualFileSystem _vfs;

    public SelectionActions(FolderLocation location, FileActionManager actions, IVirtualFileSystem vfs)
    {
        _location = location;
        _actions  = actions;
        _vfs      = vfs;
    }

    /// <summary>
    /// The actions the strip offers for <paramref name="selected"/>. Asked on the UI thread, whose state the actions read
    /// whether they can act; the filtering, the shell's verbs and the external apps are worked out off it.
    /// </summary>
    public async Task<SelectionActionSet> ForStripAsync(IReadOnlyList<FileSystemEntry> selected)
    {
        // Snapshot CanPerformAction here, on the UI thread whose state the actions read.
        var canPerform = _actions.SnapshotCanPerform();

        bool useFolderActions = UsesFolderActions(selected);
        var  currentPath = _location.CurrentPath;   // captured for the background filter (open-folder gating)
        var  inContainer = _location.InContainer;

        // Filter built-in actions + resolve shell verbs + custom external apps — all pure background work.
        var (applicable, shellVerbs, customActions) = await Task.Run(() =>
        {
            IReadOnlyList<IFileAction> builtIn = useFolderActions
                ? FilterForVirtual(_actions.FilterFolderActions(selected, canPerform.Folder, currentPath), inContainer)
                : _actions.FilterActions(selected, canPerform.File);

            // Only look up shell verbs for a single-file selection, and only when the
            // user has enabled registry-based file type mapping.
            List<ShellVerbAction> verbs = [];
            if (ExternalAppRegistry.Instance.UseRegistryMapping &&
                !useFolderActions && selected.Count == 1 && !selected[0].IsDirectory)
            {
                var entry = selected[0];
                var ext   = Path.GetExtension(entry.Name);
                var info  = ShellTypeResolver.Resolve(ext);
                if (info is not null)
                {
                    string experienceId = $"/shell/{ext.TrimStart('.').ToLowerInvariant()}";
                    foreach (var verb in info.Verbs)
                    {
                        // Deduplicate: skip if a built-in already handles the same verb display name
                        if (builtIn.Any(a => string.Equals(a.DisplayName, verb.FriendlyName,
                                                            StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var icon = ShellIconRef.Parse(verb.DefaultIconSpec);

                        // Tooltip: prefer the verb's own resolved description, then the type description
                        string? tooltip = verb.Tooltip
                            ?? (string.IsNullOrEmpty(info.ProgIdDescription) ? null : info.ProgIdDescription);

                        verbs.Add(new ShellVerbAction(
                            _vfs,
                            verb.Verb,
                            verb.FriendlyName,
                            verb.Command,
                            experienceId,
                            icon,
                            tooltip));
                    }
                }
            }

            // User-defined external apps — apply to file selections regardless of count
            // (the registry filters out single-file-only definitions on multi-select).
            IReadOnlyList<CustomAction> customs = !useFolderActions
                ? ExternalAppRegistry.Instance.Resolve(selected, _vfs)
                : Array.Empty<CustomAction>();

            return (builtIn, verbs, customs);
        });

        // Built-in actions first, but keep "Open With" as the last built-in — it sits just
        // above the shell-handler list as the gateway to "any other app".
        var ordered = new List<IFileAction>(applicable.Count + shellVerbs.Count + customActions.Count);
        ordered.AddRange(applicable.Where(a => a is not OpenWithAction));
        ordered.AddRange(applicable.Where(a => a is OpenWithAction));
        ordered.AddRange(shellVerbs);
        ordered.AddRange(customActions);

        // "New" — the first item when an empty folder area is in focus (no file selected).
        bool offersNew = useFolderActions && selected.Count == 0 && _location.CanCreateHere;
        return new SelectionActionSet(ordered, offersNew, TargetsOf(selected));
    }

    /// <summary>
    /// The actions a right-click menu offers for <paramref name="entries"/> — "Open With" last. Secondary capabilities (an
    /// OptionalExtension experience, like inspecting a .docx as an archive) stay on the strip and off the menu.
    /// </summary>
    public IReadOnlyList<IFileAction> ForMenu(IReadOnlyList<FileSystemEntry> entries)
    {
        // On This PC there is no open folder, so an empty selection has no target and the menu is empty.
        // Anything that WAS clicked is a target though — a drive in the list, or a folder in the directory
        // tree, which builds its own entry and is not restricted to what the list happens to be showing.
        if (_location.IsThisPc && entries.Count == 0) return [];

        var canPerform = _actions.SnapshotCanPerform();
        IReadOnlyList<IFileAction> applicable = UsesFolderActions(entries)
            ? FilterForVirtual(_actions.FilterFolderActions(entries, canPerform.Folder, _location.CurrentPath), _location.InContainer)
            : _actions.FilterActions(entries, canPerform.File, includeOptional: false);

        // "Open With" sorts to the end of the menu, matching the action strip.
        return applicable.OrderBy(a => a is OpenWithAction ? 1 : 0).ToList();
    }

    /// <summary>The paths actions for <paramref name="entries"/> act on: theirs, or the open folder when there are none.</summary>
    public IReadOnlyList<string> TargetsOf(IReadOnlyList<FileSystemEntry> entries)
        => entries.Count > 0
            ? entries.Select(e => e.FullPath).ToList()
            : (!string.IsNullOrEmpty(_location.CurrentPath) ? [_location.CurrentPath] : new List<string>());

    /// <summary>Nothing, only folders, or any of This PC's places selected: what applies is what acts on a folder.</summary>
    private static bool UsesFolderActions(IReadOnlyList<FileSystemEntry> selected)
        => selected.Count == 0 || selected.All(e => e.IsDirectory) || selected.Any(e => e.IsThisPcItem);

    /// <summary>Drops folder actions that don't make sense inside an archive (e.g. "Zip It"), when the folder they would
    /// act on is one — <paramref name="inContainer"/>, settled when it was browsed to.</summary>
    private static IReadOnlyList<IFileAction> FilterForVirtual(IReadOnlyList<IFileAction> actions, bool inContainer)
        => inContainer
            ? actions.Where(a => a is not FolderActionAdapter fa || fa.Inner.AppliesInsideArchive).ToList()
            : actions;
}
