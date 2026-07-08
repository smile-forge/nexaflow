using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexaflow.Features.Font.ViewModels;

/// <summary>
/// One font in the compare list (and one row in the picker) — uniform across an installed family, a
/// font loaded from a file, and a WOFF decoded to a temp sfnt: all resolve their faces/metadata the
/// same way, through <see cref="FontFamily.GetTypefaces"/> → <see cref="GlyphTypeface"/>. Faces and
/// details resolve lazily on first selection so the compare list stays cheap while scrolling.
/// </summary>
public sealed partial class FontItemViewModel : ObservableObject
{
    private const int GlyphPageSize = 500;

    public FontFamily FontFamily { get; }
    public string DisplayName { get; }
    public string? SourcePath { get; }
    public bool IsDecoded { get; }
    public bool CanRender { get; }
    public string? LoadError { get; }

    /// <summary>True for a font from the installed system set (no backing file path).</summary>
    public bool IsInstalled => SourcePath is null;

    private FontItemViewModel(FontFamily family, string displayName, string? sourcePath,
                             bool decoded, bool canRender, string? error)
    {
        FontFamily = family;
        DisplayName = displayName;
        SourcePath = sourcePath;
        IsDecoded = decoded;
        CanRender = canRender;
        LoadError = error;
    }

    public static FontItemViewModel Installed(FontFamily family) =>
        new(family, FontNames.Display(family), sourcePath: null, decoded: false, canRender: true, error: null);

    public static FontItemViewModel FromFile(FontFamily family, string sourcePath, bool decoded) =>
        new(family, FontNames.Display(family), sourcePath, decoded, canRender: true, error: null);

    public static FontItemViewModel Failed(string sourcePath, string error) =>
        new(new FontFamily("Segoe UI"), Path.GetFileName(sourcePath), sourcePath,
            decoded: false, canRender: false, error: error);

    public string SourceLabel =>
        !CanRender ? "Could not load"
        : SourcePath is null ? "Installed"
        : IsDecoded ? "WOFF → decoded"
        : SourcePath;

    // ── Faces (lazy) ─────────────────────────────────────────────────────────

    private IReadOnlyList<FontFaceViewModel>? _faces;
    public IReadOnlyList<FontFaceViewModel> Faces => _faces ??= ResolveFaces();

    [ObservableProperty] private FontFaceViewModel? _selectedFace;

    public FontFaceViewModel? Representative =>
        Faces.FirstOrDefault(f => f.IsRegular) ?? Faces.FirstOrDefault();

    // ── Shared preview options + effective render attributes ──────────────────

    /// <summary>The page's shared preview settings (size + bold/italic/underline), attached when this item
    /// joins a compare list. The compare row binds text/size to these and weight/style to the effective
    /// attributes below, so the header toggles AND the selected face both drive the preview.</summary>
    public FontPreviewOptions? Options { get; private set; }

