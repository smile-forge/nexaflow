using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Features.WindowsFileSystem.Views;

namespace Nexaflow.Features.WindowsFileSystem.RibbonHandlers;

/// <summary>
/// Pins a FileSystem tab to the ribbon, baking the current navigation path into the button so it
/// always re-opens the exact location the user was viewing. Clicking just re-opens the page kind —
/// no executor needed.
/// </summary>
public sealed class FileSystemTabPinHandler : ITabPinHandler
{
    public string TabPageKind => FileSystemPageRegistration.PageKind;

    public RibbonPinResult? Pin(Page tab, int insertIndex = -1)
    {
        if (tab.Content is FileSystemView fsPage)
        {
            var vm       = fsPage.ViewModel;
            var path     = vm.CurrentPath;
            var isThisPc = string.IsNullOrEmpty(path) || path == "This PC";
            if (!isThisPc)
                vm.ResetRootToCurrentPath();

            return new RibbonPinResult
            {
                PageKind   = FileSystemPageRegistration.PageKind,
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
            PageKind   = FileSystemPageRegistration.PageKind,
            Label      = tab.Title,
            Icon       = tab.Icon,
            PageParams = tab.PageParams is not null ? new(tab.PageParams) : null
        };
    }
}
