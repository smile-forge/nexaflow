using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.RibbonHandlers;

/// <summary>
/// Allows non-destructive <see cref="IFileAction"/> instances to be pinned to the ribbon.
/// When the ribbon button is clicked, the action runs against the current file selection.
/// </summary>
public sealed class FileActionRibbonPinHandler : IRibbonPinHandler
{
    private readonly FileActionManager _actions;

    public FileActionRibbonPinHandler(FileActionManager actions) => _actions = actions;

    public string ContentKind => PageKinds.FileAction;

    public RibbonPinResult? Pin(object payload, int insertIndex = -1)
    {
        if (payload is not FileActionPinPayload pinPayload) return null;
        var action = pinPayload.Action;
        if (action.IsDestructive) return null;

        var paths     = pinPayload.SelectedPaths;
        var firstName = paths.Count > 0 ? Path.GetFileName(paths[0]) : null;
        var label     = firstName is { Length: > 0 } ? $"{action.DisplayName}: {firstName}" : action.DisplayName;

        var pageParams = new Dictionary<string, string> { ["actionType"] = action.GetType().FullName! };
        if (paths.Count > 0)
            pageParams["files"] = string.Join("|", paths);

        return new RibbonPinResult { Label = label, Icon = action.Icon, PageParams = pageParams };
    }

    public void Execute(Dictionary<string, string>? pageParams, IRibbonExecutionContext context)
    {
        if (pageParams?.TryGetValue("actionType", out var typeName) != true) return;

        var action = _actions.FindByTypeName(typeName!);
        if (action == null) return;

        List<string> paths;
        if (pageParams.TryGetValue("files", out var filesStr) && !string.IsNullOrEmpty(filesStr))
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
