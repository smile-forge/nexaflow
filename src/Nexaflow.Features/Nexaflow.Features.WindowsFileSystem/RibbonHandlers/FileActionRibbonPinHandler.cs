using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Features.WindowsFileSystem.Services;

namespace Nexaflow.Features.WindowsFileSystem.RibbonHandlers;

/// <summary>
/// Allows non-destructive <see cref="IFileAction"/> instances to be pinned to the ribbon.
/// Looks up actions through <see cref="FileSystemFeatureRegistry"/>, which owns the
/// singleton instance set.
/// </summary>
public sealed class FileActionRibbonPinHandler : IRibbonPinHandler
{
    private readonly FileSystemFeatureRegistry _registry;

    public FileActionRibbonPinHandler(IShellServices shell, IAIService ai, IReadOnlyDictionary<Type, IFeatureConfig> configs)
        => _registry = FileSystemFeatureRegistry.For(shell, ai, configs);

    public string ContentKind => FileSystemPageRegistration.FileActionKind;

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

        return new RibbonPinResult { Label = label, Icon = action.Icon, PageParams = pageParams };
    }

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
        {
            paths = [.. filesStr.Split('|').Where(p => !string.IsNullOrEmpty(p))];
        }
        else
        {
            paths = [.. context.SelectedFilePaths];
            if (paths.Count == 0)
            {
                context.ShowError("Select files in the file explorer first.");
                return;
            }
        }

        var missing = paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
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
