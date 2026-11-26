using Microsoft.Extensions.Logging;
using Nexaflow.AiHost.Provisioning;

using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Nexaflow.Session.Services;

/// <summary>
/// The saved workspaces — the list the selector and the taskbar's jump list show — and what adds, clones, renames and deletes
/// them, each written back to disk. The live runtimes are not here (<see cref="WorkspaceRuntimes"/>). The session makes one
/// (<see cref="ShellHost"/>) and hands it to what reads or changes the list.
/// </summary>
public sealed class WorkspaceManager
{
    /// <summary>
    /// Saved workspace configurations — what the dropdown and taskbar JumpList show.
    /// Never contains runtime-only data.
    /// </summary>
    public ObservableCollection<Workspace> Workspaces { get; } = [];

    /// <summary>
    /// Fired when workspaces are added/removed, so listeners
    /// (JumpList, ShellViewModel) can refresh their derived state.
    /// </summary>
    public event EventHandler? WorkspacesRefreshed;

    /// <summary>The saved workspace list on disk, which <see cref="SaveWorkspaces"/> writes.</summary>
    private readonly WorkspacesConfig _savedConfig;

    /// <summary>The session's provider pool, which a workspace's provider configs are read through.</summary>
    private readonly ProviderManager _providers;

    /// <summary>The session's configuration: the list is saved through it, and every workspace in it is listed in it.</summary>
    private readonly ConfigManager _configuration;

    /// <summary>Where what the list tidied of the workspaces' folders, and what it could not, is put on record.</summary>
    private readonly ILogger<WorkspaceManager> _log;

    /// <summary>
    /// The saved workspaces <paramref name="config"/> holds, or one default workspace where it holds none, each listed in
    /// <paramref name="configuration"/> (<see cref="Workspace.ListIn"/>). The list alone: no runtime is made until a window opens one.
    /// </summary>
    /// <param name="config">The saved workspace list, which <see cref="SaveWorkspaces"/> writes back.</param>
    /// <param name="providers">The session's provider pool, which a workspace's provider configs are read through.</param>
    /// <param name="configuration">The session's configuration: the list is saved through it, and each workspace's folders sit
    /// under its own.</param>
    /// <param name="log">Where the list says what it tidied of the workspaces' folders, and what it could not.</param>
    public WorkspaceManager(WorkspacesConfig config, ProviderManager providers, ConfigManager configuration, ILogger<WorkspaceManager> log)
    {
        _savedConfig   = config;
        _providers     = providers;
        _configuration = configuration;
        _log           = log;

        var workspaces = config.Contexts is { Count: > 0 } saved ? saved : [new Workspace()];
        foreach (var p in workspaces)
            Keep(p);
    }

    /// <summary>Adds <paramref name="workspace"/> to <see cref="Workspaces"/>, listed in the session's configuration.</summary>
    private void Keep(Workspace workspace)
    {
        workspace.ListIn(_configuration);
        Workspaces.Add(workspace);
    }

    /// <summary>The reserved sub-folder of <c>Contexts\</c> that orphaned workspace data is moved into
    /// (rather than deleted) so a mis-fire is recoverable by hand. Never itself treated as an orphan.</summary>
    public const string OrphanQuarantineDir = "_orphaned";

