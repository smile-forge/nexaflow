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
        if (payload is not IFileAction action || action.IsDestructive) return null;
        return new RibbonPinResult
        {
            Label      = action.DisplayName,
            Icon       = action.Icon,
            PageParams = new Dictionary<string, string> { ["actionType"] = action.GetType().FullName! }
        };
    }

    public void Execute(Dictionary<string, string>? pageParams, IRibbonExecutionContext context)
    {
        if (pageParams?.TryGetValue("actionType", out var typeName) != true) return;

        var action = _actions.FindByTypeName(typeName!);
        if (action == null) return;

        var paths = context.SelectedFilePaths;
        if (paths.Count == 0)
        {
            context.ShowError("Select files in the file explorer first.");
            return;
        }

        var missing = paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            context.ShowConfirmation(
                "Files Not Found",
                "The selected files no longer exist. Remove this ribbon button?",
                context.RemoveCurrentRibbonItem);
            return;
        }

        if (paths.Count == 1) action.PerformAction(paths[0]);
        else                  action.PerformAction(paths);
    }
}
