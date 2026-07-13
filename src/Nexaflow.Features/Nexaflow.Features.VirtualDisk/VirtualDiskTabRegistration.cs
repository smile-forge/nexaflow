using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.VirtualDisk.ViewModels;
using Nexaflow.Features.VirtualDisk.Views;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.VirtualDisk;

/// <summary>Advertises the "As Disk" inspector tab — disk/partition/volume metadata beside a lazily-browsed
/// contents tree. Opened by the "As Disk" file action on a disk image.</summary>
public sealed class VirtualDiskTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "VirtualDisk";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Virtual Disk" : Path.GetFileName(path);

        var page = new Page
        {
            Title = title,
            Icon = "💽",
            PageParams = pageParams,
            ContentFactory = () => new VirtualDiskView(
                new VirtualDiskViewModel(path, shell, VirtualFileSystem.Instance)),
        };

        // "folder › disk.vhdx" — the folder crumb opens a file-browser tab there.
        page.SetFileBreadcrumbs(path, title);

        return page;
    }
}
