using Nexaflow.Features.Code.ViewModels;
using Nexaflow.Features.Code.Views;
using Nexaflow.Features.Common;
using System.IO;

namespace Nexaflow.Features.Code;

/// <summary>
/// Registers the "As Code" tab: the shared code editor plus a code-intelligence side panel. The editable-size
/// ceiling comes from <see cref="CodeEditorConfig"/> (injected by FeatureManager). An optional <c>ast</c> page
/// param tells the view to jump to a member after opening (used when following a cross-file member link).
/// </summary>
public sealed class CodeTabRegistration(CodeEditorConfig config, IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Code";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var ast   = pageParams?.GetValueOrDefault("ast");
        var title = string.IsNullOrEmpty(path) ? "Code" : Path.GetFileName(path);

        return new Page
        {
            Title       = title,
            Icon        = "⟨⟩",
            Breadcrumbs = { new BreadcrumbSegment { Label = title } },
            ContentFactory = () => new CodeView(new CodeViewModel(path, shell, config.GetMaxEditableBytes(), ast)),
        };
    }
}
