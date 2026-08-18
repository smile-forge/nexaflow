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
/// The constructor is the whole architecture: discovery layers and device actions both arrive as
/// <b>handles</b>, so none of their assemblies is loaded until the user runs one. A machine that never
/// opens this page never pays for any of them, and adding either kind never edits this file.
/// </remarks>
public sealed class NetworkPageRegistration(
    IReadOnlyList<ISubfeatureHandle<INetworkProbe>> layers,
    IReadOnlyList<ISubfeatureHandle<IDeviceAction>> actions,
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

        page.ContentFactory = () => new NetworkView(new NetworkViewModel(layers, actions, shellServices));
        return page;
    }
}
