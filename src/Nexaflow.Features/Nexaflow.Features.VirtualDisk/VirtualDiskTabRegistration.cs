using Nexaflow.Features.Common;
using Nexaflow.Features.VirtualDisk.ViewModels;
using Nexaflow.Features.VirtualDisk.Views;

namespace Nexaflow.Features.VirtualDisk;

/// <summary>
/// Placeholder registration for the VirtualDisk feature — will mount and browse virtual-disk images via
/// DiscUtils (the external/DiscUtils submodule). Stub only: the page shows a "not yet implemented" notice
/// pending feature specification, is not offered as AI context, and has no default ribbon button.
/// </summary>
public sealed class VirtualDiskTabRegistration(IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "VirtualDisk";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null) => new()
    {
        Title       = "Virtual Disk",
        Icon        = "💽",
        Breadcrumbs = { new BreadcrumbSegment { Label = "Virtual Disk" } },
        ContentFactory = () => new VirtualDiskView(new VirtualDiskViewModel(shellServices))
    };
}
