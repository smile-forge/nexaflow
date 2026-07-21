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
    /// attributes below, which come from the selected face alone.</summary>
    public FontPreviewOptions? Options { get; private set; }

    public void AttachOptions(FontPreviewOptions options)
    {
        Options = options;
        RaiseEffective();
        OnPropertyChanged(nameof(Options));
    }

    // How a preview row renders: entirely the selected face's own metrics. There are no bold / italic /
    // underline overrides — the UI has no style toggles, so a face is shown exactly as it is designed.

    public FontWeight EffectiveWeight => SelectedFace?.Weight ?? FontWeights.Normal;

    public FontStyle EffectiveStyle => SelectedFace?.Style ?? FontStyles.Normal;

    public FontStretch EffectiveStretch => SelectedFace?.Stretch ?? FontStretches.Normal;

    private void RaiseEffective()
    {
        OnPropertyChanged(nameof(EffectiveWeight));
        OnPropertyChanged(nameof(EffectiveStyle));
        OnPropertyChanged(nameof(EffectiveStretch));
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

    /// <summary>
    /// Just the Identity rows the details panel shows first — family, face, source, format, faces-in-file —
    /// with no heading. The AI context preview lists these above a small specimen, where the metrics, legal
    /// and technical groups would be noise in a ~35%-width panel. A font that failed to load reports why.
    /// </summary>
    public IReadOnlyList<DetailRow> IdentityRows
    {
        get
        {
            if (!CanRender) return [new DetailRow("Status", LoadError ?? "Could not load font.")];
            var rows = new List<DetailRow>();
            AddIdentityRows(rows, Representative, SelectedFace ?? Representative);
            return rows;
        }
    }

    /// <summary>The identity label→value pairs, shared by the details panel and the context preview so the
    /// two can never drift.</summary>
    private void AddIdentityRows(List<DetailRow> rows, FontFaceViewModel? rep, FontFaceViewModel? face)
    {
        Add(rows, "Family", DisplayName);
        Add(rows, "Face", face?.FaceName);
        Add(rows, "Sample text", face?.SampleText);
        Add(rows, "Source", SourceLabel);
        Add(rows, "Format", DescribeFormat(rep));
        Add(rows, "Faces in file", Faces.Count.ToString());
    }

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
        AddIdentityRows(rows, rep, face);

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

    /// <summary>The face's mapped code points. An empty result is NOT cached on the instance: it means
    /// either a degenerate font or a read that lost the race in <see cref="SnapshotCodePoints"/>, and the
    /// latter must be able to heal on the next look rather than leaving the glyph map permanently blank.</summary>
    private IReadOnlyList<int> AllCodePoints
    {
        get
        {
            if (_allCodePoints is { Count: > 0 }) return _allCodePoints;
            var built = BuildCodePoints();
            if (built.Count > 0) _allCodePoints = built;
            return built;
        }
    }

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

        return SnapshotCodePoints(glyph)
            .Where(cp => cp >= 0x20 && cp != 0x7F && !(cp >= 0x80 && cp <= 0x9F)
                         && cp <= 0x10FFFF && !(cp >= 0xD800 && cp <= 0xDFFF))
            .OrderBy(cp => cp)
            .ToList();
    }

    /// <summary>Serialises our own glyph-map reads. See <see cref="SnapshotCodePoints"/>.</summary>
    private static readonly Lock GlyphMapGate = new();

    /// <summary>
    /// Code points already read, keyed by face identity (file + weight/style/stretch) rather than by
    /// <see cref="GlyphTypeface"/> instance — <c>FontFamily.GetTypefaces()</c> hands back fresh instances
    /// each call, so instance keys would never hit. Two wins: a large CJK map is enumerated once per face
    /// for the whole process instead of per compare row, and the lazy-population race below can only be
    /// lost on the very first read of a face.
    /// </summary>
    private static readonly Dictionary<(string, int, int, int), int[]> CodePointsByFace = new();

    /// <summary>
    /// The face's mapped code points, copied out defensively.
    /// <para>
    /// <see cref="GlyphTypeface.CharacterToGlyphMap"/> is populated lazily and the
    /// <see cref="GlyphTypeface"/> is shared per face, so two threads first touching the same face race —
    /// the AI's <c>get_font_details</c> / <c>render_font_preview</c> tools read a font off the UI thread
    /// while the user expands the glyph map. The race surfaces two different ways: a mid-flight enumeration
    /// throws <see cref="InvalidOperationException"/>, and <c>ToArray</c> throws
    /// <see cref="ArgumentException"/> because it pre-sizes the destination from <c>Count</c> and then
    /// copies. The gate removes our own contention; the retry covers population driven from elsewhere
    /// (WPF's own text rendering), and settles because population happens once.
    /// </para>
    /// </summary>
    private static int[] SnapshotCodePoints(GlyphTypeface glyph)
    {
        var key = (glyph.FontUri?.ToString() ?? string.Empty,
                   glyph.Weight.ToOpenTypeWeight(), glyph.Style.GetHashCode(), glyph.Stretch.ToOpenTypeStretch());

        lock (GlyphMapGate)
        {
            if (CodePointsByFace.TryGetValue(key, out var cached)) return cached;

            // Population completes in microseconds, so yielding between attempts converges quickly; the
            // cap only exists so a pathological font can never spin here forever.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    // Accumulated one key at a time rather than via ToArray(), so a map that grows while
                    // being read costs a retry rather than a pre-sized-array failure.
                    List<int> codePoints = [];
                    foreach (var codePoint in glyph.CharacterToGlyphMap.Keys) codePoints.Add(codePoint);

                    var snapshot = codePoints.ToArray();
                    // Only a real read is remembered: caching an empty degrade would make it permanent.
                    if (snapshot.Length > 0) CodePointsByFace[key] = snapshot;
                    return snapshot;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    // Give up rather than fail the whole details panel — an empty glyph map degrades to
                    // "No glyphs." while every other row still renders, and the next look retries.
                    if (attempt >= 50) return [];
                    Thread.Yield();
                }
            }
        }
    }
}
