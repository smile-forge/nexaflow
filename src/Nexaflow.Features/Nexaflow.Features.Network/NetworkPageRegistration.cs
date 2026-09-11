using Nexaflow.Features.Common;
using Nexaflow.Features.Network.ViewModels;
using Nexaflow.Features.Network.Views;
using Nexaflow.IO.Network.Actions;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network;

/// <summary>
/// Advertises the "Network" page — what is on this machine's segments.
/// </summary>
/// <remarks>
/// <para>
/// The constructor is the whole architecture: discovery layers and device actions both arrive as
/// <b>handles</b>, so none of their assemblies is loaded until the user runs one. A machine that never
/// opens this page never pays for any of them, and adding either kind never edits this file.
/// </para>
/// <para>
/// The config arrives here too, and that is what makes Options → Save reopen this tab: the shell refreshes
/// the pages whose registration takes the config that changed.
/// </para>
/// </remarks>
public sealed class NetworkPageRegistration(
    IReadOnlyList<ISubfeatureHandle<INetworkProbe>> layers,
    IReadOnlyList<ISubfeatureHandle<IDeviceAction>> actions,
    NetworkConfig config,
    IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "Network";
    public string PageKind => StaticPageKind;
    public bool CanBeContextItem => true;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var page = new Page
        {
            Title = "Network",
            Icon = "🖧",
            Breadcrumbs = { new BreadcrumbSegment { Label = "Network" } },
        };

        NetworkViewModel? viewModel = null;
        page.ContentFactory = () =>
            new NetworkView(viewModel = new NetworkViewModel(layers, actions, config, shellServices));

        // Options → Save closes and reopens this tab to apply what changed. The page being closed stops
        // sending there and then, rather than finishing a sweep under settings the user has just changed.
        page.Closed += (_, _) => _ = viewModel?.ShutDownAsync();

        return page;
    }
}
