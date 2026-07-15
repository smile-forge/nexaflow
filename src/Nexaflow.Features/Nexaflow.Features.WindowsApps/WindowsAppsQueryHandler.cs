using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.ViewModels;

namespace Nexaflow.Features.WindowsApps;

/// <summary>
/// Filters the installed-apps list by name from the shell input bar when a WindowsApps tab is active.
/// Auto-discovered by FeatureManager (lives in a Nexaflow.Features.* assembly).
/// </summary>
public sealed class WindowsAppsQueryHandler : IQueryHandler
{
    public string Description =>
        "Filters the installed applications list by name. Use when an Installed Apps tab is active and " +
        "the user types part of an application name.";

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null) =>
        pageVm is WindowsAppsViewModel ? 0.7f : 0f;

    public Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not WindowsAppsViewModel vm)
            return Task.FromResult<string?>("No Installed Apps tab is active.");

        return Task.FromResult(vm.SetFilter(input.Trim()));
    }
}