    /// <summary>
    /// Rebuilds <paramref name="config"/>'s workspace list from the data folders under <c>Contexts\</c> —
    /// the recovery path for a <c>workcontexts.json</c> that could not be read. The folder name IS the
    /// workspace name (see <see cref="WorkspaceDir"/>), so a workspace whose entry is gone comes back
    /// pointing at everything it owns: its AI and provider config, ribbon layout, per-workspace feature
    /// configs (scratchpad notes among them) and every conversation. Nothing else rebuilds the list from
    /// disk, so without this the damaged file costs the user all of that, and
    /// <see cref="QuarantineOrphanedDataFolders"/> would file it as orphaned.
    /// <para>
    /// Additive and idempotent: entries already in the list are kept as they are, and only folders no entry
    /// names are added (with a fresh symbol and colour — those lived only in the file that was lost). The
    /// folders themselves are read for their names and never touched. Returns the names recovered. The folders are those under
    /// <paramref name="configuration"/>'s base, which the list is not yet kept by: this runs before it is.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> RecoverWorkspacesFromDataFolders(WorkspacesConfig config, ConfigManager configuration)
    {
        var root = Path.Combine(configuration.BaseDir, "Contexts");
        if (!Directory.Exists(root)) return [];

        var known     = config.Contexts.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recovered = new List<Workspace>();

        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, OrphanQuarantineDir, StringComparison.OrdinalIgnoreCase)) continue;
            if (!known.Add(name)) continue;

            var (icon, color) = WorkspaceStyle.Random();
            recovered.Add(new Workspace { Name = name, Icon = icon, Color = color });
        }

        if (recovered.Count == 0) return [];

        // Through the setter, so the rebuilt list is sanitized like any other (a folder whose name is not a
        // usable workspace name, or that collides with an entry already there, is dealt with there).
        config.Contexts = [.. config.Contexts, .. recovered];
        return [.. recovered.Select(p => p.Name)];
    }

    /// <summary>
    /// Quarantines data folders under <c>Contexts\</c> that no loaded workspace references — leftovers from a
    /// workspace removed out-of-band, or (the reason this exists) folders orphaned when an older build reset
    /// the workspace list on a version bump before forward-migration existed. Nothing rebuilds the list from
    /// these folders, so they are permanently unreachable dead weight. Instead of deleting, they are moved
    /// into <see cref="OrphanQuarantineDir"/> so a wrong call is recoverable; the user can clear that bin.
    ///
    /// HARD-GATED on <paramref name="listIsAuthoritative"/>: the list is only trustworthy when it actually
    /// loaded (or migrated) from disk. When it defaulted — missing or unreadable <c>workcontexts.json</c> —
    /// EVERY folder looks orphaned, and moving them all would be the very data-loss this guards against, so
    /// it is skipped entirely. Call once at startup, right after the list is made. Returns the
    /// folder names quarantined (for logging / tests).
    /// </summary>
    public IReadOnlyList<string> QuarantineOrphanedDataFolders(bool listIsAuthoritative)
    {
        if (!listIsAuthoritative) return [];

        var root = Path.Combine(_configuration.BaseDir, "Contexts");
        if (!Directory.Exists(root)) return [];

        // Folder name == workspace Name (see WorkspaceDir); match case-insensitively like the filesystem.
        var live       = Workspaces.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var quarantine = Path.Combine(root, OrphanQuarantineDir);
        var moved      = new List<string>();

        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (live.Contains(name)) continue;                                                  // a live workspace's folder
            if (string.Equals(name, OrphanQuarantineDir, StringComparison.OrdinalIgnoreCase))
                continue;                                                                       // the quarantine bin itself
            try
            {
                Directory.CreateDirectory(quarantine);
                Directory.Move(dir, UniqueQuarantinePath(quarantine, name));
                moved.Add(name);
            }
            catch (Exception ex)
            {
                // A locked file leaves the folder in place rather than crashing startup — retried next launch.
                _log.LogWarning(ex, "quarantining the orphaned workspace folder '{Folder}'", name);
            }
        }

        ForgetOrphanedLocalFolders(live);

        if (moved.Count > 0)
            _log.LogInformation("workspaces: quarantined {Count} orphaned workspace folder(s) into '{Quarantine}': {Folders}",
                                moved.Count, OrphanQuarantineDir, string.Join(", ", moved));

        return moved;
    }

    /// <summary>
    /// Lets go of what no listed workspace keeps on this machine alone. Caches, worked out again from what they were kept of, so
    /// let go of rather than quarantined; a folder in use is left for the next launch.
    /// </summary>
    private void ForgetOrphanedLocalFolders(IReadOnlySet<string> live)
    {
        var root = Path.Combine(_configuration.LocalDir, "Contexts");
        if (!Directory.Exists(root)) return;

        foreach (var dir in Directory.GetDirectories(root))
        {
            if (live.Contains(Path.GetFileName(dir))) continue;

            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { _log.LogWarning(ex, "removing the orphaned local workspace folder '{Folder}'", dir); }
        }
    }

    /// <summary>A destination inside <paramref name="quarantine"/> for <paramref name="name"/> that doesn't
    /// collide with one already quarantined under that name (appends " (2)", " (3)", …).</summary>
    private static string UniqueQuarantinePath(string quarantine, string name)
    {
        var dest = Path.Combine(quarantine, name);
        for (int i = 2; Directory.Exists(dest); i++)
            dest = Path.Combine(quarantine, $"{name} ({i})");
        return dest;
    }

    /// <summary>
    /// Makes <paramref name="model"/> <paramref name="workspace"/>'s <paramref name="slot"/> model — a download a choice waited on
    /// has landed — and saves it. The workspace's runtime takes it up through the session (<see cref="WorkspaceRuntimes.Reacquire"/>).
    /// </summary>
    public void UseLocalModel(Workspace workspace, LocalSlot slot, ProviderModelPair model)
    {
        workspace.EnsureSharedServicesLoaded(_providers);
        LocalColumns.Use(workspace.AiConfig, slot, model);
        workspace.SaveAiConfig();
    }

    /// <summary>Adds a new workspace with a random icon/colour to <see cref="Workspaces"/>.</summary>
    public Workspace AddWorkspace(string name)
    {
        var (icon, color) = WorkspaceStyle.Random();
        var workspace = new Workspace { Name = name, Icon = icon, Color = color };
        Keep(workspace);
        WorkspacesRefreshed?.Invoke(this, EventArgs.Empty);
        return workspace;
    }

    /// <summary>
    /// Creates a new workspace whose AI config + provider configs are copied from
    /// <paramref name="source"/>'s folder, with a randomised icon/colour.
    /// </summary>
    public Workspace CloneWorkspace(Workspace source, string name)
    {
        var (icon, color) = WorkspaceStyle.Random();
        var workspace = new Workspace { Name = name, Icon = icon, Color = color };
        var destDir = WorkspaceDir(_configuration, name);

        source.EnsureSharedServicesLoaded(_providers);
        _configuration.SaveTo(destDir, source.AiConfig, source.AiConfig.ConfigName);
        _configuration.SaveTo(destDir, source.Persona, source.Persona.ConfigName);
        foreach (var cfg in source.ProviderConfigs)
            _configuration.SaveTo(destDir, cfg, cfg.ConfigName);
        foreach (var cfg in source.WorkspaceConfigs)
            _configuration.SaveTo(destDir, cfg, cfg.ConfigName);

        Keep(workspace);
        WorkspacesRefreshed?.Invoke(this, EventArgs.Empty);
        return workspace;
    }

    /// <summary>A workspace name not already in use (appends " 2", " 3", … on collision, case-insensitive).</summary>
    public string UniqueWorkspaceName(string baseName)
    {
        var taken = Workspaces.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Renames <paramref name="workspace"/> to <paramref name="newName"/>, moving its on-disk data folder
    /// so its AI / provider / conversation config follows the new name (otherwise the workspace would
    /// silently start from an empty folder). Always re-persists the workspace list, so an unchanged name
    /// with edited colour/icon is saved too. The caller must ensure the name is non-empty and not
    /// already used by another workspace (which would collide on the folder move).
    /// </summary>
    public void RenameWorkspace(Workspace workspace, string newName)
    {
        if (!string.Equals(workspace.Name, newName, StringComparison.Ordinal))
        {
            var oldDir = WorkspaceDir(_configuration, workspace.Name);
            var newDir = WorkspaceDir(_configuration, newName);
            // Skip the move on a case-only rename (same path on Windows) or when there's nothing to move.
            if (!string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(oldDir) && !Directory.Exists(newDir))
        Directory.Move(oldDir, newDir);

                // What it keeps on this machine alone goes with it.
                var oldLocal = WorkspaceLocalDir(_configuration, workspace.Name);
                var newLocal = WorkspaceLocalDir(_configuration, newName);
                if (!string.Equals(oldLocal, newLocal, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(oldLocal) && !Directory.Exists(newLocal))
                    Directory.Move(oldLocal, newLocal);
            workspace.Name = newName;   // observable → selector/ribbon update; Dir now resolves to newDir
        }
        SaveWorkspaces();
    }

    /// <summary>
    /// Removes <paramref name="workspace"/> from <see cref="Workspaces"/> and deletes its on-disk data
    /// folder (config + conversations). At least one workspace always remains. The caller (Options editor)
    /// prevents removing the active workspace and persists the list. Returns true if removed.
    /// </summary>
    public bool RemoveWorkspace(Workspace workspace)
    {
        if (Workspaces.Count <= 1) return false;
        if (!Workspaces.Remove(workspace)) return false;
        DeleteWorkspaceFolder(workspace.Name);
        WorkspacesRefreshed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Permanently deletes <paramref name="workspace"/>: removes it from the list, deletes its on-disk
    /// data folder (config + every conversation), and persists the list. Refuses to delete the last
    /// remaining workspace. The caller ends its runtime first (<see cref="WorkspaceRuntimes.Close"/>) — used by the
    /// Configure panel's "Delete Workspace" action, which has no separate Save step. Returns true when deleted.
    /// </summary>
    public bool DeleteWorkspace(Workspace workspace)
    {
        if (!RemoveWorkspace(workspace)) return false;   // removes + deletes the folder
        SaveWorkspaces();
        return true;
    }

    /// <summary>Deletes a workspace's data folder, swallowing IO failures (a locked file orphans the
    /// folder rather than crashing the delete).</summary>
    private void DeleteWorkspaceFolder(string name)
    {
        try
        {
            var dir = WorkspaceDir(_configuration, name);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

            var local = WorkspaceLocalDir(_configuration, name);
            if (Directory.Exists(local)) Directory.Delete(local, recursive: true);
        }
        catch { /* leave an orphaned folder rather than fail the removal */ }
    }

    /// <summary>
    /// Every path a tab names — those <paramref name="open"/> in the live workspaces, and those each workspace's default tabs and
    /// last session would open again — for what the app clears (<see cref="DraftLocation.Sweep"/>) never to take a file a tab shows
    /// or will show. Called on the UI thread, where the tabs are.
    /// </summary>
    internal IReadOnlyList<string> TabPaths(IEnumerable<Page> open)
    {
        var saved = Workspaces.SelectMany(workspace => workspace.DefaultTabs.Concat(workspace.LastSessionTabs ?? []))
                              .SelectMany(tab => DraftLocation.PathsIn(tab.PageParams));
        var showing = open.SelectMany(tab => DraftLocation.PathsIn(tab.PageParams));
        return [.. saved.Concat(showing).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The workspace an item opened from outside the app (Explorer's "Open in Nexaflow") goes to: the one workspace of those
    /// <paramref name="withWindows"/>, or the first saved workspace when none or several are.
    /// </summary>
    public Workspace WorkspaceForOpenedPath(IEnumerable<Workspace> withWindows) => ChooseWorkspaceForOpenedPath(Workspaces, withWindows);

    /// <summary>
    /// The rule <see cref="WorkspaceForOpenedPath"/> applies to the <paramref name="saved"/> workspaces and those
    /// <paramref name="withWindows"/>. Several live runtimes on one workspace are still that one workspace.
    /// </summary>
    internal static Workspace ChooseWorkspaceForOpenedPath(IReadOnlyList<Workspace> saved, IEnumerable<Workspace> withWindows)
    {
        var open = withWindows.Distinct<Workspace>(ReferenceEqualityComparer.Instance).Take(2).ToList();
        return open is [var only] ? only : saved[0];
    }

    /// <summary>Per-workspace data folder under <paramref name="configuration"/>'s base: <c>%AppData%\…\Contexts\&lt;name&gt;</c>
    /// (folder name kept for compat).</summary>
    public static string WorkspaceDir(ConfigManager configuration, string name)
        => Path.Combine(configuration.BaseDir, "Contexts", name);

    /// <summary>
    /// What a workspace keeps on this machine alone: <c>%LocalAppData%\…\Contexts\&lt;name&gt;</c> — caches too large to roam with
    /// its settings. Renamed, deleted and let go of with the workspace's own folder.
    /// </summary>
    public static string WorkspaceLocalDir(ConfigManager configuration, string name)
        => Path.Combine(configuration.LocalDir, "Contexts", name);

    /// <summary>Persists the workspace list (names/colours/icons only).</summary>
    public void SaveWorkspaces()
    {
        _savedConfig.Contexts = [.. Workspaces];
        _configuration.Save(_savedConfig, _savedConfig.ConfigName);
    }

    /// <summary>Persists one workspace's AI ability config to its own folder.</summary>
    public void SaveWorkspaceAiConfig(Workspace workspace) => workspace.SaveAiConfig();

    /// <summary>Persists one workspace's assistant persona (name + system prompt) to its own folder.</summary>
    public void SaveWorkspacePersona(Workspace workspace)
        => _configuration.SaveTo(workspace.Dir, workspace.Persona, workspace.Persona.ConfigName);

    /// <summary>Persists one of a workspace's per-workspace feature configs to its own folder.</summary>
    public void SaveWorkspaceScopedConfig(Workspace workspace, IFeatureConfig config)
        => _configuration.SaveTo(workspace.Dir, config, config.ConfigName);

    /// <summary>Persists one of a workspace's provider configs — the key or address a provider is reached with — to its own folder.</summary>
    public void SaveWorkspaceProviderConfig(Workspace workspace, IProviderConfig config)
        => _configuration.SaveTo(workspace.Dir, config, config.ConfigName);

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Folds a grid saved before the Local column's models on this machine into it, once, and turns the Local column on where this
    /// machine runs a model; each job it is used for keeps a model the job offers here, read from <paramref name="assets"/>. Nothing
    /// is downloaded. Saved when it changed — before the workspace runs, the setup wizard does it so it can offer what the column
    /// now needs.
    /// </summary>
    /// <param name="assets">The files local models need; null where the session keeps none, when only the column is gathered.</param>
    /// <returns>Whether the grid changed.</returns>
    internal static bool FoldInLocalColumns(Workspace workspace, ProviderSet providers, IModelAssetStore? assets)
    {
        var folded = LocalColumns.Gather(workspace.AiConfig, providers.IsLocal);
        if (assets is not null)
        {
            var shelf = new LocalModelShelf(providers, assets);
            IReadOnlyList<(ProviderModelPair, bool, bool, bool)> Offered(LocalSlot slot)
                => [.. shelf.OptionsFor(slot).Select(o => (o.Column(), o.Runs, shelf.IsReady(o), o.Model.Tier == LocalColumns.TierOf(slot)))];
            folded |= LocalColumns.Seed(workspace.AiConfig, Offered);
            folded |= LocalColumns.Settle(workspace.AiConfig, Offered);
        }
        if (folded) workspace.SaveAiConfig();
        return folded;
    }

    /// <summary>The grid columns actually assigned to an ability — the models in use (need an instance).</summary>
    internal static IReadOnlyList<ProviderModelPair> AssignedColumns(AiConfig cfg)
    {
        var assigned = cfg.AssignedColumnIds();
        return [.. cfg.Columns.Where(c => assigned.Contains(c.Id))];
    }

    /// <summary>Whether <paramref name="workspace"/> has a language model ready to answer: one its ability grid assigns, from a provider
    /// whose required config (an API key, say) is complete — or one that takes no config at all, Nexaflow's own, from an assembly
    /// <paramref name="providers"/> has loaded. A speech model alone does not make one.</summary>
    public static bool HasAnsweringModel(Workspace workspace, ProviderManager providers)
    {
        var ai       = workspace.AiConfig;
        var assigned = ai.AssignedColumnIds();
        if (assigned.Count == 0) return false;
        var byName = workspace.ProviderConfigs.ToDictionary(c => c.FriendlyName);
        return ai.Columns
            .Where(c => c.Kind == ModelKind.Language && assigned.Contains(c.Id))
            .Any(c => byName.TryGetValue(c.ProviderName, out var config)
                      ? ConfigRequirements.IsComplete(config)
                      : providers.TakesNoConfig(c.AssemblyFileName));
    }
}
