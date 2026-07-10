using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Syntax;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>A lazily-expanded node in the snaplink file picker (folders + markdown/code files only).</summary>
public sealed partial class FileNode : ObservableObject
{
    private static readonly FileNode Placeholder = new("…", "", true, _ => []);
    private readonly Func<string, IReadOnlyList<FileNode>> _load;
    private bool _loaded;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDir { get; }
    public ObservableCollection<FileNode> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    public FileNode(string name, string fullPath, bool isDir, Func<string, IReadOnlyList<FileNode>> load)
    {
        Name = name; FullPath = fullPath; IsDir = isDir; _load = load;
        if (isDir) Children.Add(Placeholder);   // makes the expander appear; real children load on first expand
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _loaded) return;
        _loaded = true;
        Children.Clear();
        foreach (var c in _load(FullPath)) Children.Add(c);
    }
}

/// <summary>A candidate snaplink target in the picker (a heading, a class, a method, or the whole file).</summary>
public sealed record SnaplinkTarget(string Label, double Indent, Snaplink Link);

/// <summary>
/// Drives the inline snaplink picker: a file tree rooted at the product root (markdown + code files), and —
/// once a file is selected — its choosable targets (markdown headings, or classes→methods via
/// <see cref="CodeStructureExtractor"/>), plus a whole-file option. Produces a <see cref="Snaplink"/>.
/// </summary>
public sealed partial class SnaplinkPickerViewModel : ObservableObject
{
    private static readonly CodeStructureExtractor Extractor = new();
    private readonly string _root;

    public ObservableCollection<FileNode> Roots { get; } = [];
    public ObservableCollection<SnaplinkTarget> Targets { get; } = [];

    [ObservableProperty] private SnaplinkTarget? _selectedTarget;
    [ObservableProperty] private string _selectedFileLabel = "Pick a file from the tree";

    public SnaplinkPickerViewModel(string root)
    {
        _root = root;
        foreach (var n in Load(root)) Roots.Add(n);
    }

    private IReadOnlyList<FileNode> Load(string dir)
    {
        var result = new List<FileNode>();
        try
        {
            foreach (var d in Directory.GetDirectories(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(d);
                if (Skip(name)) continue;
                result.Add(new FileNode(name, d, true, Load));
            }
            foreach (var f in Directory.GetFiles(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(f);
                if (IsRelevant(name)) result.Add(new FileNode(name, f, false, Load));
            }
        }
        catch { /* unreadable dir → nothing */ }
        return result;
    }

    private static bool Skip(string name) =>
        name.StartsWith('.') || name is "bin" or "obj" or "node_modules" or "packages";

    private static bool IsRelevant(string name) =>
        name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)
        || HighlightingRegistry.IsStructured(name);

    /// <summary>Called (from the view) when the tree selection changes — rebuilds the target list.</summary>
    public void SelectFile(FileNode? node)
    {
        Targets.Clear();
        SelectedTarget = null;
        if (node is null || node.IsDir) { SelectedFileLabel = "Pick a file from the tree"; return; }

        var rel = Path.GetRelativePath(_root, node.FullPath).Replace('\\', '/');
        SelectedFileLabel = rel;

        var text = ReadText(node.FullPath);
        var isMarkdown = node.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                         || node.Name.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

        if (isMarkdown && text is not null) BuildMarkdown(rel, text);
        else if (HighlightingRegistry.Resolve(node.Name).TreeSitterLanguage is { } grammar && text is not null)
            BuildCode(rel, grammar, text, Path.GetDirectoryName(node.FullPath));
        else
            Targets.Add(new SnaplinkTarget("(whole file)", 0, new Snaplink { Type = isMarkdown ? "markdown" : "code", Doc = rel }));

        SelectedTarget = Targets.FirstOrDefault();
    }

    private void BuildMarkdown(string rel, string text)
    {
        Targets.Add(new SnaplinkTarget("(whole file)", 0, new Snaplink { Type = "markdown", Doc = rel }));
        // SnaplinkTargets owns heading parsing, so the picker and the validator agree on what a title_path is.
        foreach (var path in SnaplinkTargets.HeadingPaths(text))
            Targets.Add(new SnaplinkTarget(path[^1], (path.Count - 1) * 14,
                new Snaplink { Type = "markdown", Doc = rel, TitlePath = [.. path] }));
    }

    private void BuildCode(string rel, string grammar, string text, string? dir)
    {
        Targets.Add(new SnaplinkTarget("(whole file)", 0, new Snaplink { Type = "code", Doc = rel }));
        var outline = Extractor.Extract(grammar, text, dir);
        foreach (var t in outline.Types)
        {
            Targets.Add(new SnaplinkTarget(t.Name, 0, new Snaplink { Type = "code", Doc = rel, Class = t.Name, Ast = t.AstPath }));
            foreach (var m in t.Members)
                Targets.Add(new SnaplinkTarget(m.Signature, 16,
                    new Snaplink { Type = "code", Doc = rel, Class = t.Name, Method = m.Name, Ast = m.AstPath }));
        }
        foreach (var m in outline.TopLevel)
            Targets.Add(new SnaplinkTarget(m.Signature, 0, new Snaplink { Type = "code", Doc = rel, Method = m.Name, Ast = m.AstPath }));
    }

    /// <summary>A fresh snaplink for the current selection (so it isn't shared with the picker's template).</summary>
    public Snaplink? BuildSelectedLink()
    {
        if (SelectedTarget is not { Link: { } t }) return null;
        return new Snaplink
        {
            Type = t.Type, Doc = t.Doc, TitlePath = t.TitlePath is null ? null : [.. t.TitlePath],
            Class = t.Class, Method = t.Method, Ast = t.Ast, Status = Status.Should
        };
    }

    private static string? ReadText(string path) => SnaplinkTargets.ReadText(path);
}
