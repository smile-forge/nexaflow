using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.Editing.Wpf;
using Nexaflow.Features.Images.Services;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// The layers panel of a layered picture: its layers, top first, with thumbnails; the active layer's opacity, blend and locks;
/// the edits made to layers — added, grouped, copied, removed, moved, merged, flattened, turned to pixels — and to their
/// masks; the canvas's size; and what a selection does to the active layer — delete, fill, copy, cut, paste, crop. Every edit
/// is a step of the picture's history; which layer is active, and whether its mask is, are part of the picture but no step.
/// </summary>
public sealed partial class LayersViewModel : ObservableObject
{
    /// <summary>The longest side a thumbnail is drawn at.</summary>
    public const int ThumbnailSide = 48;
    private readonly IShellServices? _shell;
    private readonly Func<Action, Task> _runOnUi;
    private readonly Func<LayeredPicture, SKColorF> _colour;
    private LayeredSession? _document;
    // What was copied last: the picture on the clipboard by its hash, where it lay, and its pixels as deep as they were held —
    // what a paste of that same picture puts back, so copying within a deep picture loses nothing.
    private (string Hash, SKRectI At, TiledPixels Pixels)? _copied;
    private CancellationTokenSource? _thumbnails;

    /// <param name="colour">The colour a fill lays down: the colour chosen for markup.</param>
    /// <param name="colour">The colour a fill lays down, in a picture's values.</param>
    public LayersViewModel(IShellServices? shell, Func<Action, Task> runOnUi, Func<LayeredPicture, SKColorF> colour)
    {
        _shell = shell;
        _runOnUi = runOnUi;
        _colour = colour;
        BlendChoices = [.. LayerBlend.Offered.Select(o => new LayerBlendChoice(o.Blend, Str.Get($"Images.Layers.Blend.{o.Blend.Key}")))];
    }

    public ObservableCollection<LayerItem> Items { get; } = [];

    /// <summary>A layered picture is open.</summary>
    public bool HasDocument => _document is not null;

    /// <summary>The layer whose mask is shown alone instead of the layers; null while none is.</summary>
    [ObservableProperty] private Guid? _maskAlone;

    /// <summary>An edit that turns layers to pixels at full size is being made.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RasterizeCommand), nameof(MergeDownCommand), nameof(FlattenCommand))]
    private bool _isBusy;

    /// <summary>The layered picture to follow — called again whenever it changes.</summary>
    public void Follow(LayeredSession? document)
    {
        if (!ReferenceEquals(document, _document))
        {
            _document = document;
            Items.Clear();
            MaskAlone = null;
            OnPropertyChanged(nameof(HasDocument));
        }
        Rebuild();
    }

    private LayeredPicture? Picture => _document?.Picture;

    private Layer? Active => Picture?.Active;

