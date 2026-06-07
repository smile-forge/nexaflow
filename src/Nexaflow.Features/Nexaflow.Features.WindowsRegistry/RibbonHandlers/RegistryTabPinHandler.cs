using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Features.WindowsRegistry.Views;

namespace Nexaflow.Features.WindowsRegistry.RibbonHandlers;

/// <summary>
/// Handles drag-pin and execution of registry tabs on the ribbon. Pin bakes the current key into the
/// ribbon button (re-rooting the tree there) so it always re-opens rooted at that exact key.
/// </summary>
public sealed class RegistryTabPinHandler(IShellServices _shell) : IRibbonPinHandler
{
    public string ContentKind => RegistryPageRegistration.PageKind;

    public RibbonPinResult? Pin(object payload, int insertIndex = -1)
    {
        if (payload is not Page tab) return null;

        if (tab.Content is RegistryView view)
        {
            var vm = view.ViewModel;
            vm.ResetRootToCurrentKey();               // re-root the tree at the current key before pinning
            var (hive, sub) = Split(vm.CurrentKeyPath);
            var leaf = sub.Length == 0 ? hive : sub[(sub.LastIndexOf('\\') + 1)..];

            return new RibbonPinResult
            {
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
            Label      = tab.Title,
            Icon       = tab.Icon,
            PageParams = tab.PageParams is not null ? new(tab.PageParams) : null
        };
    }

    public void Execute(Dictionary<string, string>? pageParams, IRibbonExecutionContext context)
        => _shell.OpenTab(RegistryPageRegistration.PageKind, pageParams);

    private static (string Hive, string Sub) Split(string full)
    {
        var i = full.IndexOf('\\');
        return i < 0 ? (full, "") : (full[..i], full[(i + 1)..]);
    }
}
