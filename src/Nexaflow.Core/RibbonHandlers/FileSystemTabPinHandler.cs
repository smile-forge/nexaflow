using Nexaflow.Core.Models;
using Nexaflow.Core.Views;
using Nexaflow.Core;
using Nexaflow.Features.Common.Ribbon;
using System.Collections.Generic;

namespace Nexaflow.Core.RibbonHandlers;

/// <summary>
/// Handles drag-pin and execution of FileSystem tabs on the ribbon.
/// Pin receives a <see cref="Page"/> and bakes the current navigation path into
/// the ribbon button so it always re-opens the exact location the user was viewing.
/// </summary>
public sealed class FileSystemTabPinHandler(WorkContext workContext) : IRibbonPinHandler
{
    public string ContentKind => FileSystemPageRegistration.PageKind;

    public RibbonPinResult? Pin(object payload, int insertIndex = -1)
    {
        if (payload is not Page tab) return null;

        if (tab.Content is FileSystemView fsPage)
        {
            var vm       = fsPage.ViewModel;
            var path     = vm.CurrentPath;
            var isThisPc = string.IsNullOrEmpty(path) || path == "This PC";
            if (!isThisPc)
                vm.ResetRootToCurrentPath();

            return new RibbonPinResult
            {
                Label      = isThisPc ? "This PC" : System.IO.Path.GetFileName(path) is { Length: > 0 } n ? n : path,
                Icon       = isThisPc ? "🖥" : "📁",
                PageParams = isThisPc
                    ? new() { ["mode"] = "thispc" }
                    : new() { ["mode"] = "path", ["path"] = path }
            };
        }

        // Content not yet loaded — snapshot whatever the tab already knows.
        return new RibbonPinResult
        {
            Label      = tab.Title,
            Icon       = tab.Icon,
            PageParams = tab.PageParams is not null ? new(tab.PageParams) : null
        };
    }

    public void Execute(Dictionary<string, string>? pageParams, IRibbonExecutionContext context)
        => ((IShellServices?)workContext.ShellServices)?.OpenTab(FileSystemPageRegistration.PageKind, pageParams);
}
