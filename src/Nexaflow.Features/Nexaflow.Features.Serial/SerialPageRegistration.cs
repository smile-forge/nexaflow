using Nexaflow.Features.Common;
using Nexaflow.Features.Serial.Services;
using Nexaflow.Features.Serial.ViewModels;
using Nexaflow.Features.Serial.Views;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Serial;

/// <summary>
/// Advertises the serial terminal: one tab per COM port (<c>port</c> says which), or a tab that picks one. Defining it
/// opens nothing — the port is opened only when the user, or the agent with the user's approval, connects.
/// </summary>
public sealed class SerialPageRegistration(IShellServices shell) : IPageRegistration
{
    public const string PortParam = "port";

    public static string StaticPageKind => "Serial";
    public string PageKind => StaticPageKind;
    public bool CanBeContextItem => true;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new(PortParam, "The COM port, e.g. 'COM5'. Omit to choose one on the page.", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var port = pageParams?.GetValueOrDefault(PortParam)?.Trim().ToUpperInvariant();
        var page = new Page
        {
            Title = string.IsNullOrEmpty(port) ? Str.Get("Serial.Page.Title") : port,
            Icon = "⌨",
            PageParams = Params(port),
            Breadcrumbs = { new BreadcrumbSegment { Label = Str.Get("Serial.Page.Title") } },
        };

        page.ContentFactory = () =>
        {
            var vm = new SerialViewModel(new SystemSerialLink(), SerialPortCatalog.List, shell, port);
            vm.PortChanged += () =>
            {
                page.Title = vm.Title;
                page.PageParams = Params(vm.Port);
                page.Breadcrumbs.Clear();
                page.Breadcrumbs.Add(new BreadcrumbSegment { Label = Str.Get("Serial.Page.Title") });
                if (vm.Port.Length > 0) page.Breadcrumbs.Add(new BreadcrumbSegment { Label = vm.Port });
            };
            page.Closed += (_, _) => vm.Dispose();
            return new SerialView(vm);
        };
        return page;
    }

    static Dictionary<string, string> Params(string? port) => string.IsNullOrEmpty(port) ? [] : new() { [PortParam] = port };
}
