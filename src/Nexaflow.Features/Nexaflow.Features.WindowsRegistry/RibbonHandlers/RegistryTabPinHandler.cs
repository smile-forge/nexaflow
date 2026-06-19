using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Features.WindowsRegistry.Views;

namespace Nexaflow.Features.WindowsRegistry.RibbonHandlers;

/// <summary>
/// Pins a registry tab to the ribbon, baking the current key into the button (re-rooting the tree
/// there) so it always re-opens rooted at that exact key. Clicking just re-opens the page kind.
/// </summary>
public sealed class RegistryTabPinHandler : ITabPinHandler
{
    public string TabPageKind => RegistryPageRegistration.PageKind;

    public RibbonPinResult? Pin(Page tab, int insertIndex = -1)
    {
        if (tab.Content is RegistryView view)
        {
            var vm = view.ViewModel;
            vm.ResetRootToCurrentKey();               // re-root the tree at the current key before pinning
            var (hive, sub) = Split(vm.CurrentKeyPath);
            var leaf = sub.Length == 0 ? hive : sub[(sub.LastIndexOf('\\') + 1)..];

            return new RibbonPinResult
            {
                PageKind   = RegistryPageRegistration.PageKind,
                Label      = leaf,
                Icon       = "🗝",
                PageParams = sub.Length == 0
                    ? new() { ["hive"] = hive }
                    : new() { ["hive"] = hive, ["path"] = sub }
            };
        }

        // Content not yet loaded — snapshot whatever the tab already knows.
        return new RibbonPinResult
        {
            PageKind   = RegistryPageRegistration.PageKind,
            Label      = tab.Title,
            Icon       = tab.Icon,
            PageParams = tab.PageParams is not null ? new(tab.PageParams) : null
        };
    }

    private static (string Hive, string Sub) Split(string full)
    {
        var i = full.IndexOf('\\');
        return i < 0 ? (full, "") : (full[..i], full[(i + 1)..]);
    }
}
