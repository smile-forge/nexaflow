using Nexaflow.Features.Common;
using Nexaflow.Features.Json.Services;
using Nexaflow.Features.Json.ViewModels;
using Nexaflow.IO.Common;
using System.IO;

namespace Nexaflow.Features.Json;

public sealed class JsonTabRegistration : IPageRegistration
{
    private readonly IShellServices _shellServices;
    private readonly IVirtualFileSystem _vfs;

    public JsonTabRegistration(IShellServices shellServices, IVirtualFileSystem vfs)
    {
        _shellServices = shellServices;
        _vfs           = vfs;
    }

    public static string StaticPageKind => "Json";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Full path of the JSON file to open.", OnDisk: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "JSON" : _vfs.GetDisplayName(path);

        var page = new Page
        {
            Title       = title,
            Icon        = "{}",
            ViewModelFactory = () => new JsonViewModel(path, _vfs, new JsonPathEvaluator(), _shellServices),
        };
        page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
