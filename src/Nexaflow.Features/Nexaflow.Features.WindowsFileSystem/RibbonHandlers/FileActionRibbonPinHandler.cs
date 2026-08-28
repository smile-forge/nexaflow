using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.WindowsFileSystem.RibbonHandlers;

/// <summary>
/// Allows non-destructive <see cref="IFileAction"/> instances to be pinned to the ribbon, and runs them
/// when clicked. Looks up actions through <see cref="FileSystemFeatureRegistry"/>, which owns the
/// singleton instance set. The two halves — building the button (<see cref="IRibbonPinHandler"/>) and
/// running it (<see cref="IRibbonItemExecutor"/>) — share this instance's registry.
/// </summary>
public sealed class FileActionRibbonPinHandler : IRibbonPinHandler, IRibbonItemExecutor
{
    private readonly FileSystemFeatureRegistry _registry;

    public FileActionRibbonPinHandler(IShellServices shell, IAIService ai, IReadOnlyDictionary<Type, IFeatureConfig> configs)
        => _registry = FileSystemFeatureRegistry.For(shell, ai, configs);

    public IReadOnlyList<string> AcceptedFormats { get; } = [FileSystemPageRegistration.FileActionKind];

    public string PageKind => FileSystemPageRegistration.FileActionKind;

    // PageParam keys reserved by this handler. Reinit params from the action are
    // stored flat under the "r." prefix to keep them distinct from these.
    private const string KeyActionType = "actionType";
    private const string KeyFiles      = "files";
    private const string ReinitPrefix  = "r.";

    public RibbonPinResult? Pin(object payload, int insertIndex = -1)
    {
        if (payload is not FileActionPinPayload pinPayload) return null;
        var action = pinPayload.Action;
        if (action.IsDestructive) return null;

        var paths     = pinPayload.SelectedPaths;
        var firstName = paths.Count > 0 ? Path.GetFileName(paths[0]) : null;
        var label     = firstName is { Length: > 0 } ? $"{action.DisplayName}: {firstName}" : action.DisplayName;

        var pageParams = new Dictionary<string, string> { [KeyActionType] = action.GetType().FullName! };
        if (paths.Count > 0)
            pageParams[KeyFiles] = string.Join("|", paths);

        // Runtime-constructed actions (e.g. ShellVerbAction) need their state
        // persisted so FeatureManager.FindFileAction can rehydrate them later.
        var reinit = action.GetReinitParams();
        if (reinit is not null)
            foreach (var kv in reinit)
                pageParams[ReinitPrefix + kv.Key] = kv.Value;

        return new RibbonPinResult { PageKind = PageKind, Label = label, Icon = action.Icon, PageParams = pageParams };
    }

    /// <summary>
    /// Why a pinned action cannot run on a selection of <paramref name="selectionCount"/> files, or null when
    /// it can.
    /// <para>
    /// The action strip decides this by <em>hiding</em> the action: <c>FileActionManager</c> drops anything
    /// whose <see cref="IFileAction.SupportsMultipleFiles"/> is false from a multi-selection, so the user
    /// never has a button to press. A pinned ribbon button cannot be hidden — it is already on the ribbon —
    /// and this path resolved the action straight from the registry and invoked it, so the guard the rest of
    /// the shell applies simply was not here. "Properties" pinned, two files selected, click:
    /// <c>NotImplementedException</c>. Saying why it cannot run is the ribbon's version of not offering it;
    /// quietly acting on the first of the files the user selected would be a different action from the one
    /// they asked for.
    /// </para>
    /// </summary>
    internal static string? BlockedReason(IFileAction action, int selectionCount) =>
        selectionCount == 0
            ? "Select files in the file explorer first."
            : selectionCount > 1 && !action.SupportsMultipleFiles
                ? $"\"{action.DisplayName}\" works on one file at a time — select a single file."
                : null;

    public void Execute(Dictionary<string, string>? pageParams, IRibbonExecutionContext context)
    {
        if (pageParams?.TryGetValue(KeyActionType, out var typeName) != true) return;

        Dictionary<string, string>? reinit = null;
        foreach (var kv in pageParams)
            if (kv.Key.StartsWith(ReinitPrefix))
                (reinit ??= [])[kv.Key[ReinitPrefix.Length..]] = kv.Value;

        var action = _registry.FindFileAction(typeName!, reinit);
        if (action == null) return;

        List<string> paths;
        if (pageParams.TryGetValue(KeyFiles, out var filesStr) && !string.IsNullOrEmpty(filesStr))
            paths = [.. filesStr.Split('|').Where(p => !string.IsNullOrEmpty(p))];
        else
            paths = [.. context.SelectedFilePaths];

        if (BlockedReason(action, paths.Count) is { } why)
        {
            context.ShowError(why);
            return;
        }

        // Through the VFS: a pinned action on a mounted or in-archive path is still valid, and asking
        // File.Exists about it would wrongly offer to delete the user's ribbon button.
        var missing = paths.Where(p => !VirtualFileSystem.Instance.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            context.ShowConfirmation(
                "Files Not Found",
                "The pinned files no longer exist. Remove this ribbon button?",
                context.RemoveCurrentRibbonItem);
            return;
        }

        if (paths.Count == 1) action.PerformAction(paths[0]);
        else                  action.PerformAction(paths);
    }
}
