using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Features.WindowsFileSystem.Views;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem;

// The FileMapConfig / ExternalAppsConfig / TemplatedCreateConfig parameters are declared so
// FeatureManager builds a config→registration mapping for them; that lets OptionsViewModel fire
// TabRefreshRequested("FileSystem") when any of them is saved, which rebuilds open file-system
// tabs so the new action set / verb gating / template list takes effect immediately.
// They are intentionally unread — only their types matter to the discovery.
#pragma warning disable CS9113 // Parameter is unread.
public sealed class FileSystemPageRegistration(
    IShellServices shell,
    IAIService ai,
    IReadOnlyDictionary<Type, IFeatureConfig> configs,
    FileMapConfig _fileMapConfig,
    ExternalAppsConfig _externalAppsConfig,
    TemplatedCreateConfig _templatedCreateConfig) : IPageRegistration
{
#pragma warning restore CS9113
    public const string PageKind       = "FileSystem";
    public const string FileActionKind = "FileAction";

    public static string StaticPageKind => PageKind;
    string IPageRegistration.PageKind => PageKind;

    IReadOnlyList<PageParameter> IPageRegistration.Parameters =>
    [
        new("mode",  "\"thispc\" for the drive list (default), or \"path\" to open a folder.", Required: false),
        new("path",  "Folder to open. Required when mode is \"path\".", Required: false),
        new("label", "Display name for the tab.", Required: false),
    ];

    /// <summary>The file browser opens standalone — offer it (and its named-folder variants) for the dropdown.</summary>
    bool IPageRegistration.CanBeContextItem => true;

    /// <summary>
    /// "This PC" plus each named Windows folder. The folder set is resolved once (known-folder lookups
    /// are P/Invokes); each call only builds fresh, cheap <see cref="Page"/> stubs from the cached paths.
    /// </summary>
    IReadOnlyList<Page> IPageRegistration.CreatePageDefinitions(Dictionary<string, string>? pageParams)
    {
        var pages = new List<Page> { CreatePageDefinition(new() { ["mode"] = "thispc" }) };
        foreach (var (title, icon, path) in NamedFolders.Value)
            pages.Add(FolderPage(title, icon, path));
        return pages;
    }

    // GUIDs match Core's KnownFolderService so the resolved paths equal the {Token}-expanded
    // default-ribbon paths — letting the editor exclude folders already on the ribbon.
    private static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    private static readonly Guid Pictures  = new("33E28130-4E1E-4676-835A-98395C3BC3BB");
    private static readonly Guid Videos    = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC");
    private static readonly Guid Music     = new("4BD8D571-6D19-48D3-BE97-422220080E43");
    private static readonly Guid Desktop   = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>Named Windows folders (title, icon, resolved path), resolved once per process.</summary>
    private static readonly Lazy<IReadOnlyList<(string Title, string Icon, string Path)>> NamedFolders = new(() =>
    {
        var list = new List<(string, string, string)>();
        void Add(string title, string icon, Guid folderId)
        {
            var path = NativeMethods.GetKnownFolderPath(folderId);
            if (!string.IsNullOrEmpty(path)) list.Add((title, icon, path));
        }
        Add("Desktop",   "📂", Desktop);
        Add("Documents", "📄", Documents);
        Add("Downloads", "📥", Downloads);
        Add("Pictures",  "🖼", Pictures);
        Add("Music",     "🎵", Music);
        Add("Videos",    "🎬", Videos);
        return list;
    });

    /// <summary>A cheap catalog stub: metadata only, no content factory (the editor reads Title/Icon/params).</summary>
    private static Page FolderPage(string title, string icon, string path) => new()
    {
        Title      = title,
        Icon       = icon,
        PageKind   = PageKind,
        PageParams = new() { ["mode"] = "path", ["path"] = path },
    };

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var mode = pageParams?.GetValueOrDefault("mode") ?? "thispc";

        if (mode == "path" && pageParams!.TryGetValue("path", out var path))
        {
            var label = pageParams.GetValueOrDefault("label") ?? "Files";
            var tab = new Page
            {
                Title       = label,
                Icon        = "📁",
                PageKind    = PageKind,
                PageParams  = pageParams,
                Breadcrumbs = { new BreadcrumbSegment { Label = label } }
            };
            tab.ContentFactory = () => CreateView(new FileSystemViewModel(path, shell, ai, configs), tab);
            return tab;
        }
        else
        {
            var tab = new Page
            {
                Title       = "This PC",
                Icon        = "🖥",
                PageKind    = PageKind,
                PageParams  = pageParams ?? new() { ["mode"] = "thispc" },
                Breadcrumbs = { new BreadcrumbSegment { Label = "This PC" } }
            };
            tab.ContentFactory = () => CreateView(FileSystemViewModel.CreateThisPc(shell, ai, configs), tab);
            return tab;
        }
    }

    private static FileSystemView CreateView(FileSystemViewModel fsVm, Page tab)
    {
        var keyHandler = new FileSystemKeyboardHandler(fsVm);
        var dropTarget = new FileSystemDropTarget(fsVm);
        var page       = new FileSystemView(fsVm, keyHandler, dropTarget);

        bool applying = false;
        page.NavigationChanged += segments =>
        {
            if (applying) return;
            applying = true;
            try { ApplyBreadcrumbs(tab, page, segments); }
            finally { applying = false; }
        };
        return page;
    }

    private static void ApplyBreadcrumbs(
        Page tab,
        FileSystemView page,
        IReadOnlyList<(string Label, string Path)> segments)
    {
        var crumbs = segments.Select((seg, i) =>
        {
            var capturedPath = seg.Path;
            var isLast = i == segments.Count - 1;
            return new BreadcrumbSegment
            {
                Label    = seg.Label,
                Navigate = isLast ? null : (string.IsNullOrEmpty(capturedPath)
                    ? () => page.ViewModel.GoToThisPc(rebuildTree: true)
                    : () => page.ViewModel.NavigateTo(capturedPath))
            };
        }).ToList();

        var currentLabel = segments[^1].Label;
        tab.Title = currentLabel.Length > 15 ? currentLabel[..10] + "…" : currentLabel;
        tab.PageParams = string.IsNullOrEmpty(segments[^1].Path)
            ? new Dictionary<string, string> { ["mode"] = "thispc" }
            : new Dictionary<string, string> { ["mode"] = "path", ["path"] = segments[^1].Path };

        tab.Breadcrumbs.Clear();
        foreach (var c in crumbs) tab.Breadcrumbs.Add(c);
    }
}
