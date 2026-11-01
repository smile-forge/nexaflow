using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Layers;
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
    /// <summary>The longest side the assistant is shown, in pixels.</summary>
    private const int CaptureEdge = 2048;

    /// <summary>How long after the last change a layered picture is written, so a burst of edits is one write.</summary>
    private static readonly TimeSpan DocumentSaveDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>A layered picture this many pixels, layers counted, or more, saves among the workspace's jobs, with its progress.</summary>
    private const long LargeDocumentPixels = 64L * 1024 * 1024;

    private readonly IShellServices? _shell;
    private readonly ImageEditingServices _editing;
    private readonly ImagesConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _documentSave;

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
        Cutter = new PictureCutter(editing, selection, shell, runOnUi);

        // What each model-backed control waits for, and the download offered while a panel that needs it is open — unless a
        // picture tool's banner, for an edit the recipe asks for, is offering the same.
        var picking = new ModelsGate(selection, "Images.Models.Picking.", runOnUi);
        var erasingModels = new ModelsGate(erasing, "Images.Models.Erasing.", runOnUi);
        var restoring = new ModelsGate(pictures, "Images.Models.Restoring.", runOnUi);
        picking.Defer(Stage.Remover, Stage.Eraser);
        erasingModels.Defer(Stage.Eraser);
        restoring.Defer(Stage.Restorer);

        Editor = new ImageEditorViewModel(this, shell, editing, Stage.Restorer, Stage.Remover, Cutter, Stage.Eraser, picking, erasingModels, restoring);
        Layers = new LayersViewModel(shell, runOnUi, () => Editor.InkSkColour);
        Layers.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LayersViewModel.MaskAlone)) RequestRender(); };
        Start = new ImageStartViewModel(this, shell, ai, editing, _config);
        Pictures = new CanvasPictures(this, shell, ai, editing);
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
        ? Str.Format("Images.Layers.SavedLookFormat", string.Join(", ", document.Picture.All.OfType<UndrawableLayer>().Select(l => l.Name)))
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
        Editor.OnRecipeChanged();
    }

    /// <summary>A picture shown has pixels — its first decode, a sharper one, or what a tool made: the editor catches up.</summary>
    private void OnDecoded(PictureRendering rendering)
    {
        if (CurrentDocument is null || ReferenceEquals(rendering, EditTarget?.Rendering)) Editor.OnSessionChanged();
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
        if (read.HoldsUndrawable) document.ShowSavedLook(OpenRasterPackage.EntryPath(path, OpenRasterPackage.MergedImagePath));
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
            var name = Path.GetFileNameWithoutExtension(document.Path) + OpenRasterPackage.Extension;
            destination = await _shell.PickSaveFileAsync(name, [OpenRasterPackage.Extension], document.DraftFolder ?? Path.GetDirectoryName(document.Path));
            if (destination is null) return false;
            destination = Path.ChangeExtension(destination, OpenRasterPackage.Extension);
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
        var copy = DraftLocation.NewFile(Path.GetFileNameWithoutExtension(document.Path) + " " + Str.Get("Images.Layers.SavedLookFileName") + ".png");
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
            var source = proxy.SourceSize;
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
        var layered = CurrentDocument is { } document
            ? $" It is a layered picture of {document.Picture.All.Count()} layer(s); the active one is '{document.Picture.Active?.Name}'."
            : string.Empty;
        var edited = IsEdited ? " It has edits, shown as they would be saved." : string.Empty;
        return $"Artist, editing '{CurrentFileName}' — {DescribeCurrentImage()}, with the {Editor.Tool} tool out.{layered}{edited} "
             + "Call capture_image to see the picture itself, or get_image_info for its metadata.";
    }

    public string? GetSecurityContext() => CurrentPath;

    /// <summary>The picture this tab edits — never its folder.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFiles(CurrentPath is { } path ? [path] : []);

    public string? GetAiSystemPromptGuidance() =>
        "A picture is open in Artist, the image editor. To see it, call capture_image — it returns the picture as edited to you as a "
      + "vision attachment. Its tools pick things out of it, cut them out, restore it and erase from it; each edit is a step the user "
      + "can undo.";

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "capture_image",
            "Return the picture the user is editing, as edited, so you can see it — the way to answer any question about what it shows.",
            [],
            ToolSafety.SafeOperation,
            async (_, _) =>
            {
                var png = await EncodeCurrentPngAsync();
                if (png is not { Length: > 0 })
                    return ToolResult.Error("Couldn't capture the picture.", "No picture is open, or it is not drawn yet.");
                return ToolResult.Ok($"Captured '{CurrentFileName}'.", $"The picture \"{CurrentFileName}\" is attached for you to look at.")
                       with { Images = [new ContextImage(png, "image/png", CurrentFileName)] };
            },
            exemptFromRepeatGuard: true) { Needs = [AiAbility.ImageRecognition] },

        new DelegateClientTool(
            "get_image_info",
            "Get the picture's filename, format, pixel dimensions and file size.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(IsStarting
                ? ToolResult.Error("No picture is open.")
                : ToolResult.Ok($"{CurrentFileName}: {DescribeCurrentImage()}.", DescribeImageInfo())),
            parallelizable: true),

        new DelegateClientTool(
            "select_in_picture",
            "Pick out everything 'what' names in the picture — \"the mug\", \"woman with red hair\" — and show it selected in the "
          + "editor's Cut out tool, where the user can copy it or save it without its background. Says how many it found, how surely "
          + "and where, in the picture's pixels.",
            [new ClientToolParameter("what", "What to pick out, in a few words: a noun phrase like \"dog\" or \"red car\".")],
            ToolSafety.SafeOperation,
            async (args, _) => await SelectNamedAsync(ToolArgs.Str(args, "what"))),

        new DelegateClientTool(
            "cut_out",
            "Cut something out of the picture — everything 'what' names, or the thing inside 'box' — into a new PNG, transparent "
          + "around it and cropped to it, opened for the user in a new Artist tab to save where they choose; the picture itself is "
          + "untouched.",
            [
                new ClientToolParameter("what", "What to cut out, in a few words: a noun phrase like \"dog\" or \"red car\". Not needed with 'box'.", Required: false),
                new ClientToolParameter("box", "A box round the one thing to cut out, in the picture's pixels: left,top,right,bottom.", Required: false),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await CutOutToolAsync(ToolArgs.Str(args, "what"), ToolArgs.Str(args, "box"), ct)),

        new DelegateClientTool(
            "restore_picture",
            "Restore the picture with the picture tools on this machine — softness, noise and JPEG blocks cleaned up, the faces in it "
          + "repaired — kept at its size or made twice or four times as large, as one of its edits (AI enhance and AI upscale): shown "
          + "at once, undone with Undo, saved when the user saves.",
            [
                new ClientToolParameter("scale", "1 keeps its size, 2 or 4 makes it that many times as large. 1 when not given.", Required: false, Choices: ["1", "2", "4"]),
                new ClientToolParameter("faces", "Whether the faces in it are repaired. Yes when not given.", Required: false, Choices: ["yes", "no"]),
                new ClientToolParameter("strength", "How much of the restoration shows over the picture as it was, 0 to 100. 100 when not given.", Required: false),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await RestoreToolAsync(ToolArgs.Str(args, "scale"), ToolArgs.Str(args, "faces"), ToolArgs.Str(args, "strength"), ct)),

        new DelegateClientTool(
            "erase_in_picture",
            "Erase from the picture everything 'what' names — \"the sign\", \"people in the background\" — or the thing inside 'box', "
          + "and fill where it was from what is round it, as a step of its edits: shown at once, undone with Undo, saved when the user "
          + "saves. Says what it erased and where.",
            [
                new ClientToolParameter("what", "What to erase, in a few words: a noun phrase like \"sign\" or \"man in a red coat\". Not needed with 'box'.", Required: false),
                new ClientToolParameter("box", "A box round the one thing to erase, in the picture's pixels: left,top,right,bottom.", Required: false),
                new ClientToolParameter("fill", "How to fill where it was: 'ai' (LaMa, best where an edge or shape runs through it) or "
                                              + "'content-aware' (the picture's own texture). AI where it is downloaded, else content-aware.",
                                        Required: false, Choices: ["ai", "content-aware"]),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await EraseNamedAsync(ToolArgs.Str(args, "what"), ToolArgs.Str(args, "box"), ToolArgs.Str(args, "fill"), ct)),
    ];

    /// <summary>The tab editing the picture, set by its view: what opens beside it opens in its window.</summary>
    public IPageView? Host { get; set; }

    /// <summary>Runs <paramref name="action"/> on the UI thread, as the tools — which run off it — must to touch the editor.</summary>
    private Task OnUi(Action action) => _shell is null ? RunHere(action) : _shell.RunOnUiAsync(action);

    private static Task RunHere(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    /// <summary>The assistant's select_in_picture: the Cut out tool out, and everything <paramref name="what"/> names picked out there.</summary>
    private async Task<ToolResult> SelectNamedAsync(string? what)
    {
        if (string.IsNullOrWhiteSpace(what)) return ToolResult.Error("nothing named", "Say what to pick out as 'what'.");
        if (!Cutter.Available) return ToolResult.Error("picture selection unavailable", "This machine cannot run picture selection.");
        if (!Cutter.CanPick)
            return ToolResult.Error("picture selection not downloaded",
                "Picture selection is not on this machine yet: the user can download it in Settings > Local > Tools, or from the editor's Cut out.");

        Task<IReadOnlyList<PictureFind>>? finding = null;
        await OnUi(() =>
        {
            if (!IsEditing) return;
            Editor.Tool = EditTool.CutOut;
            finding = Cutter.FindNamedAsync(what.Trim());
        });
        if (finding is null) return ToolResult.Error("no picture", "No picture is open to pick from.");

        var finds = await finding;
        if (Cutter.Failed) return ToolResult.Error("could not pick it out", Cutter.Notice);
        if (finds.Count == 0) return ToolResult.Ok($"found no {what}", $"Nothing in '{CurrentFileName}' is \"{what}\".");
        var where = string.Join("; ", finds.Select(f => string.Create(CultureInfo.InvariantCulture,
            $"{f.Score:P0} sure at {f.Box.Left:F0},{f.Box.Top:F0},{f.Box.Right:F0},{f.Box.Bottom:F0}")));
        return ToolResult.Ok($"picked out {finds.Count} × {what}",
            $"Picked out {finds.Count} \"{what}\" in '{CurrentFileName}' ({where}); they show selected in the editor's Cut out tool, where the user can copy or save them.");
    }

    /// <summary>restore_picture: the picture restored as AI enhance and AI upscale restore it — the recipe's restore section set,
    /// the tool showing it out — answered once the restoration is made.</summary>
    private async Task<ToolResult> RestoreToolAsync(string? scale, string? faces, string? strength, CancellationToken ct)
    {
        int times = scale switch { null or "" or "1" => 1, "2" => 2, "4" => 4, _ => 0 };
        if (times == 0) return ToolResult.Error("no such scale", "The scale is 1, 2 or 4.");
        bool repair = !string.Equals(faces, "no", StringComparison.OrdinalIgnoreCase);
        double shown = 1;
        if (strength is { Length: > 0 })
        {
            if (!double.TryParse(strength, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || percent is < 0 or > 100)
                return ToolResult.Error("no such strength", "The strength is from 0 to 100.");
            shown = percent / 100;
        }
        if (!Restorer.Available) return ToolResult.Error("picture tools unavailable", "This machine cannot run the picture tools.");
        if (!Restorer.RestoresNow)
            return ToolResult.Error("picture tools not downloaded",
                "The picture tools are not on this machine yet: the user can download them in Settings > Local > Tools, or from the editor's AI enhance.");

        var wanted = new RecipeRestore { Scale = times, Faces = repair, Strength = shown };
        string? refused = null;
        Task<string?>? made = null;
        bool already = false;
        await OnUi(() =>
        {
            if (CurrentSession is not { Proxy: { } proxy } session)
            {
                refused = "No picture is open to restore.";
                return;
            }
            if (!Restorer.Restores((int)Math.Round(proxy.SourceSize.Width), (int)Math.Round(proxy.SourceSize.Height), times))
            {
                refused = $"'{CurrentFileName}' is too large to restore at {times}x; try a smaller scale.";
                return;
            }
            Editor.Tool = times > 1 ? EditTool.Resize : EditTool.Adjust;
            if (session.Recipe.Restore == wanted && session.RestoredPath is not null)
            {
                already = true;
                return;
            }
            made = Restorer.NextMadeAsync(ct);
            session.History.Commit(session.Recipe with { Restore = wanted });
        });
        if (refused is not null) return ToolResult.Error("could not restore it", refused);
        if (!already && await made! is { } failure) return ToolResult.Error("could not restore it", failure);
        return ToolResult.Ok($"restored {CurrentFileName}",
            $"Restored '{CurrentFileName}'{(times > 1 ? $" at {times}x its size" : "")}{(repair ? ", faces repaired" : "")}, shown at {shown:P0} strength, "
          + "as one of its edits; the user can undo it or save it.");
    }

    /// <summary>
    /// erase_in_picture: what <paramref name="what"/> names — found first, as Cut out finds it, so nothing is erased when nothing is
    /// — or what <paramref name="box"/> holds, erased as a step of the recipe with the Retouch tool out, filled by picture erasing
    /// where it is here or asked, else content-aware fill; answered once it is made.
    /// </summary>
    private async Task<ToolResult> EraseNamedAsync(string? what, string? box, string? fill, CancellationToken ct)
    {
        what = what?.Trim();
        if (!TryBox(box, out var boxed)) return ToolResult.Error("no such box", "Give 'box' as left,top,right,bottom in the picture's pixels.");
        if (string.IsNullOrEmpty(what) && boxed is null) return ToolResult.Error("nothing named", "Say what to erase as 'what', or give a 'box' round it.");
        if (!Cutter.Available) return ToolResult.Error("picture selection unavailable", "This machine cannot run picture selection, which finds what to erase.");
        if (!Cutter.CanPick)
            return ToolResult.Error("picture selection not downloaded",
                "Picture selection, which finds what to erase, is not on this machine yet: the user can download it in Settings > Local > Tools, or from the editor's Erase.");
        var by = fill switch
        {
            null or "" => Eraser.ErasesByAiNow ? EraseFill.Ai : EraseFill.ContentAware,
            _ when fill.Equals("ai", StringComparison.OrdinalIgnoreCase) => EraseFill.Ai,
            _ when fill.Equals("content-aware", StringComparison.OrdinalIgnoreCase) => EraseFill.ContentAware,
            _ => (EraseFill?)null,
        };
        if (by is not { } chosen) return ToolResult.Error("no such fill", "Fill with 'ai' or 'content-aware'.");
        if (chosen == EraseFill.Ai && !Eraser.ErasesByAiNow)
            return ToolResult.Error("picture erasing not downloaded",
                "Picture erasing (LaMa) is not on this machine yet: the user can download it in Settings > Local > Tools, or erase with fill 'content-aware'.");

        Task<IReadOnlyList<PictureFind>>? finding = null;
        bool open = false;
        await OnUi(() =>
        {
            if (!IsEditing) return;
            open = true;
            Editor.Tool = EditTool.Retouch;
            if (!string.IsNullOrEmpty(what)) finding = Cutter.FindNamedAsync(what);
        });
        if (!open) return ToolResult.Error("no picture", "No picture is open to erase from.");

        string said;
        if (finding is not null)
        {
            var finds = await finding;
            if (Cutter.Failed) return ToolResult.Error("could not find it", Cutter.Notice);
            if (finds.Count == 0) return ToolResult.Ok($"found no {what}", $"Nothing in '{CurrentFileName}' is \"{what}\"; nothing was erased.");
            said = $"{finds.Count} \"{what}\" at " + string.Join("; ", finds.Select(f => string.Create(CultureInfo.InvariantCulture,
                $"{f.Box.Left:F0},{f.Box.Top:F0},{f.Box.Right:F0},{f.Box.Bottom:F0}")));
        }
        else said = string.Create(CultureInfo.InvariantCulture, $"the thing in {boxed!.Left:F0},{boxed.Top:F0},{boxed.Right:F0},{boxed.Bottom:F0}");

        var step = new RecipeErase
        {
            Fill = chosen,
            Pick = !string.IsNullOrEmpty(what) ? new RecipeCutOut { Phrase = what } : new RecipeCutOut { Box = new RecipeCutBox(boxed!.Left, boxed.Top, boxed.Right, boxed.Bottom) },
        };
        var made = Eraser.NextMadeAsync(ct);
        await OnUi(() =>
        {
            if (CurrentSession is not { } session) return;
            session.History.Commit(session.Recipe with { Erasures = session.Recipe.Erasures.Add(step) });
            Cutter.ClearCommand.Execute(null);
        });

        if (await made is { } failure) return ToolResult.Error("could not erase it", failure);
        return ToolResult.Ok($"erased {(string.IsNullOrEmpty(what) ? "the boxed thing" : what)}",
            $"Erased {said} from '{CurrentFileName}', filled {(chosen == EraseFill.Ai ? "by AI" : "content-aware")}, as a step of its edits; the user can undo it or save it.");
    }

    /// <summary>A tool's 'box' — left,top,right,bottom in the picture's pixels — read; none given is a box of null, and false
    /// is one that does not read.</summary>
    private static bool TryBox(string? text, out PictureBox? box)
    {
        box = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        var v = new double[4];
        for (int i = 0; i < parts.Length && i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
        if (parts.Length != 4 || v[2] <= v[0] || v[3] <= v[1]) return false;
        box = new PictureBox(v[0], v[1], v[2], v[3]);
        return true;
    }

    /// <summary>
    /// cut_out: what <paramref name="what"/> names, or what <paramref name="box"/> holds, picked as Cut out picks it and made a PNG
    /// — transparent around it, cropped to it — opened as a draft in an Artist tab of its own, which the user saves where they
    /// choose. The assistant is told only that the tab opened: the new picture is its to work on once the user gives it that tab.
    /// </summary>
    private async Task<ToolResult> CutOutToolAsync(string? what, string? box, CancellationToken ct)
    {
        what = what?.Trim();
        if (!TryBox(box, out var boxed)) return ToolResult.Error("no such box", "Give 'box' as left,top,right,bottom in the picture's pixels.");
        if (string.IsNullOrEmpty(what) && boxed is null) return ToolResult.Error("nothing named", "Say what to cut out as 'what', or give a 'box' round it.");
        if (!Cutter.Available) return ToolResult.Error("picture selection unavailable", "This machine cannot run picture selection.");
        if (!Cutter.CanPick)
            return ToolResult.Error("picture selection not downloaded",
                "Picture selection is not on this machine yet: the user can download it in Settings > Local > Tools, or from the editor's Cut out.");

        Task<IReadOnlyList<PictureFind>>? finding = null;
        Task<bool>? picking = null;
        string? photo = null;
        await OnUi(() =>
        {
            if (CurrentSession is not { } session) return;
            photo = session.Path;
            Editor.Tool = EditTool.CutOut;
            if (!string.IsNullOrEmpty(what)) finding = Cutter.FindNamedAsync(what);
            else picking = Cutter.BoxedAsync(boxed!);
        });
        if (photo is null) return ToolResult.Error("no picture", "No picture is open to cut out of.");

        string said;
        if (finding is not null)
        {
            var finds = await finding;
            if (Cutter.Failed) return ToolResult.Error("could not cut it out", Cutter.Notice);
            if (finds.Count == 0) return ToolResult.Ok($"found no {what}", $"Nothing in '{CurrentFileName}' is \"{what}\"; nothing was saved.");
            said = $"{finds.Count} \"{what}\"";
        }
        else
        {
            if (!await picking!) return Cutter.Failed ? ToolResult.Error("could not cut it out", Cutter.Notice)
                                                      : ToolResult.Ok("found nothing in the box", "Nothing stands out inside that box; nothing was saved.");
            said = "the thing in the box";
        }

        byte[]? png;
        try
        {
            png = await Cutter.CutOutAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Error("could not cut it out", ex.Message);
        }
        if (png is null) return ToolResult.Ok("found nothing to keep", "What was picked has no pixels in this picture; nothing was saved.");

        // A draft: the tab gives the assistant the picture, never its folder, so nothing is written beside it. The user keeps the
        // cut-out where they choose, Save as starting beside the picture.
        var saved = DraftLocation.NewFile(Path.GetFileNameWithoutExtension(photo) + Str.Get("Images.CutOut.FileSuffix") + ".png");
        await _editing.FileSystem.WriteAllBytesAsync(saved, png, ct);
        _shell?.OpenTab(ArtistTabRegistration.StaticPageKind, new Dictionary<string, string>
        {
            ["path"]                      = saved,
            [DraftFile.PageParameterName] = ImageEditingServices.IsFileBacked(photo) ? Path.GetDirectoryName(photo) ?? "" : "",
        });
        return ToolResult.Ok($"cut out of {CurrentFileName}",
            $"Cut out {said} from '{CurrentFileName}' and opened it for the user in a new Artist tab, not saved yet: they save it where they choose.");
    }

    /// <summary>Format + pixel size + byte size of the picture, best-effort (any part may be unknown).</summary>
    private string DescribeCurrentImage()
    {
        var parts = new List<string> { CurrentFormat() };
        if (CurrentPixelSize() is { } px) parts.Add($"{px.Width}×{px.Height}px");
        var size = CurrentFileSize();
        if (size >= 0) parts.Add(Nexaflow.Visuals.Common.Formatting.SizeFormatter.FormatBytes(size));
        return string.Join(", ", parts);
    }

    /// <summary>Multi-line metadata block returned by get_image_info.</summary>
    private string DescribeImageInfo()
    {
        var lines = new List<string>
        {
            $"File: {CurrentFileName}",
            $"Format: {CurrentFormat()}",
        };
        if (CurrentPixelSize() is { } px) lines.Add($"Dimensions: {px.Width}×{px.Height} px{(IsEdited ? " (as edited)" : string.Empty)}");
        var size = CurrentFileSize();
        if (size >= 0) lines.Add($"Size: {Nexaflow.Visuals.Common.Formatting.SizeFormatter.FormatBytes(size)}");
        if (CurrentDocument is { } document) lines.Add($"Layers: {document.Picture.All.Count()}");
        return string.Join("\n", lines);
    }

    /// <summary>The picture's size as edited — a layered picture's canvas — once it is decoded.</summary>
    private SKSizeI? CurrentPixelSize() => CurrentDocument is { } document
        ? new SKSizeI(document.Picture.Width, document.Picture.Height)
        : CurrentSession?.OutputSize(CurrentRecipe);

    /// <summary>Upper-case format name from the file's extension (e.g. PNG, ORA); "image" if none.</summary>
    private string CurrentFormat()
    {
        var ext = Path.GetExtension(CurrentFileName);
        return string.IsNullOrEmpty(ext) ? "image" : ext.TrimStart('.').ToUpperInvariant();
    }

    /// <summary>On-disk size of the picture in bytes, or -1 when it can't be determined (e.g. in-archive).</summary>
    [LocalDiskOnly("the size of a picture on this disk, for the assistant; none for one anywhere else")]
    private long CurrentFileSize()
    {
        if (CurrentPath is not { } path) return -1;
        try { return File.Exists(path) ? new FileInfo(path).Length : -1; }
        catch { return -1; }
    }

    /// <summary>
    /// The picture as edited, as PNG bytes for the model to view — a layered picture's composite, a photo through the same pipeline
    /// as the screen — at most <see cref="CaptureEdge"/> on its longer side; null when nothing is drawn yet. Safe off the UI thread.
    /// </summary>
    private async Task<byte[]?> EncodeCurrentPngAsync()
    {
        if (CurrentDocument is { } document)
        {
            var picture = document.Picture;
            double fit = Math.Min(1, (double)CaptureEdge / Math.Max(picture.Width, picture.Height));
            using var renderer = await document.RendererForAsync(picture);
            using var composite = renderer?.Composite(fit);
            using var encoded = composite?.Encode(SKEncodedImageFormat.Png, 100);
            return encoded?.ToArray();
        }
        if (CurrentSession is not { } session || session.OutputSize(session.Recipe) is not { } output) return null;
        double scale = Math.Min(1, (double)CaptureEdge / Math.Max(output.Width, output.Height));
        using var snapshot = session.Snapshot(session.Recipe, new RenderRequest(RenderFrame.Output, scale));
        using var data = snapshot?.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

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
