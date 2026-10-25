using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.ViewModels;
using Nexaflow.Features.Pdf.Views;
using Nexaflow.IO.Common;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Pdf;

/// <summary>
/// Registers the PDF reader page. Takes the document's path, and optionally the page to open at, so a
/// deep link from elsewhere ("see page 42 of the spec") lands where it means to. Page reading, where the app has it,
/// reads a document's scanned pages into Markdown; the workspace's AI draws their diagrams again.
/// </summary>
public sealed class PdfTabRegistration(IShellServices shell, PdfConfig config, IAIService ai, IVirtualFileSystem vfs,
                                       IPageReading? reading = null) : IPageRegistration
{
    public static string StaticPageKind => "Pdf";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Full path to a .pdf file.", OnDisk: true),
        new("page", "1-based page to open at.", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = Path.GetFileName(path);

        var page = new Page
        {
            Icon  = "📕",
            Title = string.IsNullOrEmpty(title) ? "PDF" : title,
        };

        page.ContentFactory = () =>
        {
            var vm   = new PdfViewModel(path, shell, vfs, config, reading, ai);
            var view = new PdfView(vm, shell);

            // Unsaved page edits ride on the tab's title; a Save As makes the tab that file.
            vm.DirtyChanged += dirty => page.Title = (dirty ? "*" : string.Empty) + vm.FileName;
            vm.Moved        += moved => Follow(page, moved);

            if (pageParams?.GetValueOrDefault("page") is { } raw && int.TryParse(raw, out var at))
                view.OpenAtPage(at);

            // Closes the document, ends the browser process, and deletes any temp copy we materialised.
            // Closed fires only on a genuine close — a tear-off re-keys the registry and never reaches it.
            page.Closed += (_, _) => view.Dispose();
            return view;
        };

        if (!string.IsNullOrEmpty(path)) page.SetFileBreadcrumbs(path, title);
        return page;
    }

    private static void Follow(Page page, string path)
    {
        page.Title      = Path.GetFileName(path);
        page.PageParams = new Dictionary<string, string> { ["path"] = path };
        page.SetFileBreadcrumbs(path, page.Title);
    }
}
