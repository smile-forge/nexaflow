using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Font.ClientTools;
using Nexaflow.Features.Font.Decoding;
using Nexaflow.Features.Font.Views;

namespace Nexaflow.Features.Font.ViewModels;

/// <summary>
/// The Font viewer page: an editable preview string rendered simultaneously across a list of chosen
/// fonts (compare mode) plus a details panel for the selected font. Opens blank (standalone compare) or
/// with one-or-more fonts preloaded + the first preselected (the "As Font" action). Fonts come from the
/// installed set (picker overlay) or files; WOFF is decoded through <see cref="FontSourceResolver"/>.
/// </summary>
public sealed partial class FontViewModel : ObservableObject, IPageViewModel, IContextPreview, IDisposable
{
    private readonly IShellServices _shell;
    private readonly FontSourceResolver _resolver = new();

    /// <summary>Shared preview settings (text, size, bold/italic/underline) attached to every compare item.</summary>
    public FontPreviewOptions Options { get; } = new();

    public ObservableCollection<FontItemViewModel> Fonts { get; } = [];

    /// <summary>True once at least one font has been added — drives the empty-state hint.</summary>
    public bool HasFonts => Fonts.Count > 0;

    [ObservableProperty] private FontItemViewModel? _selectedFont;

    public FontViewModel(IEnumerable<string>? paths, IShellServices shell)
    {
        _shell = shell;
        Fonts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFonts));
            // Search hits are list positions, so adding or removing a font invalidates them. Dropping the
            // search is the honest response — silently renumbering would step "next match" onto a row that
            // never matched.
            ClearSearch();
        };

        var list = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
        foreach (var p in list) LoadFontsFromFile(p, preselect: SelectedFont is null);
    }

    partial void OnSelectedFontChanged(FontItemViewModel? value) => value?.EnsureFacesLoaded();

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddFont() =>
        _shell.ShowOverlay(new FontPickerViewModel(_shell, OnPickInstalled, OnPickFile));

    private void OnPickInstalled(FontItemViewModel item) => AddItem(item, select: true);

    private void OnPickFile(string path) => LoadFontsFromFile(path, preselect: true);

    private void AddItem(FontItemViewModel item, bool select)
    {
        item.AttachOptions(Options);   // so the header toggles + this item's face both drive its preview
        Fonts.Add(item);
        if (select) SelectedFont = item;
    }

    [RelayCommand]
    private void RemoveFont(FontItemViewModel? item)
    {
        if (item is null) return;
        int idx = Fonts.IndexOf(item);
        Fonts.Remove(item);
        if (SelectedFont == item)
            SelectedFont = Fonts.Count == 0 ? null : Fonts[Math.Min(idx, Fonts.Count - 1)];
    }

    [RelayCommand]
    private void CopyName()
    {
        if (SelectedFont is { } f) TrySetClipboard(f.DisplayName);
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedFont?.SourcePath is { } p) TrySetClipboard(p);
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the fonts at <paramref name="paths"/> into the compare list (deduping by path) and selects
    /// the first. Called from the view's <c>Reinitialize</c> when the shell re-routes this tab a font set.
    /// </summary>
    public void OpenFiles(IEnumerable<string> paths)
    {
        FontItemViewModel? first = null;
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var existing = Fonts.FirstOrDefault(f =>
                string.Equals(f.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            first ??= existing ?? LoadFontsFromFile(path, preselect: false);
        }
        if (first is not null) SelectedFont = first;
    }

    /// <summary>Loads one file (which may yield several families / an error item); returns the first added.</summary>
    private FontItemViewModel? LoadFontsFromFile(string path, bool preselect)
    {
        FontItemViewModel? first = null;
        try
        {
            var uri = _resolver.ResolveRenderableUri(path);
            if (uri is null)
            {
                var ext = Path.GetExtension(path);
                first = AddFailed(path, $"WPF can't render {ext} fonts (only .ttf/.otf/.ttc, and .woff via decode).");
            }
            else
            {
                bool decoded = !string.Equals(Path.GetExtension(path), Path.GetExtension(uri.LocalPath),
                    StringComparison.OrdinalIgnoreCase);
                var families = FontFileLoader.LoadFamilies(uri);
                if (families.Count == 0)
                    first = AddFailed(path, "No font families found in the file.");
                foreach (var family in families)
                {
                    var item = FontItemViewModel.FromFile(family, path, decoded);
                    AddItem(item, select: false);
                    first ??= item;
                }
            }
        }
        catch (Exception ex)
        {
            first = AddFailed(path, ex.Message);
        }

        if (preselect && first is not null) SelectedFont = first;
        return first;
    }

    private FontItemViewModel AddFailed(string path, string error)
    {
        var item = FontItemViewModel.Failed(path, error);
        AddItem(item, select: false);
        return item;
    }

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); } catch { /* clipboard can be locked by another process */ }
    }

    // ── AI resolution ────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a client-tool font reference to a list item: a 1-based index (matching
    /// <see cref="GetContext"/>'s numbering), an exact name, then a name substring — or the selected /
    /// first renderable font when the reference is blank. Null if nothing matches.
    /// </summary>
    public FontItemViewModel? ResolveFont(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return SelectedFont ?? Fonts.FirstOrDefault(f => f.CanRender);

        var r = reference.Trim();
        if (int.TryParse(r, out var idx) && idx >= 1 && idx <= Fonts.Count)
            return Fonts[idx - 1];

        return Fonts.FirstOrDefault(f => string.Equals(f.DisplayName, r, StringComparison.OrdinalIgnoreCase))
            ?? Fonts.FirstOrDefault(f => f.DisplayName.Contains(r, StringComparison.OrdinalIgnoreCase));
    }

    // ── IPageViewModel ───────────────────────────────────────────────────────

    public string GetContext()
    {
        // No style qualifier: the page has no bold/italic/underline toggles, so each row renders in its
        // own selected face and saying "regular" would misdescribe a face that is itself bold or italic.
        if (Fonts.Count == 0)
            return "Font viewer, empty (blank compare mode) — no fonts added yet. The user would render " +
                   $"the preview text \"{Options.PreviewText}\" at {Options.PreviewSizePt:0}pt.";

        var sb = new StringBuilder();
        sb.Append($"Font viewer comparing {Fonts.Count} font(s), each rendering the same sample text so the " +
                  "user can compare them. Fonts:");
        for (int i = 0; i < Fonts.Count; i++)
        {
            var f = Fonts[i];
            var mark = f == SelectedFont ? " (selected)" : "";
            var status = f.CanRender ? f.IsInstalled ? "installed" : f.IsDecoded ? "from WOFF file" : "from file"
                                     : "UNREADABLE";
            sb.Append($" {i + 1}. {f.DisplayName} [{status}]{mark};");
        }
        sb.Append($" Preview text (large line): \"{Options.PreviewText}\" at {Options.PreviewSizePt:0}pt, " +
                  "each in that font's selected face.");
        sb.Append($" Each row also shows this small specimen line: \"{Options.SpecimenText}\".");
        return sb.ToString();
    }

    // ── IContextPreview ──────────────────────────────────────────────────────

    /// <summary>
    /// A scrollable, read-only list of the compared fonts for the conversation context panel: each font's
    /// Identity rows above an "abcde" specimen set in that font, so the panel shows what the page shows
    /// without re-hosting the live compare list (an element cannot have two parents).
    /// </summary>
    /// <remarks>
    /// A fresh control every time the chip is selected, per the contract — it binds to the page's
    /// <see cref="Fonts"/> items but owns none of them, and is discarded on deselect.
    /// </remarks>
    public UserControl CreateContextPreview() => new FontContextPreview { DataContext = this };

    /// <summary>
    /// The scope this page's tools act within — a signature of the compared font set, so two font pages
    /// pinned together don't collapse first-wins in the conversation hub. There's no single backing file
    /// (fonts are installed or loaded from files), so the scope is each font's source path — or its
    /// display name when installed (no path) — joined in list order, with a constant for the empty
    /// compare list. Deterministic for a given set.
    /// </summary>
    public string? GetSecurityContext() =>
        Fonts.Count == 0
            ? "font-viewer (empty)"
            : "fonts: " + string.Join(" | ", Fonts.Select(f => f.SourcePath ?? f.DisplayName));

    public IReadOnlyList<IClientTool> GetClientTools() =>
        [new FontDetailsTool(this), new FontRenderTool(this)];

    public string? GetAiSystemPromptGuidance() =>
        "This is a font viewer/comparison page. Fonts are listed in the context by a 1-based index and " +
        "name. Use get_font_details to read any listed font's metadata, and render_font_preview to see a " +
        "listed font rendered as an image (you cannot otherwise see glyph shapes). Reference a font by its " +
        "index or name.";

    public void Dispose() => _resolver.Dispose();
}
