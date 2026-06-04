using Nexaflow.Features.Common;
using Nexaflow.Features.Json.Services;
using Nexaflow.Features.Json.ViewModels;
using Nexaflow.Features.Json.Views;
using System.IO;

namespace Nexaflow.Features.Json;

public sealed class JsonTabRegistration : IPageRegistration
{
    private readonly IShellServices _shellServices;

    public JsonTabRegistration(IShellServices shellServices) => _shellServices = shellServices;

    public static string StaticPageKind => "Json";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "JSON" : Path.GetFileName(path);

        return new Page
        {
            Title       = title,
            Icon        = "{}",
            Breadcrumbs = {new BreadcrumbSegment { Label = title }},
            ContentFactory = () =>
            {
                var vm = new JsonViewModel(path, new JsonFileLoader(), new JsonPathEvaluator(), _shellServices);
                return new JsonView(vm);
            },
        };
    }
}
