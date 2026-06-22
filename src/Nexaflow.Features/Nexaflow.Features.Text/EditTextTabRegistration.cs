using Nexaflow.Features.Common;
using Nexaflow.Visuals.Text.Editor;
using System.IO;

namespace Nexaflow.Features.Text;

/// <summary>
/// Registers the read-write "Edit Text" tab, hosting the shared <see cref="FileTextEditorView"/>. The
/// editable-size ceiling comes from <see cref="TextEditorConfig"/> (injected by FeatureManager).
/// </summary>
public sealed class EditTextTabRegistration(TextEditorConfig config, IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "TextEditor";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Editor" : Path.GetFileName(path);

        return new Page
        {
            Title       = title,
            Icon        = "✏",
            Breadcrumbs = { new BreadcrumbSegment { Label = title } },
            ContentFactory = () => new FileTextEditorView(new FileTextEditorViewModel(path, shell, config.GetMaxEditableBytes())),
        };
    }
}
