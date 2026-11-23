using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Rtf.ViewModels;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Rtf;

/// <summary>
/// Registers the rich-text document page. Opens the one <c>.rtf</c> file given by the <c>"path"</c> param — from the
/// "As rich text" action, or a double-click — and draws it with the shared renderer's RTF language. Discovered by
/// reflection via <see cref="StaticPageKind"/>.
/// </summary>
public sealed class RtfTabRegistration(IShellServices shell, IVirtualFileSystem vfs) : IPageRegistration
{
    public static string StaticPageKind => "Rtf";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Full path to a rich-text document (.rtf).", Required: true, OnDisk: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = Path.GetFileName(path);

        var page = new Page
        {
            Icon = "📝",
            Title = string.IsNullOrEmpty(title) ? "RTF" : title,
        };

        page.ViewModelFactory = () => new RtfViewModel(path, shell, vfs);

        if (!string.IsNullOrEmpty(path)) page.SetFileBreadcrumbs(path, title);
        return page;
    }
}
