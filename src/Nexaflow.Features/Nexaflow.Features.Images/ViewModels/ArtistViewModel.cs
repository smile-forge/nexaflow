using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Images.ClientTools;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Painting;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.OpenRaster;
using Nexaflow.Visuals.Common.Drafts;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Artist: one picture edited — a photo with its edits, or a layered picture — with the editor's tools beside it; or, holding
/// none, the start screen that makes one. Whatever the start screen opens, the tab edits from then on.
/// </summary>
public sealed partial class ArtistViewModel : ObservableObject, IPageViewModel, IDisposable
{
    /// <summary>How long after the last change a layered picture is written, so a burst of edits is one write.</summary>
    private static readonly TimeSpan DocumentSaveDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>A layered picture this many pixels, layers counted, or more, saves among the workspace's jobs, with its progress.</summary>
    private const long LargeDocumentPixels = 64L * 1024 * 1024;

    private readonly IShellServices? _shell;
    private readonly ImageEditingServices _editing;
    private readonly ImagesConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _documentSave;

    /// <summary>What the assistant can do here.</summary>
    private readonly ArtistTools _tools;

    /// <summary>The photo edited — the merged image of a layered picture, which opens over it — and its history; null on the start screen.</summary>
    private ImageEditSession? _session;

    /// <param name="path">The picture to edit; none, the start screen.</param>
    /// <param name="ai">The workspace's AI, which draws a picture into a canvas where it has a model for it.</param>
    /// <param name="pictures">The picture tools, where the app has them: AI enhance and upscale.</param>
    /// <param name="selection">Picture selection, where the app has it: what Cut out and Erase pick things out with.</param>
    /// <param name="erasing">Picture erasing, where the app has it: Erase's AI fill.</param>
    /// <param name="editing">The editing engine the registration shares across its tabs; the page does not own it.</param>
    public ArtistViewModel(string? path, IShellServices? shell, ImageEditingServices editing, IAIService? ai = null,
                           IPictureTools? pictures = null, IPictureSelection? selection = null, IPictureErasing? erasing = null,
                           ImagesConfig? config = null)
    {
        _shell   = shell;
        _editing = editing;
        _config  = config ?? new ImagesConfig();

        Stage = new PictureStage(editing, shell, DisplayMode, _cts.Token, pictures, selection, erasing) { IsEditor = true };
        Stage.PropertyChanged += OnStagePropertyChanged;
        Stage.SessionShown    += OnSessionShown;
        Stage.Changed         += OnStageChanged;
        Stage.Decoded         += OnDecoded;
        Stage.LayerToolMade   += ScheduleDocumentSave;

        _documentSave = new DispatcherTimer(DispatcherPriority.Background) { Interval = DocumentSaveDelay };
        _documentSave.Tick += (_, _) => { _documentSave.Stop(); SaveDocument(); };

        var runOnUi = Stage.RunOnUi;
        Cutter = new PictureCutter(editing, selection, shell, runOnUi, Stage.Models);

        // What each model-backed control waits for; while a panel that needs any is open, the banner offers AI picture editing's
        // models as one — unless a picture tool's banner, for an edit the recipe asks for, is offering them already.
        var picking = new ModelsGate(selection, Stage.Models, runOnUi);
        var erasingModels = new ModelsGate(erasing, Stage.Models, runOnUi);
        var restoring = new ModelsGate(pictures, Stage.Models, runOnUi);
        picking.Defer(Stage.Remover, Stage.Eraser);
        erasingModels.Defer(Stage.Eraser);
        restoring.Defer(Stage.Restorer);

        Editor = new ImageEditorViewModel(this, shell, editing, Stage.Restorer, Stage.Remover, Cutter, Stage.Eraser, Stage.Models, picking, erasingModels, restoring);
        Layers = new LayersViewModel(shell, runOnUi, picture => Painting.InkIn(picture));
        Layers.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(LayersViewModel.MaskAlone) or nameof(LayersViewModel.ViewExposure)) RequestRender(); };
        Start = new ImageStartViewModel(this, shell, ai, editing, _config);
        Pictures = new CanvasPictures(this, shell, ai, editing);
        Edits    = new PictureEdits(this, shell, ai);
        Painting = new LayerPainting(this, Editor);
        Arranging = new LayerArranging(this, editing);
        Printing = new PicturePrinting(Stage, editing, shell);
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ImageEditorViewModel.Tool) || Editor.IsMoveTool) return;
            // Leaving Move: an open transform is applied, and what floats is dropped back into its layer.
            if (Arranging.IsTransforming) Arranging.Apply();
            else if (Arranging.HasFloating) Arranging.Drop();
        };

        _tools = new ArtistTools(this, shell, editing);

        if (path is { Length: > 0 }) ShowPath(path);
        else _ = Start.RefreshAsync();
    }

    /// <summary>The picture on show, as the screen draws it.</summary>
    public PictureStage Stage { get; }

    /// <summary>The picture edited; null on the start screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStarting), nameof(IsEditing), nameof(CurrentFileName))]
    private string? _currentPath;

    /// <summary>The picture's file name; empty on the start screen.</summary>
    public string CurrentFileName => CurrentPath is { } path ? Path.GetFileName(path) : string.Empty;

    /// <summary>The tab holds no picture: the start screen shows.</summary>
    public bool IsStarting => CurrentPath is null;

    /// <summary>A picture is open in the editor.</summary>
    public bool IsEditing => CurrentPath is not null;

    /// <summary>The editor's tools, over the picture.</summary>
    public ImageEditorViewModel Editor { get; }

    /// <summary>What picks things out of the picture for Cut out and Erase.</summary>
    public PictureCutter Cutter { get; }

    /// <summary>The layers panel.</summary>
    public LayersViewModel Layers { get; }

    /// <summary>The start screen, shown while the tab holds no picture.</summary>
    public ImageStartViewModel Start { get; }

    /// <summary>Pictures brought into a layered picture as layers: dropped on the canvas, or drawn by the image model.</summary>
    public CanvasPictures Pictures { get; }

    /// <summary>The AI edits of a layered picture: fill the selection, expand the canvas, change a layer or the picture.</summary>
    public PictureEdits Edits { get; }

    /// <summary>The Paint tool: brush, eraser, eyedropper and fill on the active pixel layer or its mask.</summary>
    public LayerPainting Painting { get; }

    /// <summary>The Move tool: layers and what is selected moved, and transformed.</summary>
    public LayerArranging Arranging { get; }

    /// <summary>Print: the picture, or the canvas, laid on paper.</summary>
    public PicturePrinting Printing { get; }

    /// <summary>What gives the picture the restoration its recipe asks for.</summary>
    public PhotoRestorer Restorer => Stage.Restorer;

    /// <summary>What gives the picture the removed background its recipe asks for.</summary>
    public BackgroundRemover Remover => Stage.Remover;

    /// <summary>The erasing the picture's recipe asks for.</summary>
    public PhotoEraser Eraser => Stage.Eraser;

    // What the stage shows, under the names the editor and its views read; the stage's changes are raised here as they come.

    public ImageEditSession? CurrentSession => Stage.CurrentSession;

    public LayeredSession? CurrentDocument => Stage.CurrentDocument;

    public BitmapSource? CurrentImage => Stage.CurrentImage;

    public EditCanvas? Canvas => Stage.Canvas;

    public bool IsLayered => Stage.IsLayered;

    public ImageRecipe CurrentRecipe => Stage.CurrentRecipe;

    public bool IsEdited => Stage.IsEdited;

    internal IRecipeEditing? EditTarget => Stage.EditTarget;

    internal IMarkupEditing? MarkupTarget => Stage.MarkupTarget;

    /// <summary>The picture opened on the look the application that wrote it saved: a way on is offered.</summary>
    public bool ShowsSavedLookBar => CurrentDocument?.SavedLook is not null;

    /// <summary>Which layers of the open picture no reader can draw, by name, for the saved look's bar.</summary>
    public string SavedLookNotice => CurrentDocument is { } document
        ? Str.Format("Images.Layers.SavedLookFormat", string.Join(", ", document.Picture.All
            .Where(l => l is UndrawableLayer || l.Kept.Undrawn.Any())
            .Select(l => l is UndrawableLayer || !l.Kept.Undrawn.Any() ? l.Name : $"{l.Name} ({string.Join(", ", l.Kept.Undrawn)})")))
        : "";

    private static readonly HashSet<string> RaisedStageProperties =
    [
        nameof(PictureStage.CurrentSession), nameof(PictureStage.CurrentDocument), nameof(PictureStage.CurrentImage),
        nameof(PictureStage.Canvas), nameof(PictureStage.IsLayered), nameof(PictureStage.CurrentRecipe), nameof(PictureStage.IsEdited),
    ];

    private void OnStagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not { } name || !RaisedStageProperties.Contains(name)) return;
        OnPropertyChanged(name);
        if (name != nameof(PictureStage.CurrentDocument)) return;
        OnPropertyChanged(nameof(ShowsSavedLookBar));
        OnPropertyChanged(nameof(SavedLookNotice));
    }

    // ── What is edited ────────────────────────────────────────────────────

    /// <summary>How the picture is shown: on the edit canvas, before or after, cropping or not, a layer's mask alone.</summary>
    private ImageDisplayMode DisplayMode() =>
        new(IsEditing, IsEditing && Editor.ShowOriginal, Editor.Frame, FitToWindow: true, Layers.MaskAlone);

    /// <summary>Renders the picture for the way it is shown now.</summary>
    internal void RequestRender() => Stage.Render();

    /// <summary>The canvas's room for the picture changed size (device-independent pixels) or density.</summary>
    public void SetViewport(double width, double height, double pixelsPerDip)
    {
        if (Stage.SetViewport(width, height, pixelsPerDip)) RequestRender();
    }

    /// <summary>The edit canvas was zoomed: render more (or fewer) pixels for the same layout.</summary>
    public void SetEditZoom(double zoom)
    {
        if (Stage.SetEditZoom(zoom) && IsEditing) RequestRender();
    }

    /// <summary>
    /// Edits <paramref name="path"/> in place of what the tab held: its photo shown, and — a layered picture — the layers opened
    /// over it. The photo before is let go, written first where it changed.
    /// </summary>
    private void ShowPath(string path)
    {
        bool starting = IsStarting;
        CloseDocument();
        var previous = _session;
        _session = new ImageEditSession(path, _editing);
        CurrentPath = path;
        Stage.Show(_session);
        previous?.Dispose();
        if (starting) Editor.OnEditModeChanged(true);
        _ = OpenLayeredAsync(path);
    }

    private void OnSessionShown()
    {
        Editor.OnSessionChanged();
        AddLayerCommand.NotifyCanExecuteChanged();
        PasteAsLayerCommand.NotifyCanExecuteChanged();
        NotifyHistory();
    }

    private void OnStageChanged()
    {
        NotifyHistory();
        if (CurrentDocument is { } document)
        {
            Layers.Follow(document);
            Editor.RegionMarks.DocumentChanged();
            Painting.Refresh();
            Arranging.Refresh();
            OnPropertyChanged(nameof(ShowsSavedLookBar));
            OnPropertyChanged(nameof(SavedLookNotice));
            if (document.History.CanUndo) ScheduleDocumentSave();
        }
        Edits.Refresh();
        Editor.OnRecipeChanged();
    }

    /// <summary>A picture shown has pixels — its first decode, a sharper one, or what a tool made: the editor catches up.</summary>
    private void OnDecoded(PictureRendering rendering)
    {
        if (CurrentDocument is null || ReferenceEquals(rendering, EditTarget?.Rendering)) Editor.OnSessionChanged();
        if (CurrentDocument is not null) Layers.RedrawThumbnails();
    }

    private void NotifyHistory()
    {
        UndoEditCommand.NotifyCanExecuteChanged();
        RedoEditCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Closes and reopens the picture — after a save has replaced the file under it.</summary>
    internal void ReloadCurrent()
    {
        if (CurrentPath is { } path) ShowPath(path);
    }

    // ── History ───────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanUndoEdit))]
    private void UndoEdit()
    {
        if (Arranging.IsTransforming)
        {
            Arranging.Cancel();   // an open transform is what undo takes back first
            return;
        }
        Stage.Undo();
    }

    private bool CanUndoEdit() => Arranging.IsTransforming || Stage.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedoEdit))]
    private void RedoEdit() => Stage.Redo();

    private bool CanRedoEdit() => !Arranging.IsTransforming && Stage.CanRedo;

    /// <summary>Applies <paramref name="change"/> to the recipe of the picture the tools work on, as one undoable step.</summary>
    internal void Edit(Func<ImageRecipe, ImageRecipe> change) => Stage.Edit(change);

    internal void BeginEditGesture() => Stage.BeginEditGesture();

    internal void EndEditGesture() => Stage.EndEditGesture();

    // ── Layered pictures ──────────────────────────────────────────────────

    private bool HasSession() => CurrentSession is not null;

    /// <summary>
    /// The photo made the first layer of a layered picture its size — a picture layer holding it and its recipe — kept in a draft
    /// until the first Save asks where; the photo itself is never written over. Gives the picture; null where no photo is ready.
    /// </summary>
    internal LayeredSession? MakeLayered()
    {
        if (CurrentDocument is { } open) return open;
        if (CurrentSession is not { Proxy: { } proxy } session) return null;
        var size = CropGeometry.OutputSize(session.Recipe, proxy.SourceSize);
        var name = Path.GetFileNameWithoutExtension(session.Path);
        var picture = LayeredPicture.OfPhoto(new PictureSource(session.Path, session.Recipe.Source), session.Recipe, size.Width, size.Height, name);
        var document = new LayeredSession(DraftLocation.NewFile(name + OpenRasterPackage.Extension), picture, _editing, _config.HistoryBudget);
        document.KeptAsDraft(session.IsFileBacked ? Path.GetDirectoryName(session.Path) : null);
        ShowDocument(document);
        ScheduleDocumentSave();
        return document;
    }

    /// <summary>
    /// The picture as a layered one, what is selected on it carried over: open already, as it is; a photo, made the first layer of
    /// one — what <paramref name="selected"/> picks and draws on it selected on the canvas, where it now stands. Null where no
    /// photo is ready.
    /// </summary>
    internal async Task<LayeredSession?> LayeredKeepingSelectionAsync(RecipeCutOut? selected, CancellationToken ct)
    {
        if (CurrentDocument is { } open) return open;
        if (CurrentSession is not { Proxy: { } proxy }) return null;
        var source = proxy.SourceSize;
        var mask = selected is null ? null : await Cutter.MaskAsync(selected, ct);
        if (MakeLayered() is not { } document) return null;
        if (mask is not null && document.Picture.Active is PictureLayer photo)
            document.Commit(p => p with { Selection = DocumentSelection.OnCanvas(mask, photo, source, p) });
        return document;
    }

    /// <summary>A new pixel layer above the active one — the photo made a layered picture first, where it is not one yet.</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private void AddLayer()
    {
        if (MakeLayered() is null) return;
        Layers.AddPixelLayerCommand.Execute(null);
    }

    /// <summary>The picture on the clipboard as a new layer — the photo made a layered picture first, where it is not one yet.</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private void PasteAsLayer()
    {
        if (!PictureClipboard.HoldsPicture() || MakeLayered() is null) return;
        Layers.PasteCommand.Execute(null);
    }

    /// <summary>
    /// The layered picture at <paramref name="path"/> opened for editing over its merged image — on its saved look where it holds a
    /// layer no reader can draw. Nothing where it is not one, or where the tab moved on while it was read.
    /// </summary>
    private async Task OpenLayeredAsync(string path)
    {
        if (CurrentDocument is { } open && string.Equals(open.Path, path, StringComparison.OrdinalIgnoreCase)) return;
        if (await _editing.LayeredFormatOfAsync(path) is not { } format) return;
        LayeredPictureRead read;
        try
        {
            read = await format.ReadAsync(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            _shell?.ShowError(Str.Format("Images.Layers.OpenFailedFormat", Path.GetFileName(path), ex.Message));
            return;
        }
        if (!string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase)) return;
        var document = new LayeredSession(path, read.Picture, _editing, _config.HistoryBudget);
        if (read.HoldsUndrawable) document.ShowSavedLook(format.SavedLookOf(path));
        ShowDocument(document);
        Start.Remember(path);
    }

    private void ShowDocument(LayeredSession document)
    {
        CloseDocument();
        Stage.ShowDocument(document);
        Layers.Follow(document);
        Editor.RegionMarks.Attach(new DocumentSelection(document));
        Editor.OnSessionChanged();
        OnStageChanged();
        Pictures.DocumentShown();
    }

    /// <summary>The layered picture let go — written first where it changed — and the photo shown again.</summary>
    private void CloseDocument()
    {
        if (CurrentDocument is not { } document) return;
        if (_documentSave.IsEnabled)
        {
            _documentSave.Stop();
            SaveDocument();
        }
        Layers.Follow(null);
        Editor.RegionMarks.Attach(null);
        Stage.ShowDocument(null);
        document.Dispose();
        Editor.OnSessionChanged();
    }

    /// <summary>The layered picture is written once its edits pause.</summary>
    private void ScheduleDocumentSave()
    {
        if (CurrentDocument is null) return;
        _documentSave.Stop();
        _documentSave.Start();
    }

    /// <summary>
    /// The layered picture written where it is kept — its package, or its draft — whole and at once; a large one among the
    /// workspace's jobs. A picture layer still waiting for the picture tools puts it off until they have made it.
    /// </summary>
    private void SaveDocument()
    {
        if (CurrentDocument is { } held && _editing.LayeredFormatFor(held.Path) is { } format && !format.HoldsColour(held.Picture.Colour))
        {
            // Converted to a colour its own file cannot hold: kept as a draft in a format that does, until it is saved as one.
            var name = Path.GetFileName(held.Path);
            var extension = _editing.LayeredExtensionFor(held.Picture.Colour);
            held.MovedToDraft(DraftLocation.NewFile(Path.GetFileNameWithoutExtension(held.Path) + extension));
            _shell?.ShowNotification(Str.Format("Images.Layers.RedraftedFormat", name, extension));
        }
        if (CurrentDocument is not { } document || document.Writing(document.Path) is not { } write) return;
        var picture = document.Picture;
        var task = new ImageSaveTask(document.Path, write);
        void Done()
        {
            if (task.Failure is { } failure)
                _shell?.ShowError(Str.Format("Images.Edit.Save.FailedFormat", Path.GetFileName(task.Destination), failure.Message));
        }
        if (_shell is not null && (long)picture.Width * picture.Height * Math.Max(1, picture.All.Count()) >= LargeDocumentPixels)
        {
            _shell.QueueBackgroundTask(task, onComplete: finished => _ = _shell.RunOnUiAsync(Done));
            return;
        }
        _ = Task.Run(() => task.RunAsync(_cts.Token)).ContinueWith(_ => Stage.RunOnUi(Done), TaskScheduler.Default);
    }

    /// <summary>Save, for a layered picture: written now where it is kept — or, a draft, saved somewhere first, as a package
    /// beside the photo it was made from to start with. Gives whether it was written.</summary>
    internal async Task<bool> SaveDocumentAsAsync(bool askWhere)
    {
        if (_shell is null || CurrentDocument is not { } document) return false;
        if (Draft is { } draft && string.Equals(draft.Path, document.Path, StringComparison.OrdinalIgnoreCase))
        {
            // The tab's own draft — a canvas made on its start screen — is kept by its Save as, and the tab becomes the file kept.
            bool kept = false;
            void OnKept(string _) => kept = true;
            draft.Kept += OnKept;
            try { await draft.SaveAsCommand.ExecuteAsync(null); }
            finally { draft.Kept -= OnKept; }
            return kept;
        }
        var destination = document.Path;
        if (askWhere || document.IsDraft)
        {
            // The layered formats that hold the picture's colour, the one it is kept in first where it holds it.
            var offered = _editing.LayeredFormatsHolding(document.Picture.Colour).Select(f => f.Extension).ToArray();
            var current = Path.GetExtension(document.Path);
            var extension = offered.FirstOrDefault(e => string.Equals(e, current, StringComparison.OrdinalIgnoreCase)) ?? offered[0];
            var name = Path.GetFileNameWithoutExtension(document.Path) + extension;
            destination = await _shell.PickSaveFileAsync(name, [extension, .. offered.Where(e => e != extension)], document.DraftFolder ?? Path.GetDirectoryName(document.Path));
            if (destination is null) return false;
            if (!offered.Contains(Path.GetExtension(destination), StringComparer.OrdinalIgnoreCase)) destination = Path.ChangeExtension(destination, extension);
        }
        if (document.Writing(destination) is not { } write)
        {
            _shell.ShowNotification(Str.Get("Images.Layers.WaitForTools"));
            return false;
        }
        _documentSave.Stop();
        var task = new ImageSaveTask(destination, write);
        await task.RunAsync(_cts.Token);
        if (task.Failure is { } failure)
        {
            _shell.ShowError(Str.Format("Images.Edit.Save.FailedFormat", Path.GetFileName(destination), failure.Message));
            return false;
        }
        document.MovedTo(destination);
        _shell.ShowNotification(Str.Format("Images.Edit.Save.CopyDoneFormat", Path.GetFileName(destination)));
        return true;
    }

    /// <summary>Export: the layered picture flattened into a file of a common format — the bottom picture layer's photo's
    /// metadata carried — the package left as it is.</summary>
    internal async Task ExportDocumentAsync()
    {
        if (_shell is null || CurrentDocument is not { } document) return;
        var suggested = Path.GetFileNameWithoutExtension(document.Path) + ImageContainer.Png.Extension();
        var extensions = ImageExporter.CopyFormats.Select(c => c.Extension()).Prepend(ImageContainer.Png.Extension()).Distinct().ToList();
        var destination = await _shell.PickSaveFileAsync(suggested, extensions, document.DraftFolder ?? Path.GetDirectoryName(document.Path));
        if (destination is null) return;
        var target = ImageContainers.FromExtension(destination);
        if (!_editing.Encoders.CanEncode(target)) destination = Path.ChangeExtension(destination, (target = ImageContainer.Png).Extension());
        var export = (document.Picture.All.OfType<PictureLayer>().FirstOrDefault()?.Recipe.Export) ?? RecipeExport.Default;
        if (document.Exporting(destination, target, export) is not { } write)
        {
            _shell.ShowNotification(Str.Get("Images.Layers.WaitForTools"));
            return;
        }
        var task = new ImageSaveTask(destination, write);
        _shell.QueueBackgroundTask(task, onComplete: finished => _ = _shell.RunOnUiAsync(() =>
        {
            if (task.Failure is { } failure) _shell.ShowError(Str.Format("Images.Edit.Save.FailedFormat", Path.GetFileName(destination), failure.Message));
            else _shell.ShowNotification(Str.Format("Images.Edit.Save.CopyDoneFormat", Path.GetFileName(destination)));
        }));
    }

    /// <summary>Use the saved look: the picture's layers replaced by one picture layer showing its merged image, kept as a file of its own.</summary>
    [RelayCommand]
    private async Task UseSavedLook()
    {
        if (CurrentDocument is not { SavedLook: { } look } document) return;
        // The file the saved look is decoded from, copied as it is: a merged PNG, or the file that holds its own composite.
        var copy = DraftLocation.NewFile(Path.GetFileNameWithoutExtension(document.Path) + " " + Str.Get("Images.Layers.SavedLookFileName") + Path.GetExtension(look.Path));
        await _editing.FileSystem.WriteAllBytesAsync(copy, await _editing.FileSystem.ReadAllBytesAsync(look.Path));
        document.UseSavedLook(copy, Str.Get("Images.Layers.SavedLookName"));
    }

    /// <summary>Edit the layers: what Nexaflow draws shown from now on, the layers it cannot draw left out and kept.</summary>
    [RelayCommand]
    private void EditTheLayers() => CurrentDocument?.EditTheLayers();

    /// <summary>A layered picture's selection, as the editor's hand-drawn selection works on it: each change a step of its history.</summary>
    private sealed class DocumentSelection(LayeredSession document) : IDocumentSelection
    {
        public SKSizeI Size => new(document.Picture.Width, document.Picture.Height);

        public TiledCoverage Selection => document.Picture.Selection;

        public void Select(TiledCoverage selection) => document.Commit(p => p with { Selection = selection });

        public TiledCoverage? FromPicture(PictureMask picked)
        {
            var picture = document.Picture;
            if (picture.Active is not PictureLayer layer || document.RenderingOf(layer.Id)?.Proxy is not { } proxy) return null;
            return OnCanvas(picked, layer, proxy.SourceSize, picture);
        }

        public async Task<TiledCoverage?> ReachOfColourAsync(SKPointI at, int tolerance, bool joined, bool everyLayer)
        {
            var picture = document.Picture;
            var active = picture.Active;
            if (!everyLayer && active is not null)
            {
                // The mask being painted, in its greys; a pixel layer, its own pixels read tile by tile as the reach comes to them.
                if (picture.EditsMask && active.Mask is { } mask) return await Task.Run(() => FloodFill.Reach(mask.Coverage, at.X, at.Y, tolerance, joined));
                if (active is PixelLayer own) return await Task.Run(() => FloodFill.Reach(own.Pixels, picture.Width, picture.Height, at.X, at.Y, tolerance, joined));
            }
            // What shows, or a picture or markup layer drawn alone, flattened at full size.
            var read = everyLayer || active is null ? picture : picture with
            {
                Layers = [active with { Visible = true, Opacity = 1, Blend = LayerBlend.Normal, ClipsToBelow = false, Mask = null }],
                Background = LayerBackground.Transparent,
            };
            using var renderer = await document.RendererForAsync(read);
            if (renderer is null) return null;   // a picture layer still waiting for what the picture tools make for it
            return await Task.Run(() => FloodFill.Reach(renderer.Flattened(), picture.Width, picture.Height, at.X, at.Y, tolerance, joined));
        }

        /// <summary><paramref name="picked"/> — a mask over a picture layer's photo, at any size — where it lies on the canvas.</summary>
        internal static TiledCoverage OnCanvas(PictureMask picked, PictureLayer layer, SKSize source, LayeredPicture picture)
        {
            var shownToSource = SKMatrix.CreateScale((float)(source.Width / picked.Width), (float)(source.Height / picked.Height));
            var shown = TiledCoverage.FromBytes(picked.Pixels, picked.Width, picked.Height);
            return CoverageMapping.Map(shown, shownToSource.PostConcat(LayeredSession.SourceToCanvas(layer, source)), picture.Width, picture.Height);
        }
    }

    // ── Drafts, and what the start screen opens ───────────────────────────

    /// <summary>The Save as of a picture made for the user and not saved yet — the tab's draft — or null for a picture opened where it lives.</summary>
    [ObservableProperty] private DraftSaving? _draft;

    /// <summary>Makes the Save as of a draft the tab comes to edit — its path, and the folder Save as starts in — set by the page,
    /// which becomes the file the draft is kept as.</summary>
    public Func<string, string?, DraftSaving>? MakeDraft { get; init; }

    /// <summary>Raised once the tab edits another picture than it opened on — from its start screen — for its page to follow.</summary>
    public event Action<ArtistPicture>? PictureReplaced;

    /// <summary>
    /// The tab edits <paramref name="path"/> in place of what it held — from its start screen: a canvas made there, one opened
    /// again, a picture opened — <paramref name="draft"/>, as a draft whose Save as starts in the user's Pictures.
    /// </summary>
    internal void Open(string path, bool draft = false)
    {
        var folder = draft ? ImageStartViewModel.DraftFolder : null;
        Draft = draft ? MakeDraft?.Invoke(path, folder) : null;
        ShowPath(path);
        PictureReplaced?.Invoke(new ArtistPicture(path, Draft is not null, folder));
    }

    /// <summary>
    /// Keeps the tab's draft at <paramref name="to"/>: a layered picture written there as it now stands, a picture copied with its
    /// edits. What the draft's Save as does.
    /// </summary>
    /// <exception cref="IOException">A picture layer waits for the picture tools, or the file could not be written.</exception>
    internal async Task KeepDraftAsync(string from, string to)
    {
        if (CurrentDocument is { } document && string.Equals(document.Path, from, StringComparison.OrdinalIgnoreCase))
        {
            if (document.Writing(to) is not { } write) throw new IOException(Str.Get("Images.Layers.WaitForTools"));
            _documentSave.Stop();
            await write(_cts.Token);
            return;
        }
        await ImageDraftHandler.KeepAsync(_editing, from, to);
    }

    // ── Activation (driven by the view's Loaded/Unloaded) ─────────────────

    public void OnActivated()
    {
        if (IsStarting) _ = Start.RefreshAsync();   // the clipboard, the canvases still there, the models: changed while away
    }

    public void OnDeactivated() => _editing.Recipes.Flush();

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        if (IsStarting) return "Artist: no picture open yet — the start screen, where a blank canvas is set up, a picture drawn into one, or a picture opened.";
        var kind = CurrentDocument is { } document ? $"a layered picture of {document.Picture.All.Count()} layer(s)" : "a photo";
        return $"Artist, editing '{CurrentFileName}' — {kind}{(IsEdited ? ", with edits" : "")}, the {ArtistTools.ToolName(Editor.Tool)} tool out. "
             + "Call describe_picture for its whole state, and capture_image to see it.";
    }

    public string? GetSecurityContext() => CurrentPath;

    /// <summary>The picture this tab edits — never its folder.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFiles(CurrentPath is { } path ? [path] : []);

    public string? GetAiSystemPromptGuidance() =>
        "A picture is open in Artist, the image editor. describe_picture gives its whole state — edits, layers with their ids, the selection, "
      + "the history — and capture_image shows it. edit_picture changes its edits, select_in_picture sets the selection, edit_layers takes a "
      + "list of layer operations; each is one step the user can undo, and answers with the state it left. Saving is the user's.";

    /// <summary>What the assistant can do here: <see cref="ArtistTools"/>.</summary>
    public IReadOnlyList<IClientTool> GetClientTools() => _tools.All();

    /// <summary>The tab editing the picture, set by its view: what opens beside it opens in its window.</summary>
    public IPageView? Host { get; set; }

    public void Dispose()
    {
        Cutter.Close();
        CloseDocument();
        _documentSave.Stop();
        _cts.Cancel();
        Stage.Show(null);
        _session?.Dispose();
        _session = null;
        Stage.Dispose();
        Start.Dispose();
        Pictures.Dispose();
        _cts.Dispose();
    }
}
