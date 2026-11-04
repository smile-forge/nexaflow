using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Visuals.Common.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// One picture on show, as a tab draws it: a photo with its edits — its recipe applied, with what the picture tools make for
/// it — or a layered picture in its place, rendered for the screen through one <see cref="ImageDisplay"/>. The viewer shows
/// the pictures of its set on it one at a time; Artist the one picture it edits. The tab says which photo and how it is
/// shown; what follows from that photo's recipe and history — its tools, its sidecar, its notices — the stage keeps in step.
/// </summary>
public sealed partial class PictureStage : ObservableObject, IDisposable
{
    private readonly ImageEditingServices _editing;
    private readonly IShellServices? _shell;
    private readonly IPictureTools? _pictures;
    private readonly IPictureSelection? _selection;
    private readonly IPictureErasing? _erasing;
    private readonly Func<ImageDisplayMode> _mode;
    private readonly ImageDisplay _display;

    /// <summary>What the picture tools make for each picture layer other than the active one, quietly, by layer.</summary>
    private readonly Dictionary<Guid, PictureToolEdits> _layerTools = [];

    private IFileWatch? _sidecarWatch;

    /// <param name="mode">How the tab shows the picture now: asked each time it is rendered.</param>
    /// <param name="lifetime">The tab's lifetime: decoding and rendering stop with it.</param>
    /// <param name="pictures">The picture tools, where the app has them: what restores a photo whose recipe asks for it.</param>
    /// <param name="selection">Picture selection, where the app has it: what removes the background a recipe asks to.</param>
    /// <param name="erasing">Picture erasing, where the app has it: what fills what a recipe erases.</param>
    public PictureStage(ImageEditingServices editing, IShellServices? shell, Func<ImageDisplayMode> mode, CancellationToken lifetime,
                        IPictureTools? pictures = null, IPictureSelection? selection = null, IPictureErasing? erasing = null)
    {
        (_editing, _shell, _mode) = (editing, shell, mode);
        (_pictures, _selection, _erasing) = (pictures, selection, erasing);
        RunOnUi = action => shell is null ? RunHere(action) : shell.RunOnUiAsync(action);

        _display = new ImageDisplay(editing, RunOnUi, lifetime);
        _display.ImageChanged += image => CurrentImage = image;
        _display.Presented    += canvas => Canvas = canvas;
        _display.TonesRead    += reading => TonesRead?.Invoke(reading);
        _display.Decoded      += OnDecoded;
        _display.DecodeFailed += rendering => { if (ReferenceEquals(CurrentSession, rendering)) LoadFailed = true; };

        // AI picture editing's models, offered and fetched as one by everything here that waits on any of them.
        Models = new PictureModels([pictures, selection, erasing, editing.SceneDepth], RunOnUi);

        Restorer = new PhotoRestorer(editing, pictures, RunOnUi, Models);
        Restorer.Made += Render;
        _display.RestoredDecodeFailed += rendering =>
        {
            Restorer.Lost(rendering);
            foreach (var tools in _layerTools.Values) tools.Restorer.Lost(rendering);
        };

        Remover = new BackgroundRemover(editing, selection, RunOnUi, Models);
        Remover.Made += Render;
        _display.CutOutDecodeFailed += rendering =>
        {
            Remover.Lost(rendering);
            foreach (var tools in _layerTools.Values) tools.Remover.Lost(rendering);
        };

        SceneDepth = new PhotoSceneDepth(editing, editing.SceneDepth, RunOnUi, Models);
        SceneDepth.Made += Render;
        _display.SceneDepthReadFailed += rendering =>
        {
            SceneDepth.Lost(rendering);
            foreach (var tools in _layerTools.Values) tools.SceneDepth.Lost(rendering);
        };

        // The restoration and the scene depth are of the photo as erased: they follow each erasing as it is made.
        Eraser = new PhotoEraser(editing, erasing, selection, RunOnUi, Models);
        Eraser.Made += () =>
        {
            Restorer.Follow(EditTarget?.Rendering);
            SceneDepth.Follow(EditTarget?.Rendering);
            Render();
        };
        _display.ErasedDecodeFailed += rendering =>
        {
            Eraser.Lost(rendering);
            foreach (var tools in _layerTools.Values) tools.Eraser.Lost(rendering);
        };

        FocusedSubject = new FocusedSubject(editing, selection, RunOnUi, Models);
        FocusedSubject.Made += Render;
        _display.SubjectDecodeFailed += rendering =>
        {
            FocusedSubject.Lost(rendering);
            foreach (var tools in _layerTools.Values) tools.FocusedSubject.Lost(rendering);
        };
        // Its banner speaks only where the scene depth's — which a lens blur waits on first, for the same models — does not.
        FocusedSubject.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ToolEdit<RecipeCutOut>.HasNotice)) OnPropertyChanged(nameof(ShowsSubjectNotice)); };
        SceneDepth.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ToolEdit<SceneDepthAsk>.HasNotice)) OnPropertyChanged(nameof(ShowsSubjectNotice)); };

        // Each tool's banner offers the models for the edit it makes; while one does, no control offers them beside it.
        Models.OfferedBy(Restorer, Remover, Eraser, SceneDepth, FocusedSubject);
    }

    /// <summary>Runs on the UI thread — the shell's, or at once where there is no shell.</summary>
    public Func<Action, Task> RunOnUi { get; }

    /// <summary>What gives the picture on show the restoration its recipe asks for, and says what it is doing.</summary>
    public PhotoRestorer Restorer { get; }

    /// <summary>What gives the picture on show the removed background its recipe asks for, and says what it is doing.</summary>
    public BackgroundRemover Remover { get; }

    /// <summary>The erasing the picture's recipe asks for, made and shown in its place.</summary>
    public PhotoEraser Eraser { get; }

    /// <summary>The scene depth the picture's haze and lens blur are drawn from, estimated once and kept.</summary>
    public PhotoSceneDepth SceneDepth { get; }

    /// <summary>What gives the picture on show the outline of what its lens blur is focused on, so the blur keeps that edge crisp.</summary>
    public FocusedSubject FocusedSubject { get; }

    /// <summary>The focused subject's banner shows: its outline is being made, or could not be, and the scene depth's banner is not
    /// already speaking for the lens blur.</summary>
    public bool ShowsSubjectNotice => FocusedSubject.HasNotice && !SceneDepth.HasNotice;

    /// <summary>The tab edits the picture: edits lasting only as long as it are said at once, not only once there are some.</summary>
    public bool IsEditor { get; init; }

    /// <summary>The frame presented, as the screen shows it.</summary>
    [ObservableProperty] private BitmapSource? _currentImage;

    /// <summary>What the presented frame is a picture of; drawn over by the edit canvas.</summary>
    [ObservableProperty] private EditCanvas? _canvas;

    /// <summary>The photo on show could not be decoded.</summary>
    [ObservableProperty] private bool _loadFailed;

    /// <summary>The photo on show, with its recipe and history; null while nothing is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentRecipe), nameof(IsEdited), nameof(HasChangedSourceNotice), nameof(SessionOnlyNotice),
                              nameof(HasSessionOnlyNotice), nameof(ShowsSessionOnlyNotice), nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RotateLeftCommand), nameof(RotateRightCommand), nameof(FlipHorizontalCommand),
                                nameof(FlipVerticalCommand), nameof(RevertEditsCommand))]
    private ImageEditSession? _currentSession;

    /// <summary>The layered picture shown and edited in the photo's place; null while the photo is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLayered), nameof(CurrentRecipe), nameof(IsEdited), nameof(IsEmpty))]
    private LayeredSession? _currentDocument;

    /// <summary>A layered picture is on show.</summary>
    public bool IsLayered => CurrentDocument is not null;

    /// <summary>Nothing is on show.</summary>
    public bool IsEmpty => CurrentSession is null && CurrentDocument is null;

    /// <summary>The picture the editor's tools work on: the photo, or — in a layered picture — its active picture layer; null
    /// where there is none.</summary>
    public IRecipeEditing? EditTarget => CurrentDocument is { } document ? document.ActivePicture : CurrentSession;

    /// <summary>The annotations the Markup tool works on: the photo's, or — in a layered picture — the active picture layer's or
    /// markup layer's; null where there are none.</summary>
    public IMarkupEditing? MarkupTarget => CurrentDocument?.ActiveNotes;

    /// <summary>The edits of the picture the tools work on — the photo, or the active picture layer — nothing while there is none.</summary>
    public ImageRecipe CurrentRecipe => EditTarget?.Recipe ?? ImageRecipe.Identity;

    /// <summary>The picture carries edits — what the "edited" badge says.</summary>
    public bool IsEdited => !CurrentRecipe.IsIdentity;

    /// <summary>The photo changed after its edits were made; the user decides whether they still apply.</summary>
    public bool HasChangedSourceNotice => CurrentSession?.LoadStatus == RecipeLoadStatus.SourceChanged;

    /// <summary>Why edits to this photo last only as long as the tab, or null when they are kept beside it.</summary>
    public string? SessionOnlyNotice => CurrentSession switch
    {
        null                                        => null,
        { CanPersist: true }                        => null,
        { IsFileBacked: false }                     => Str.Get("Images.Edit.Notice.Archive"),
        { LoadStatus: RecipeLoadStatus.Newer }      => Str.Get("Images.Edit.Notice.Newer"),
        { LoadStatus: RecipeLoadStatus.Unreadable } => Str.Get("Images.Edit.Notice.Unreadable"),
        _                                           => null,
    };

    public bool HasSessionOnlyNotice => SessionOnlyNotice is not null;

    /// <summary>The session-only notice, once it matters: in the editor, or once there are edits to lose.</summary>
    public bool ShowsSessionOnlyNotice => HasSessionOnlyNotice && (IsEditor || IsEdited);

    /// <summary>A different photo is on show.</summary>
    public event Action? SessionShown;

    /// <summary>The photo's recipe, or the layered picture, changed.</summary>
    public event Action? Changed;

    /// <summary>A frame landed whose tones were asked for. On the UI thread, while what the reading marked is still that frame's.</summary>
    public event Action<ToneReading>? TonesRead;

    /// <summary>A picture on show has pixels — its first decode, a sharper one, or what a tool made — before it is rendered again.</summary>
    public event Action<PictureRendering>? Decoded;

    /// <summary>A picture tool made what a layered picture's other layer asks for: the picture has more to write.</summary>
    public event Action? LayerToolMade;

    // ── What is on show ───────────────────────────────────────────────────

    /// <summary>Shows <paramref name="session"/> — null, nothing — its tools following it and its sidecar watched. The session stays
    /// the caller's to close; a layered picture on show is let go first, the caller having written it.</summary>
    public void Show(ImageEditSession? session)
    {
        if (ReferenceEquals(CurrentSession, session) && session?.Proxy is not null && CurrentDocument is null)
        {
            Render();
            return;
        }
        DetachDocument();
        if (CurrentSession is { } previous) previous.RecipeChanged -= OnRecipeChanged;
        _sidecarWatch?.Dispose();
        _sidecarWatch = null;

        CurrentSession = session;
        LoadFailed = false;
        Canvas = null;
        if (session is null)
        {
            _display.Clear();
            FollowTools();
            SessionShown?.Invoke();
            return;
        }
        _display.Show(session);
        session.RecipeChanged += OnRecipeChanged;
        if (session.CanPersist && _shell is not null)
            _sidecarWatch = _shell.WatchFile(ImageRecipeStore.SidecarPath(session.Path), () => CurrentSession?.ReloadFromSidecar());
        FollowTools();
        SessionShown?.Invoke();
        Render();
    }

    /// <summary>Shows <paramref name="document"/> in the photo's place — null, the photo again. The layered picture stays the
    /// caller's to write and close.</summary>
    public void ShowDocument(LayeredSession? document)
    {
        if (ReferenceEquals(CurrentDocument, document)) return;
        DetachDocument();
        CurrentDocument = document;
        Canvas = null;
        if (document is not null)
        {
            document.PictureChanged += OnDocumentChanged;
            _display.ShowDocument(document);
        }
        else if (CurrentSession is { } session) _display.Show(session);
        else _display.Clear();
        OnDocumentChanged();
    }

    /// <summary>The layered picture on show let go, with the tools that followed its other layers.</summary>
    private void DetachDocument()
    {
        if (CurrentDocument is not { } document) return;
        document.PictureChanged -= OnDocumentChanged;
        foreach (var tools in _layerTools.Values) tools.Dispose();
        _layerTools.Clear();
        CurrentDocument = null;
    }

    /// <summary>Renders what is on show for the way the tab shows it now.</summary>
    public void Render() => _display.Render(_mode());

    /// <summary>The surface the picture is shown on changed size (device-independent pixels) or density: whether to render again.</summary>
    public bool SetViewport(double width, double height, double pixelsPerDip) => _display.SetViewport(width, height, pixelsPerDip);

    /// <summary>The edit canvas was zoomed: whether more (or fewer) pixels are needed for the same layout.</summary>
    public bool SetEditZoom(double zoom) => _display.SetEditZoom(zoom);

    private void OnRecipeChanged()
    {
        OnPropertyChanged(nameof(CurrentRecipe));
        OnPropertyChanged(nameof(IsEdited));
        OnPropertyChanged(nameof(HasChangedSourceNotice));
        OnPropertyChanged(nameof(SessionOnlyNotice));
        OnPropertyChanged(nameof(HasSessionOnlyNotice));
        OnPropertyChanged(nameof(ShowsSessionOnlyNotice));
        FollowTools();
        Changed?.Invoke();
        Render();
    }

    private void OnDocumentChanged()
    {
        OnPropertyChanged(nameof(CurrentRecipe));
        OnPropertyChanged(nameof(IsEdited));
        FollowTools();
        Changed?.Invoke();
        Render();
    }

    private void OnDecoded(PictureRendering rendering)
    {
        if (CurrentDocument is null && !ReferenceEquals(rendering, CurrentSession)) return;
        Decoded?.Invoke(rendering);
        Render();
    }

    /// <summary>
    /// The picture tools follow what is shown: the stage's own — whose banners speak — the picture the tools work on; each other
    /// picture layer of a layered picture its own, quietly, so it is drawn as its recipe asks too.
    /// </summary>
    private void FollowTools()
    {
        var target = EditTarget?.Rendering;
        Eraser.Follow(target);
        Restorer.Follow(target);
        Remover.Follow(target);
        SceneDepth.Follow(target);
        FocusedSubject.Follow(target);
        if (CurrentDocument is not { } document)
        {
            foreach (var tools in _layerTools.Values) tools.Dispose();
            _layerTools.Clear();
            return;
        }
        var seen = new HashSet<Guid>();
        foreach (var layer in document.Picture.All.OfType<PictureLayer>())
        {
            if (document.RenderingOf(layer.Id) is not { } rendering || ReferenceEquals(rendering, target)) continue;
            seen.Add(layer.Id);
            if (!_layerTools.TryGetValue(layer.Id, out var tools))
                _layerTools[layer.Id] = tools = new PictureToolEdits(_editing, _pictures, _selection, _erasing, Models, RunOnUi, () =>
                {
                    Render();
                    LayerToolMade?.Invoke();
                });
            tools.Follow(rendering);
        }
        foreach (var gone in _layerTools.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _layerTools[gone].Dispose();
            _layerTools.Remove(gone);
        }
    }

    /// <summary>What the tools made and kept for <paramref name="photo"/>'s recipe — its restoration made from the photo as
    /// erased, where it erases — for thumbnails, the clipboard and the page reader.</summary>
    public MadeByTools KeptByTools(string photo, ImageRecipe recipe)
    {
        var erased = recipe.Erasures.IsEmpty ? null : Eraser.KeptFor(photo, new EraseSteps(recipe.Erasures));
        var madeFrom = recipe.Erasures.IsEmpty ? photo : erased;
        return new(recipe.Restore is { } restore && madeFrom is not null ? Restorer.KeptFor(madeFrom, restore) : null,
                   recipe.CutOut is { } cutOut ? Remover.KeptFor(photo, cutOut) : null,
                   erased,
                   recipe.AsksSceneDepth && madeFrom is not null ? SceneDepth.KeptFor(madeFrom, SceneDepthAsk.Instance) : null,
                   recipe.LensBlur?.Subject is { } subject ? FocusedSubject.KeptFor(photo, subject) : null);
    }

    // ── Edits of the picture the tools work on ────────────────────────────

    /// <summary>Opens one step of the picture's history — the layered picture's, or the photo's — for everything done until it is
    /// disposed, gestures among it included: changes said at once, undone at once.</summary>
    public IDisposable Batch() => CurrentDocument?.History.Batch() ?? CurrentSession?.History.Batch() ?? NothingShown.Instance;

    /// <summary>The batch of a stage showing nothing: there is no history to fold into.</summary>
    private sealed class NothingShown : IDisposable
    {
        public static NothingShown Instance { get; } = new();

        public void Dispose() { }
    }

    /// <summary>Applies <paramref name="change"/> to the recipe of the picture the tools work on — the photo, or the active
    /// picture layer — as one undoable step.</summary>
    public void Edit(Func<ImageRecipe, ImageRecipe> change)
    {
        if (EditTarget is { } target) target.Commit(change(target.Recipe));
    }

    /// <summary>A continuous change begins — a slider drag — and lasts as one undo step of the photo's, or the layered
    /// picture's, history until <see cref="EndEditGesture"/>.</summary>
    public void BeginEditGesture()
    {
        if (CurrentDocument is { } document) document.History.BeginGesture();
        else CurrentSession?.History.BeginGesture();
    }

    public void EndEditGesture()
    {
        if (CurrentDocument is { } document) document.History.EndGesture();
        else CurrentSession?.History.EndGesture();
    }

    public bool CanUndo => CurrentDocument?.History.CanUndo ?? CurrentSession?.History.CanUndo == true;

    public bool CanRedo => CurrentDocument?.History.CanRedo ?? CurrentSession?.History.CanRedo == true;

    /// <summary>The last step taken back: the layered picture's, or the photo's.</summary>
    public void Undo()
    {
        if (CurrentDocument is { } document) document.History.Undo();
        else CurrentSession?.History.Undo();
    }

    public void Redo()
    {
        if (CurrentDocument is { } document) document.History.Redo();
        else CurrentSession?.History.Redo();
    }

    private bool HasSession() => CurrentSession is not null;

    // A turn or a flip is an edit: remembered with the photo, and undone with Undo.

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void RotateLeft() => Edit(r => r with { Geometry = r.Geometry.Turned(-1) });

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void RotateRight() => Edit(r => r with { Geometry = r.Geometry.Turned(1) });

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void FlipHorizontal() => Edit(r => r with { Geometry = r.Geometry.Flipped(horizontal: true) });

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void FlipVertical() => Edit(r => r with { Geometry = r.Geometry.Flipped(horizontal: false) });

    /// <summary>The photo changed since its edits were made: keep them, anchored to the photo as it is now.</summary>
    [RelayCommand]
    private void KeepChangedEdits() => CurrentSession?.KeepEditsForChangedSource();

    /// <summary>The photo changed since its edits were made: drop them.</summary>
    [RelayCommand]
    private void DiscardChangedEdits()
    {
        if (CurrentSession is not { } session) return;
        session.Revert();
        session.KeepEditsForChangedSource();
    }

    /// <summary>Back to the photo as it is on disk — one undoable step; the sidecar goes with it.</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private void RevertEdits() => CurrentSession?.Revert();

    private static Task RunHere(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (CurrentDocument is { } document) document.PictureChanged -= OnDocumentChanged;
        if (CurrentSession is { } session) session.RecipeChanged -= OnRecipeChanged;
        foreach (var tools in _layerTools.Values) tools.Dispose();
        _layerTools.Clear();
        _sidecarWatch?.Dispose();
        _display.Dispose();
    }

    /// <summary>AI picture editing's models — the picture tools, picture selection and picture erasing — offered and fetched as one.</summary>
    public PictureModels Models { get; }
}