    public void AttachOptions(FontPreviewOptions options)
    {
        if (Options is not null) Options.PropertyChanged -= OnOptionsChanged;
        Options = options;
        Options.PropertyChanged += OnOptionsChanged;
        RaiseEffective();
        OnPropertyChanged(nameof(Options));
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FontPreviewOptions.IsBold) or nameof(FontPreviewOptions.IsItalic)
                              or nameof(FontPreviewOptions.IsUnderline))
            RaiseEffective();
    }

    /// <summary>Bold header toggle wins; otherwise the selected face's own weight.</summary>
    public FontWeight EffectiveWeight =>
        Options?.IsBold == true ? FontWeights.Bold : SelectedFace?.Weight ?? FontWeights.Normal;

    public FontStyle EffectiveStyle =>
        Options?.IsItalic == true ? FontStyles.Italic : SelectedFace?.Style ?? FontStyles.Normal;

    public FontStretch EffectiveStretch => SelectedFace?.Stretch ?? FontStretches.Normal;

    public TextDecorationCollection? EffectiveDecorations =>
        Options?.IsUnderline == true ? TextDecorations.Underline : null;

    private void RaiseEffective()
    {
        OnPropertyChanged(nameof(EffectiveWeight));
        OnPropertyChanged(nameof(EffectiveStyle));
        OnPropertyChanged(nameof(EffectiveStretch));
        OnPropertyChanged(nameof(EffectiveDecorations));
    }

    /// <summary>Resolves faces + selects a representative. Called when the item becomes the selection.</summary>
    public void EnsureFacesLoaded()
    {
        if (!CanRender) return;
        if (SelectedFace is null && Faces.Count > 0)
            SelectedFace = Representative;
    }

    private IReadOnlyList<FontFaceViewModel> ResolveFaces()
    {
        var list = new List<FontFaceViewModel>();
        if (!CanRender) return list;
        try
        {
            foreach (var tf in FontFamily.GetTypefaces())
                if (tf.TryGetGlyphTypeface(out var gt))
                    list.Add(new FontFaceViewModel(tf, gt));
        }
        catch { /* a face that won't resolve is simply omitted */ }
        return list;
    }

    // ── Details panel ────────────────────────────────────────────────────────

    public IReadOnlyList<DetailRow> Details => BuildDetails();

    partial void OnSelectedFaceChanged(FontFaceViewModel? oldValue, FontFaceViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        _allCodePoints = null;
        _glyphPage = 0;
        RaiseEffective();   // the chosen face changes how this font previews
        OnPropertyChanged(nameof(Details));
        RaiseGlyphState();
    }

    /// <summary>Picks a face (from a chip click) — shows it in the preview and its metrics.</summary>
    [RelayCommand]
    private void SelectFace(FontFaceViewModel? face) => SelectedFace = face;

    /// <summary>Copies a glyph to the clipboard so it can be pasted (like Character Map).</summary>
    [RelayCommand]
    private void CopyGlyph(string? glyph)
    {
        if (string.IsNullOrEmpty(glyph)) return;
        try { Clipboard.SetText(glyph); } catch { /* clipboard can be locked by another process */ }
    }

    private List<DetailRow> BuildDetails()
    {
        var rows = new List<DetailRow>();
        if (!CanRender)
        {
            rows.Add(DetailRow.Header("Font"));
            rows.Add(new DetailRow("Status", LoadError ?? "Could not load font."));
            return rows;
        }

        var rep = Representative;
        var face = SelectedFace ?? rep;

        // Identity
        rows.Add(DetailRow.Header("Identity"));
        Add(rows, "Family", DisplayName);
        Add(rows, "Face", face?.FaceName);
        Add(rows, "Sample text", face?.SampleText);
        Add(rows, "Source", SourceLabel);
        Add(rows, "Format", DescribeFormat(rep));
        Add(rows, "Faces in file", Faces.Count.ToString());

        // Style & metrics (per selected face)
        if (face is not null) rows.AddRange(face.MetricRows);

        // Legal & licensing (family-level, from the representative face)
        if (rep is not null)
        {
            var legal = new List<DetailRow> { DetailRow.Header("Legal & licensing") };
            Add(legal, "Copyright", FontNames.Pick(rep.Glyph.Copyrights));
            Add(legal, "Trademark", FontNames.Pick(rep.Glyph.Trademarks));
            Add(legal, "License", FontNames.Pick(rep.Glyph.LicenseDescriptions));
            Add(legal, "Embedding rights", rep.Glyph.EmbeddingRights.ToString());
            Add(legal, "Manufacturer", FontNames.Pick(rep.Glyph.ManufacturerNames));
            Add(legal, "Designer", FontNames.Pick(rep.Glyph.DesignerNames));
            Add(legal, "Designer URL", FontNames.Pick(rep.Glyph.DesignerUrls));
            Add(legal, "Vendor URL", FontNames.Pick(rep.Glyph.VendorUrls));
            if (legal.Count > 1) rows.AddRange(legal);

            // Technical
            var tech = new List<DetailRow> { DetailRow.Header("Technical") };
            Add(tech, "Version", FontNames.Pick(rep.Glyph.VersionStrings) ?? rep.Glyph.Version.ToString("0.0"));
            Add(tech, "Font file", rep.Glyph.FontUri?.LocalPath);
            Add(tech, "Description", FontNames.Pick(rep.Glyph.Descriptions));
            if (tech.Count > 1) rows.AddRange(tech);
        }

        return rows;
    }

    private string DescribeFormat(FontFaceViewModel? rep)
    {
        var uri = rep?.Glyph.FontUri?.LocalPath ?? SourcePath;
        var ext = uri is null ? null : Path.GetExtension(uri).ToLowerInvariant();
        return ext == ".otf" ? "OpenType (CFF)" : "TrueType";
    }

    private static void Add(List<DetailRow> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) rows.Add(new DetailRow(label, value!));
    }

    // ── Glyph specimen (lazy, paged) ─────────────────────────────────────────

    private IReadOnlyList<int>? _allCodePoints;
    [ObservableProperty] private int _glyphPage;

    private IReadOnlyList<int> AllCodePoints => _allCodePoints ??= BuildCodePoints();

    /// <summary>The current page of glyphs (Prev/Next walk through them all).</summary>
    public IReadOnlyList<string> GlyphSamples =>
        AllCodePoints.Skip(GlyphPage * GlyphPageSize).Take(GlyphPageSize)
            .Select(char.ConvertFromUtf32).ToList();

    public int GlyphPageCount => Math.Max(1, (AllCodePoints.Count + GlyphPageSize - 1) / GlyphPageSize);
    public bool HasGlyphPages => AllCodePoints.Count > GlyphPageSize;
    public bool CanPrevGlyphPage => GlyphPage > 0;
    public bool CanNextGlyphPage => GlyphPage < GlyphPageCount - 1;

    public string GlyphPageLabel
    {
        get
        {
            int total = AllCodePoints.Count;
            if (total == 0) return "No glyphs.";
            int start = GlyphPage * GlyphPageSize + 1;
            int end = Math.Min(start + GlyphPageSize - 1, total);
            return $"Glyphs {start:N0}–{end:N0} of {total:N0}";
        }
    }

    [RelayCommand]
    private void PrevGlyphPage() { if (CanPrevGlyphPage) GlyphPage--; }

    [RelayCommand]
    private void NextGlyphPage() { if (CanNextGlyphPage) GlyphPage++; }

    partial void OnGlyphPageChanged(int value) => RaiseGlyphState();

    private void RaiseGlyphState()
    {
        OnPropertyChanged(nameof(GlyphPage));
        OnPropertyChanged(nameof(GlyphSamples));
        OnPropertyChanged(nameof(GlyphPageLabel));
        OnPropertyChanged(nameof(GlyphPageCount));
        OnPropertyChanged(nameof(HasGlyphPages));
        OnPropertyChanged(nameof(CanPrevGlyphPage));
        OnPropertyChanged(nameof(CanNextGlyphPage));
    }

    private IReadOnlyList<int> BuildCodePoints()
    {
        var glyph = (SelectedFace ?? Representative)?.Glyph;
        if (glyph is null) return [];

        return glyph.CharacterToGlyphMap.Keys
            .Where(cp => cp >= 0x20 && cp != 0x7F && !(cp >= 0x80 && cp <= 0x9F)
                         && cp <= 0x10FFFF && !(cp >= 0xD800 && cp <= 0xDFFF))
            .OrderBy(cp => cp)
            .ToList();
    }
}