    // ── The rows ────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        if (Picture is not { } picture)
        {
            Items.Clear();
            Refresh();
            return;
        }
        var rows = new List<(Layer Layer, int Depth)>();
        void Walk(ImmutableArray<Layer> layers, int depth)
        {
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                rows.Add((layers[i], depth));
                if (layers[i] is GroupLayer group) Walk(group.Children, depth + 1);
            }
        }
        Walk(picture.Layers, 0);

        var existing = Items.ToDictionary(i => i.Id);
        var stale = new List<(LayerItem Item, Layer Layer)>();
        for (int index = 0; index < rows.Count; index++)
        {
            var (layer, depth) = rows[index];
            if (!existing.TryGetValue(layer.Id, out var item)) item = new LayerItem(layer.Id);
            if (index < Items.Count) { if (!ReferenceEquals(Items[index], item)) { Items.Remove(item); Items.Insert(index, item); } }
            else Items.Add(item);
            item.Name = layer.Name;
            item.Kind = KindOf(layer);
            item.Depth = depth;
            item.Visible = layer.Visible;
            item.Locked = layer.Locked;
            item.AlphaLocked = layer.AlphaLocked;
            item.ClipsToBelow = layer.ClipsToBelow;
            item.HasMask = layer.Mask is not null;
            item.MaskEnabled = layer.Mask?.Enabled == true;
            item.IsActive = picture.ActiveId == layer.Id;
            item.IsMaskActive = item.IsActive && picture.EditsMask && layer.Mask is not null;
            item.Detail = DetailOf(layer);
            if (!ReferenceEquals(item.Shown, layer)) stale.Add((item, layer));   // drawn once its thumbnail lands, not before
        }
        while (Items.Count > rows.Count) Items.RemoveAt(Items.Count - 1);
        if (MaskAlone is { } alone && picture.Find(alone)?.Mask is null) MaskAlone = null;
        if (stale.Count > 0) DrawThumbnails(stale);
        Refresh();
    }

    private static LayerKind KindOf(Layer layer) => layer switch
    {
        PictureLayer => LayerKind.Picture,
        MarkupLayer => LayerKind.Markup,
        GroupLayer => LayerKind.Group,
        UndrawableLayer => LayerKind.Undrawable,
        _ => LayerKind.Pixels,
    };

    /// <summary>What the row says beneath the name: its blend and opacity where not Normal and full, or why it is not drawn.</summary>
    private string DetailOf(Layer layer)
    {
        if (layer is UndrawableLayer undrawable) return Str.Format("Images.Layers.UndrawableFormat", undrawable.Kind);
        var parts = new List<string>();
        if (layer.Blend != LayerBlend.Normal) parts.Add(BlendLabel(layer.Blend));
        if (layer.Opacity < 1) parts.Add(Str.Format("Images.Layers.OpacityFormat", Math.Round(layer.Opacity * 100)));
        return string.Join(" · ", parts);
    }

    private string BlendLabel(LayerBlend blend) => BlendChoices.FirstOrDefault(c => c.Blend == blend)?.Label ?? blend.Op;

    /// <summary>The thumbnails of the rows whose layers changed, drawn off the UI thread — a later change lets an earlier drawing go.</summary>
    private void DrawThumbnails(IReadOnlyList<(LayerItem Item, Layer Layer)> stale)
    {
        if (_document is not { } document) return;
        _thumbnails?.Cancel();
        var cts = new CancellationTokenSource();
        _thumbnails = cts;
        var (width, height) = (document.Picture.Width, document.Picture.Height);
        _ = Task.Run(() =>
        {
            foreach (var (item, layer) in stale)
            {
                if (cts.IsCancellationRequested) return;
                using var picture = document.ThumbnailOf(layer, ThumbnailSide);
                var thumbnail = picture is null ? null : SkiaWpf.ToFrozenBitmap(picture);
                var mask = layer.Mask is { } m ? MaskThumbnail(m.Coverage, width, height) : null;
                // A picture layer not decoded yet has nothing to show: it stays to be drawn once it is (RedrawThumbnails).
                bool drawn = thumbnail is not null || layer is not PictureLayer;
                _ = _runOnUi(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    item.Thumbnail = thumbnail;
                    item.MaskThumbnail = mask;
                    if (drawn) item.Shown = layer;
                });
            }
        });
    }

    /// <summary>A layer's picture has pixels now — its first decode — so the thumbnails not drawn yet are drawn.</summary>
    internal void RedrawThumbnails()
    {
        if (_document is not null) Rebuild();
    }

    private static BitmapSource MaskThumbnail(TiledCoverage coverage, int width, int height)
    {
        double scale = Math.Min(1, (double)ThumbnailSide / Math.Max(width, height));
        int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
        var grey = coverage.Resampled(w, h).ToBytes();
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, grey, w);
        bitmap.Freeze();
        return bitmap;
    }

    // ── The active layer ────────────────────────────────────────────────────

    /// <summary>The blends offered: OpenRaster's own, and the active layer's where another application's.</summary>
    public IReadOnlyList<LayerBlendChoice> BlendChoices { get; }

    public bool HasActive => Active is not null;

    /// <summary>The active layer's opacity, 0…100.</summary>
    public double Opacity
    {
        get => Math.Round((Active?.Opacity ?? 1) * 100);
        set => Change(l => l with { Opacity = Math.Clamp(value, 0, 100) / 100 });
    }

    public LayerBlendChoice? Blend
    {
        get => Active is { } layer ? BlendChoices.FirstOrDefault(c => c.Blend == layer.Blend) ?? new LayerBlendChoice(layer.Blend, layer.Blend.Op) : null;
        set
        {
            if (value is not null) Change(l => l with { Blend = value.Blend });
        }
    }

    public bool ActiveClips
    {
        get => Active?.ClipsToBelow == true;
        set => Change(l => l with { ClipsToBelow = value });
    }

    public bool ActiveAlphaLocked
    {
        get => Active?.AlphaLocked == true;
        set => Change(l => l with { AlphaLocked = value }, evenLocked: true);
    }

    public bool ActiveLocked
    {
        get => Active?.Locked == true;
        set => Change(l => l with { Locked = value }, evenLocked: true);
    }

    /// <summary>The active layer is a group: what Isolated says applies.</summary>
    public bool ActiveIsGroup => Active is GroupLayer;

    public bool ActiveIsolated
    {
        get => (Active as GroupLayer)?.Isolated ?? true;
        set => Change(l => l is GroupLayer g ? g with { Isolated = value } : l);
    }

    /// <summary>The active layer renamed.</summary>
    public string ActiveName
    {
        get => Active?.Name ?? "";
        set
        {
            if (!string.IsNullOrWhiteSpace(value)) Change(l => l with { Name = value.Trim() }, evenLocked: true);
        }
    }

    /// <summary>The active layer made what <paramref name="change"/> makes of it — unless it is locked, where only the lock and
    /// name may change.</summary>
    private void Change(Func<Layer, Layer> change, bool evenLocked = false)
    {
        if (_document is not { } document || Active is not { } layer || (layer.Locked && !evenLocked)) return;
        document.Commit(p => p.WithLayer(layer.Id, change));
    }

    /// <summary>The row clicked: its layer becomes active, its mask not — no step of its own.</summary>
    [RelayCommand]
    private void Activate(LayerItem? item)
    {
        if (item is null || _document is not { } document) return;
        document.Amend(p => p with { ActiveId = item.Id, EditsMask = false });
    }

    /// <summary>The mask's thumbnail clicked: its layer becomes active, and the tools work on the mask, in greys.</summary>
    [RelayCommand]
    private void ActivateMask(LayerItem? item)
    {
        if (item is null || _document is not { } document || document.Picture.Find(item.Id)?.Mask is null) return;
        document.Amend(p => p with { ActiveId = item.Id, EditsMask = true });
    }

    /// <summary>Alt-click on a mask: shown alone instead of the layers, or the layers again.</summary>
    [RelayCommand]
    private void ShowMaskAlone(LayerItem? item)
    {
        if (item is null) return;
        MaskAlone = MaskAlone == item.Id ? null : item.Id;
    }

    /// <summary>Ctrl-click on a mask: it is loaded as the selection.</summary>
    [RelayCommand]
    private void LoadMaskAsSelection(LayerItem? item)
    {
        if (item is null || _document is not { } document || document.Picture.Find(item.Id)?.Mask is not { } mask) return;
        document.Commit(p => p with { Selection = mask.Coverage });
    }

    /// <summary>Shift-click on a mask: used, or set aside.</summary>
    [RelayCommand]
    private void ToggleMaskEnabled(LayerItem? item)
    {
        if (item is null || _document is not { } document) return;
        document.Commit(p => p.WithLayer(item.Id, l => l.Mask is { } m && !l.Locked ? l with { Mask = m with { Enabled = !m.Enabled } } : l));
    }

    [RelayCommand]
    private void ToggleVisible(LayerItem? item)
    {
        if (item is null || _document is not { } document) return;
        document.Commit(p => p.WithLayer(item.Id, l => l with { Visible = !l.Visible }));
    }

    [RelayCommand]
    private void ToggleLocked(LayerItem? item)
    {
        if (item is null || _document is not { } document) return;
        document.Commit(p => p.WithLayer(item.Id, l => l with { Locked = !l.Locked }));
    }

    // ── Layers ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasDocumentNow))]
    private void AddPixelLayer() => Insert(new PixelLayer { Name = NextName("Images.Layers.PixelName") });

    [RelayCommand(CanExecute = nameof(HasDocumentNow))]
    private void AddMarkupLayer() => Insert(new MarkupLayer { Name = NextName("Images.Layers.MarkupName") });

    /// <summary>The active layer put in a new group of its own, in its place.</summary>
    [RelayCommand(CanExecute = nameof(HasActive))]
    private void Group()
    {
        if (_document is not { } document || Active is not { } layer || document.Picture.Locate(layer.Id) is not { } at) return;
        var group = new GroupLayer { Name = NextName("Images.Layers.GroupName"), Children = [layer] };
        document.Commit(p => p.WithSiblings(layer.Id, s => s.SetItem(at.Index, group)) with { ActiveId = group.Id, EditsMask = false });
    }

    [RelayCommand(CanExecute = nameof(HasActive))]
    private void Duplicate()
    {
        if (Active is not { } layer) return;
        Insert(LayerEdits.Duplicate(layer, Str.Format("Images.Layers.CopyNameFormat", layer.Name)));
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (_document is not { } document || Active is not { } layer) return;
        document.Commit(p => p.Without(layer.Id));
    }

    private bool CanDelete() => Active is { Locked: false } && Picture!.All.Count() > 1;

    [RelayCommand(CanExecute = nameof(HasActive))]
    private void MoveUp() => Step(+1);

    [RelayCommand(CanExecute = nameof(HasActive))]
    private void MoveDown() => Step(-1);

    /// <summary>The active layer moved one place up or down among the layers of its group.</summary>
    private void Step(int by)
    {
        if (_document is not { } document || Active is not { } layer || document.Picture.Locate(layer.Id) is not { } at) return;
        int count = document.Picture.SiblingsOf(layer.Id).Length;
        int index = Math.Clamp(at.Index + by, 0, count - 1);
        if (index == at.Index) return;
        document.Commit(p => p.Moved(layer.Id, at.Parent?.Id, index));
    }

    /// <summary>A row dragged onto another: above it, below it, or into it where it is a group.</summary>
    public void Move(Guid dragged, Guid target, LayerDrop drop)
    {
        if (_document is not { } document || dragged == target) return;
        var picture = document.Picture;
        if (picture.Find(target) is not { } onto || picture.Locate(target) is not { } at) return;
        if (drop == LayerDrop.Into && onto is GroupLayer group)
        {
            document.Commit(p => p.Moved(dragged, group.Id, group.Children.Length));
            return;
        }
        // Counted as the target's group stands once the dragged layer is out of it.
        var siblings = picture.SiblingsOf(target).Where(l => l.Id != dragged).ToList();
        int index = siblings.FindIndex(l => l.Id == target) + (drop == LayerDrop.Above ? 1 : 0);
        document.Commit(p => p.Moved(dragged, at.Parent?.Id, index));
    }

    private void Insert(Layer layer)
    {
        if (_document is not { } document) return;
        document.Commit(p => p.Inserted(layer, p.ActiveId));
    }

    private bool HasDocumentNow() => _document is not null;

    /// <summary>"Layer 3": the kind's name and the next number no layer of the picture has in its name.</summary>
    private string NextName(string key)
    {
        var names = Picture?.All.Select(l => l.Name).ToHashSet() ?? [];
        int n = names.Count + 1;
        while (names.Contains(Str.Format(key, n))) n++;
        return Str.Format(key, n);
    }

    // ── Into pixels ─────────────────────────────────────────────────────────

    /// <summary>The active picture or markup layer turned to pixels, as it looks, for the brush; one step.</summary>
    [RelayCommand(CanExecute = nameof(CanRasterize))]
    private Task Rasterize() => IntoPixels(picture => picture.Active is { } layer ? [layer] : [], (picture, pixels) =>
    {
        var layer = picture.Active!;
        var rasterized = new PixelLayer
        {
            Id = layer.Id, Name = layer.Name, Visible = layer.Visible, Opacity = layer.Opacity, Blend = layer.Blend, ClipsToBelow = layer.ClipsToBelow,
            Mask = layer.Mask, Kept = layer.Kept, Pixels = pixels,
        };
        return picture.WithLayer(layer.Id, _ => rasterized);
    }, plain: true);

    private bool CanRasterize() => !IsBusy && Active is (PictureLayer or MarkupLayer) and { Locked: false };

    /// <summary>The active layer and the one beneath it in its group, as they look together, made one pixel layer in its place.</summary>
    [RelayCommand(CanExecute = nameof(CanMergeDown))]
    private Task MergeDown() => IntoPixels(picture => Pair(picture) is var (lower, upper) ? [lower, upper] : [], (picture, pixels) =>
    {
        var (lower, upper) = Pair(picture)!.Value;
        var merged = new PixelLayer { Id = lower.Id, Name = lower.Name, Pixels = pixels, Kept = lower.Kept };
        return picture.Without(upper.Id).WithLayer(lower.Id, _ => merged) with { ActiveId = lower.Id, EditsMask = false };
    }, plain: false);

    private bool CanMergeDown() => !IsBusy && Picture is { } picture && Pair(picture) is var (lower, upper) && !lower.Locked && !upper.Locked;

    /// <summary>The active layer and the one beneath it in the same group; null where it is the lowest.</summary>
    private static (Layer Lower, Layer Upper)? Pair(LayeredPicture picture)
    {
        if (picture.Active is not { } upper || picture.Locate(upper.Id) is not { Index: > 0 } at) return null;
        return (picture.SiblingsOf(upper.Id)[at.Index - 1], upper);
    }

    /// <summary>Every layer, as the picture looks, made one pixel layer — the background still shown beneath it.</summary>
    [RelayCommand(CanExecute = nameof(CanFlatten))]
    private Task Flatten() => IntoPixels(picture => [.. picture.Layers], (picture, pixels) =>
    {
        var flat = new PixelLayer { Name = Str.Get("Images.Layers.FlattenedName"), Pixels = pixels };
        return picture with { Layers = [flat], ActiveId = flat.Id, EditsMask = false };
    }, plain: false);

    private bool CanFlatten() => !IsBusy && Picture is { } picture && picture.All.Count() > 1;

    /// <summary>
    /// <paramref name="layers"/> drawn at full size together into pixels — off the UI thread, picture layers from their full
    /// photos — and the picture <paramref name="apply"/> makes with them committed as one step, unless it moved on meanwhile.
    /// <paramref name="plain"/> draws them as they are by themselves, unblended and at full opacity, their mask kept apart.
    /// </summary>
    private async Task IntoPixels(Func<LayeredPicture, IReadOnlyList<Layer>> layers, Func<LayeredPicture, TiledPixels, LayeredPicture> apply, bool plain)
    {
        if (_document is not { } document) return;
        var picture = document.Picture;
        var drawn = layers(picture);
        if (drawn.Count == 0) return;
        if (plain) drawn = [.. drawn.Select(l => l with { Visible = true, Opacity = 1, Blend = LayerBlend.Normal, ClipsToBelow = false, Mask = null })];
        IsBusy = true;
        try
        {
            using var renderer = await document.RendererForAsync(picture with { Layers = [.. drawn] });
            if (renderer is null)
            {
                _shell?.ShowNotification(Str.Get("Images.Layers.WaitForTools"));
                return;
            }
            var pixels = await Task.Run(renderer.Flattened);
            if (!ReferenceEquals(document.Picture, picture)) return;   // edited meanwhile: the result is of a picture gone
            document.Commit(p => apply(p, pixels));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _shell?.ShowError(Str.Format("Images.Layers.FailedFormat", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Masks ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAddMask))]
    private void RevealAllMask() => SetMask(p => TiledCoverage.Covered(p.Width, p.Height));

    [RelayCommand(CanExecute = nameof(CanAddMask))]
    private void HideAllMask() => SetMask(p => TiledCoverage.Clear(p.Width, p.Height));

    /// <summary>A mask showing the layer where the selection is.</summary>
    [RelayCommand(CanExecute = nameof(CanMaskFromSelection))]
    private void MaskFromSelection() => SetMask(p => p.Selection);

    /// <summary>A mask hiding the layer where the selection is.</summary>
    [RelayCommand(CanExecute = nameof(CanMaskFromSelection))]
    private void MaskHidingSelection() => SetMask(p => p.Selection.Inverted());

    /// <summary>A mask from the layer's own transparency: shown where it is painted.</summary>
    [RelayCommand(CanExecute = nameof(CanMaskFromTransparency))]
    private void MaskFromTransparency() => SetMask(p => ((PixelLayer)p.Active!).Pixels.Alpha(p.Width, p.Height));

    private bool CanAddMask() => Active is { Locked: false, Mask: null } and not UndrawableLayer;

    private bool CanMaskFromSelection() => CanAddMask() && Picture?.Selection.IsEmpty == false;

    private bool CanMaskFromTransparency() => CanAddMask() && Active is PixelLayer;

    private void SetMask(Func<LayeredPicture, TiledCoverage> coverage)
    {
        if (_document is not { } document || Active is not { } layer) return;
        document.Commit(p => p.WithLayer(layer.Id, l => l with { Mask = new LayerMask(coverage(p)) }) with { EditsMask = true });
    }

    /// <summary>A picture layer's cut-out turned into its mask: what its recipe keeps, carried onto the canvas; the cut-out goes.</summary>
    [RelayCommand(CanExecute = nameof(CanMaskFromCutOut))]
    private void MaskFromCutOut()
    {
        if (_document is not { } document || Active is not PictureLayer layer || document.RenderingOf(layer.Id) is not { CutOutPath: { } file, Proxy: { } proxy }) return;
        var source = proxy.SourceSize;
        var grey = Editing.CuttingOut.CutOutMasks.ReadGrey(file, (int)Math.Round(source.Width), (int)Math.Round(source.Height));
        var kept = TiledCoverage.FromBytes(grey, (int)Math.Round(source.Width), (int)Math.Round(source.Height));
        var picture = document.Picture;
        var onCanvas = CoverageMapping.Map(kept, LayeredSession.SourceToCanvas(layer, source), picture.Width, picture.Height);
        document.Commit(p => p.WithLayer(layer.Id, l => ((PictureLayer)l) with { Mask = new LayerMask(onCanvas), Recipe = ((PictureLayer)l).Recipe with { CutOut = null } }));
    }

    private bool CanMaskFromCutOut() => Active is PictureLayer { Locked: false, Mask: null, Recipe.CutOut: not null } layer
                                        && _document?.RenderingOf(layer.Id)?.CutOutPath is not null;

    [RelayCommand(CanExecute = nameof(HasEditableMask))]
    private void InvertMask() => ChangeMask(m => m with { Coverage = m.Coverage.Inverted() });

    /// <summary>The mask baked into a pixel layer's transparency, and gone.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyMask))]
    private void ApplyMask()
    {
        if (_document is not { } document || Active is not PixelLayer { Mask: { } mask } layer) return;
        document.Commit(p => p.WithLayer(layer.Id, _ => layer with { Pixels = layer.Pixels.KeptWhere(mask.Enabled ? mask.Coverage : TiledCoverage.Covered(p.Width, p.Height)), Mask = null })
                             with { EditsMask = false });
    }

    [RelayCommand(CanExecute = nameof(HasEditableMask))]
    private void DeleteMask()
    {
        if (_document is not { } document || Active is not { } layer) return;
        document.Commit(p => p.WithLayer(layer.Id, l => l with { Mask = null }) with { EditsMask = false });
    }

    private bool HasEditableMask() => Active is { Locked: false, Mask: not null };

    private bool CanApplyMask() => Active is PixelLayer { Locked: false, Mask: not null };

    private void ChangeMask(Func<LayerMask, LayerMask> change)
    {
        if (_document is not { } document || Active is not { Mask: not null } layer) return;
        document.Commit(p => p.WithLayer(layer.Id, l => l.Mask is { } m ? l with { Mask = change(m) } : l));
    }

    // ── The canvas ──────────────────────────────────────────────────────────

    /// <summary>Canvas size: the width typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasMemory), nameof(CanvasTooLarge))]
    [NotifyCanExecuteChangedFor(nameof(ResizeCanvasCommand))]
    private int _canvasWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasMemory), nameof(CanvasTooLarge))]
    [NotifyCanExecuteChangedFor(nameof(ResizeCanvasCommand))]
    private int _canvasHeight;

    /// <summary>Where the picture stays as the canvas grows or shrinks.</summary>
    [ObservableProperty] private CanvasAnchor _canvasAnchor = CanvasAnchor.Centre;

    /// <summary>What one layer of the canvas typed takes in memory.</summary>
    public string CanvasMemory => Str.Format("Images.Layers.CanvasMemoryFormat", SizeFormatter.FormatBytes(PixelBytes * Math.Max(0, CanvasWidth) * Math.Max(0, CanvasHeight)));

    /// <summary>The bytes a pixel of the picture's layers takes, as deep as it is held.</summary>
    private long PixelBytes => (Picture?.Colour ?? PictureColour.Rgb8).Tiles.PixelBytes;

    /// <summary>A layer of the canvas typed would take more than a quarter of the machine's memory.</summary>
    public bool CanvasTooLarge => PixelBytes * CanvasWidth * CanvasHeight > GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 4;

    /// <summary>The canvas size form starts from the canvas as it is.</summary>
    [RelayCommand(CanExecute = nameof(HasDocumentNow))]
    private void BeginCanvasSize()
    {
        if (Picture is not { } picture) return;
        (CanvasWidth, CanvasHeight, CanvasAnchor) = (picture.Width, picture.Height, CanvasAnchor.Centre);
        (ColourMode, ColourDepth) = (ColourModelChoice.Of(picture.Colour.Model), ChannelDepthChoice.Of(picture.Colour.Depth));
    }

    [RelayCommand(CanExecute = nameof(CanResizeCanvas))]
    private void ResizeCanvas()
    {
        if (_document is not { } document) return;
        var picture = document.Picture;
        int dx = (CanvasWidth - picture.Width) * ((int)CanvasAnchor % 3) / 2, dy = (CanvasHeight - picture.Height) * ((int)CanvasAnchor / 3) / 2;
        document.Commit(p => LayerEdits.Resized(p, CanvasWidth, CanvasHeight, dx, dy));
    }

    private bool CanResizeCanvas() => _document is not null && CanvasWidth is >= 1 and <= LayeredPicture.MaxSide && CanvasHeight is >= 1 and <= LayeredPicture.MaxSide
                                      && (CanvasWidth != Picture!.Width || CanvasHeight != Picture.Height);

    // ── Colour ───────────────────────────────────────────────────────────────

    /// <summary>Colour and grey, as the mode picker offers them.</summary>
    public IReadOnlyList<ColourModelChoice> ColourModes => ColourModelChoice.All;

    /// <summary>8, 16 and 32 bits, as the depth picker offers them.</summary>
    public IReadOnlyList<ChannelDepthChoice> ColourDepths => ChannelDepthChoice.All;

    /// <summary>The colour mode the picture is converted to.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertColourCommand))]
    private ColourModelChoice _colourMode = ColourModelChoice.Of(ColourModel.Rgb);

    /// <summary>The depth the picture is converted to.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertColourCommand))]
    private ChannelDepthChoice _colourDepth = ChannelDepthChoice.Of(ChannelDepth.U8);

    /// <summary>
    /// The picture held in the colour mode and depth chosen: every layer converted — colour made grey, grey spread to colour, light
    /// made linear for 32 bits or encoded again from them, what a shallower depth cannot hold clamped — one step.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConvertColour))]
    private void ConvertColour()
    {
        if (_document is not { } document) return;
        var colour = new PictureColour(ColourMode.Model, ColourDepth.Depth);
        document.Commit(p => p.ConvertedTo(colour));
        OnPropertyChanged(nameof(ShowsLight));
        OnPropertyChanged(nameof(CanvasMemory));
    }

    private bool CanConvertColour() => Picture is { } picture && picture.Colour != new PictureColour(ColourMode.Model, ColourDepth.Depth);

    /// <summary>The picture is 32-bit: it holds light, shown at <see cref="ViewExposure"/>.</summary>
    public bool ShowsLight => Picture?.Colour.IsLinear == true;

    /// <summary>Stops of exposure a 32-bit picture is shown at — a view of its light, never saved.</summary>
    [ObservableProperty] private double _viewExposure;

    partial void OnViewExposureChanged(double value)
    {
        if (_document is { } document) document.ViewExposure = value;
    }

    /// <summary>The canvas cropped to the selection's bounds.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CropToSelection()
    {
        if (_document is not { } document || LayerEdits.CoveredArea(document.Picture.Selection) is not { } area) return;
        document.Commit(p => LayerEdits.Resized(p, area.Width, area.Height, -area.Left, -area.Top));
    }

    private bool HasSelection() => Picture?.Selection.IsEmpty == false;

    // ── What a selection does to the active layer ───────────────────────────

    /// <summary>The selection, or — where nothing is selected — everything.</summary>
    private static TiledCoverage Selected(LayeredPicture picture)
        => picture.Selection.IsEmpty ? TiledCoverage.Covered(picture.Width, picture.Height) : picture.Selection;

    /// <summary>Delete: what is selected cleared from the active pixel layer — on one whose transparency is locked, filled with
    /// the background — or, editing its mask, hidden.</summary>
    [RelayCommand(CanExecute = nameof(CanPaint))]
    private void DeleteSelected()
    {
        if (_document is not { } document || Active is not { Locked: false } layer) return;
        var picture = document.Picture;
        var selected = Selected(picture);
        if (picture.EditsMask && layer.Mask is { } mask)
        {
            document.Commit(p => p.WithLayer(layer.Id, l => l with { Mask = mask with { Coverage = mask.Coverage.Combined(selected, Editing.Recipe.RegionMode.Subtract) } }));
            return;
        }
        if (layer is not PixelLayer pixels) return;
        var changed = layer.AlphaLocked ? pixels.Pixels.Filled(selected, picture.Colour.FromSrgb(picture.Background.Colour), keepAlpha: true) : pixels.Pixels.Cleared(selected);
        document.Commit(p => p.WithLayer(layer.Id, _ => pixels with { Pixels = changed }));
    }

    /// <summary>Fill (Shift+F5): what is selected filled with the colour chosen on the active pixel layer — or, editing its mask,
    /// with the colour's grey.</summary>
    [RelayCommand(CanExecute = nameof(CanPaint))]
    private void Fill()
    {
        if (_document is not { } document || Active is not { Locked: false } layer) return;
        var picture = document.Picture;
        var selected = Selected(picture);
        var colour = _colour(picture);
        if (picture.EditsMask && layer.Mask is { } mask)
        {
            // The colour's grey, laid over the mask as much as the selection covers.
            var laid = mask.Coverage.LaidOver(selected, picture.Colour.ShownGrey(colour));
            document.Commit(p => p.WithLayer(layer.Id, l => l with { Mask = mask with { Coverage = laid } }));
            return;
        }
        if (layer is not PixelLayer pixels) return;
        document.Commit(p => p.WithLayer(layer.Id, _ => pixels with { Pixels = pixels.Pixels.Filled(selected, colour, keepAlpha: layer.AlphaLocked) }));
    }

    private bool CanPaint() => Active is { Locked: false } layer && (layer is PixelLayer || Picture?.EditsMask == true && layer.Mask is not null);

    /// <summary>Copy: the active layer's pixels inside the selection.</summary>
    [RelayCommand(CanExecute = nameof(HasActive))]
    private Task Copy() => CopyAsync(merged: false);

    /// <summary>Copy merged: the picture's pixels, as it looks, inside the selection.</summary>
    [RelayCommand(CanExecute = nameof(HasDocumentNow))]
    private Task CopyMerged() => CopyAsync(merged: true);

    /// <summary>Cut: copied, then cleared from the active pixel layer.</summary>
    [RelayCommand(CanExecute = nameof(CanPaint))]
    private async Task Cut()
    {
        if (await CopyAsync(merged: false)) DeleteSelected();
    }

    private async Task<bool> CopyAsync(bool merged)
    {
        if (_document is not { } document) return false;
        var picture = document.Picture;
        var selected = Selected(picture);
        TiledPixels pixels;
        if (!merged && picture.Active is PixelLayer layer) pixels = layer.Pixels;
        else
        {
            var drawn = merged ? picture : picture.Active is { } active ? picture with { Layers = [active with { Visible = true, Mask = null, ClipsToBelow = false }] } : null;
            if (drawn is null) return false;
            using var renderer = await document.RendererForAsync(drawn);
            if (renderer is null)
            {
                _shell?.ShowNotification(Str.Get("Images.Layers.WaitForTools"));
                return false;
            }
            pixels = await Task.Run(renderer.Flattened);
        }
        var kept = pixels.KeptWhere(selected);
        if (kept.Bounds is not { IsEmpty: false } area) return false;
        var png = ClipboardPng(kept, area);
        PictureClipboard.CopyPng(png);
        _copied = (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)), area, kept);
        return true;
    }

    /// <summary>
    /// The pixels of <paramref name="area"/> as the PNG other applications paste: 8-bit as they are, 16-bit as a 16-bit PNG, and
    /// a 32-bit picture's light encoded as sRGB at 16 bits — what is past white clipped, since a PNG holds no more.
    /// </summary>
    private static byte[] ClipboardPng(TiledPixels pixels, SKRectI area)
    {
        using var bitmap = pixels.ToBitmap(area);
        var info = pixels.Format.Colour.Depth == ChannelDepth.F32
            ? new SKImageInfo(area.Width, area.Height, SKColorType.Rgba16161616, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb())
            : bitmap.Info;
        using var encoded = new SKBitmap(info);
        using (var held = SKImage.FromPixelCopy(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes))
            if (held is null || !held.ReadPixels(info, encoded.GetPixels(), encoded.RowBytes, 0, 0))
                throw new InvalidOperationException("The pixels could not be encoded.");
        using var image = SKImage.FromPixelCopy(encoded.Info, encoded.GetPixels(), encoded.RowBytes)
                          ?? throw new InvalidOperationException("The pixels could not be encoded.");
        using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("The pixels could not be encoded.");
        return data.ToArray();
    }

    /// <summary>
    /// Paste: the picture on the clipboard as a new pixel layer above the active one — where it was copied from, when it was
    /// copied here, else at the canvas's top-left.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDocumentNow))]
    private void Paste()
    {
        if (_document is not { } document || PictureClipboard.ReadPng() is not { } png) return;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png));
        var name = Str.Get("Images.Layers.PastedName");
        // The picture copied here last comes back as deep as it was held, where it lay.
        if (_copied is { } copied && copied.Hash == hash)
        {
            Insert(new PixelLayer { Name = name, Pixels = copied.Pixels });
            return;
        }
        using var bitmap = SKBitmap.Decode(png);
        if (bitmap is null) return;
        Insert(new PixelLayer { Name = name, Pixels = TiledPixels.FromBitmap(bitmap, 0, 0, document.Picture.Colour.Tiles) });
    }

    // ── Keeping step ────────────────────────────────────────────────────────

    /// <summary>Every command and active-layer property says again what it allows.</summary>
    private void Refresh()
    {
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(Blend));
        OnPropertyChanged(nameof(ActiveClips));
        OnPropertyChanged(nameof(ActiveAlphaLocked));
        OnPropertyChanged(nameof(ActiveLocked));
        OnPropertyChanged(nameof(ActiveIsGroup));
        OnPropertyChanged(nameof(ActiveIsolated));
        OnPropertyChanged(nameof(ActiveName));
        foreach (var command in new IRelayCommand[]
                 {
                     AddPixelLayerCommand, AddMarkupLayerCommand, GroupCommand, DuplicateCommand, DeleteCommand, MoveUpCommand, MoveDownCommand,
                     RasterizeCommand, MergeDownCommand, FlattenCommand, RevealAllMaskCommand, HideAllMaskCommand, MaskFromSelectionCommand,
                     MaskHidingSelectionCommand, MaskFromTransparencyCommand, MaskFromCutOutCommand, InvertMaskCommand, ApplyMaskCommand,
                     DeleteMaskCommand, BeginCanvasSizeCommand, ResizeCanvasCommand, ConvertColourCommand, CropToSelectionCommand, DeleteSelectedCommand, FillCommand,
                     CopyCommand, CopyMergedCommand, CutCommand, PasteCommand,
                 })
            command.NotifyCanExecuteChanged();
    }
}
