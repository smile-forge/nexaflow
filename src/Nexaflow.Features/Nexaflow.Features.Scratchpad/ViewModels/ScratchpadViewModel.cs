using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Scratchpad.ClientTools;
using Nexaflow.Features.Scratchpad.Models;
using Nexaflow.Features.Scratchpad.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Nexaflow.Features.Scratchpad.ViewModels;

public sealed partial class ScratchpadViewModel : ObservableObject, IDisposable, IPageViewModel
{
    private readonly PostItStore      _store;
    private readonly ScratchpadConfig _config;
    private readonly IShellServices?  _shellServices;
    private readonly DispatcherTimer  _expiryTimer;
    private readonly Random           _rng = Random.Shared;

    private readonly Dictionary<Guid, DispatcherTimer> _saveTimers = [];

    public ObservableCollection<PostItViewModel> Notes           { get; } = [];
    public ObservableCollection<PostItViewModel> RecycleBinNotes { get; } = [];

    [ObservableProperty] private bool   _showingRecycleBin;
    [ObservableProperty] private double _scale    = 1.0;
    [ObservableProperty] private double _offsetX  = 0;
    [ObservableProperty] private double _offsetY  = 0;
    [ObservableProperty] private string _statusText = string.Empty;

    public ScratchpadViewModel(ScratchpadConfig config, IShellServices? shellServices = null)
        : this(config, new PostItStore(), shellServices)
    {
    }

    public ScratchpadViewModel(ScratchpadConfig config, PostItStore store, IShellServices? shellServices = null)
    {
        _config        = config;
        _shellServices = shellServices;
        _store         = store;

        LoadNotes();
        PurgeRecycleBinOnStartup();
        UpdateStatus();

        _expiryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _expiryTimer.Tick += (_, _) => CheckExpiry();
        _expiryTimer.Start();
    }

    private void LoadNotes()
    {
        foreach (var note in _store.LoadAll())
            AddNoteViewModel(note);
    }

    private PostItViewModel AddNoteViewModel(PostItNote note)
    {
        var vm = new PostItViewModel(note)
        {
            RequestRemove       = RemoveNote,
            RequestSave         = ScheduleSave,
            GetMaxZIndex        = () => Notes.Count > 0 ? Notes.Max(n => n.ZIndex) : 0,
            GetNoteLifetime     = () => _config.GetNoteLifetime(),
            OpenUrl             = HandleLink,
            AttachmentDirectory = _store.AttachmentDir(note.Id),
            RequestUrlPreview   = (url, onReady) => QueueUrlPreview(note.Id, url, onReady),
        };
        Notes.Add(vm);
        return vm;
    }

    /// <summary>Routes a clicked link (file path or http(s) URL) to the shell's object dispatch;
    /// the file-system / web feature claims it. Returns false when unclaimed so the markdown
    /// renderer falls back to opening it in the OS browser.</summary>
    private bool HandleLink(string url) => _shellServices?.HandleObject(url) ?? false;

    /// <summary>Queues a background fetch of a URL's preview (downloading any image into the note's
    /// attachment folder) and invokes <paramref name="onReady"/> on the UI thread with the markdown.</summary>
    private void QueueUrlPreview(Guid noteId, string url, Action<string> onReady)
    {
        if (_shellServices is null) return;
        var task = new UrlPreviewTask(url, _store.EnsureAttachmentDir(noteId));
        _shellServices.QueueBackgroundTask(task, onComplete: ok =>
        {
            if (ok && task.PreviewMarkdown is { } md) onReady(md);
        });
    }

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>A new post-it centred on <paramref name="canvasPosition"/> (notes are 200 wide),
    /// with a fresh z-order, slight random tilt and the configured lifetime.</summary>
    private PostItNote CreateNoteAt(Point canvasPosition, string content = "") => new()
    {
        Content   = content,
        X         = canvasPosition.X - 100,
        Y         = canvasPosition.Y - 100,
        ZIndex    = Notes.Count > 0 ? Notes.Max(n => n.ZIndex) + 1 : 1,
        Rotation  = (_rng.NextDouble() * 16) - 8,
        ExpiresAt = DateTimeOffset.Now + _config.GetNoteLifetime()
    };

    public PostItViewModel AddNote(Point canvasPosition)
    {
        var note = CreateNoteAt(canvasPosition);
        _store.Save(note);
        var vm = AddNoteViewModel(note);
        vm.StartInEdit = true;   // freshly created empty note opens ready to type
        UpdateStatus();
        return vm;
    }

    /// <summary>Creates a plain text/markdown note.</summary>
    public PostItViewModel AddNoteWithContent(string content, Point canvasPosition)
    {
        var note = CreateNoteAt(canvasPosition, content);
        _store.Save(note);
        var vm = AddNoteViewModel(note);
        UpdateStatus();
        return vm;
    }

    /// <summary>Creates an image post-it from a dropped image file: copies the file into the note's
    /// attachment folder (it then follows the note's lifetime) and renders it via <c>![](name)</c>.</summary>
    public PostItViewModel AddImageNote(string sourcePath, Point canvasPosition)
    {
        var note = CreateNoteAt(canvasPosition);
        var dir  = _store.EnsureAttachmentDir(note.Id);
        if (DroppedMedia.CopyImageInto(sourcePath, dir) is { } fileName)
        {
            note.Content = $"![]({fileName})";
            SizeNoteToImage(note, Path.Combine(dir, fileName));
        }
        else
        {
            note.Content = DroppedMedia.FileLinkMarkdown(sourcePath);   // couldn't copy → link instead
        }
        _store.Save(note);
        var vm = AddNoteViewModel(note);
        UpdateStatus();
        return vm;
    }

    /// <summary>Creates an image post-it from a raw bitmap (e.g. a browser image drag with no file):
    /// encodes it as PNG into the note's attachment folder.</summary>
    public PostItViewModel AddBitmapNote(System.Windows.Media.Imaging.BitmapSource bitmap, Point canvasPosition)
    {
        var note = CreateNoteAt(canvasPosition);
        var dir  = _store.EnsureAttachmentDir(note.Id);
        if (DroppedMedia.SaveBitmapInto(bitmap, dir) is { } fileName)
        {
            note.Content = $"![]({fileName})";
            SizeNoteToImage(note, Path.Combine(dir, fileName));
        }
        _store.Save(note);
        var vm = AddNoteViewModel(note);
        UpdateStatus();
        return vm;
    }

    /// <summary>Creates a post-it with a clickable link to a non-image file or folder. Clicking it
    /// routes through the shell so it opens with its default action (see <see cref="HandleLink"/>).</summary>
    public PostItViewModel AddFileLinkNote(string path, Point canvasPosition)
    {
        var note = CreateNoteAt(canvasPosition, DroppedMedia.FileLinkMarkdown(path));
        _store.Save(note);
        var vm = AddNoteViewModel(note);
        UpdateStatus();
        return vm;
    }

    /// <summary>Creates a post-it showing the bare URL immediately, then queues a background task to
    /// replace it with a rich preview (title, description, downloaded image) — see <see cref="UrlPreviewTask"/>.</summary>
    public PostItViewModel AddUrlNote(string url, Point canvasPosition)
    {
        var note = CreateNoteAt(canvasPosition, url);
        _store.Save(note);
        var vm = AddNoteViewModel(note);
        UpdateStatus();

        // The note is the single URL block; on completion swap its whole content for the preview.
        // (onReady runs on the UI thread; setting Content triggers the debounced save.)
        QueueUrlPreview(note.Id, url, md => vm.Content = md);
        return vm;
    }

    /// <summary>Creates a board note from the AI: the given content on a fixed white, diagonally-rounded
    /// note — a consistent look so AI-authored notes are easy to spot. Placed near the current viewport
    /// centre with a slight cascade so successive notes don't perfectly overlap. Returns its snapshot.</summary>
    public NoteSnapshot AddNoteFromAi(string content)
    {
        var basePt  = ViewportCenter();
        var cascade = Notes.Count % 8;
        var note    = CreateNoteAt(new Point(basePt.X + cascade * 24, basePt.Y + cascade * 24), content);
        note.Color  = "White";
        note.Shape  = "DiagonalsRounded";

        _store.Save(note);
        var vm = AddNoteViewModel(note);
        UpdateStatus();
        return ToSnapshot(vm);
    }

    /// <summary>Sizes a note so a freshly added image fits without clipping, capped and aspect-preserved.</summary>
    private static void SizeNoteToImage(PostItNote note, string imagePath)
    {
        try
        {
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(imagePath),
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.None);
            double w = frame.PixelWidth, h = frame.PixelHeight;
            if (w <= 0 || h <= 0) return;

            const double max = 360, min = 140, padW = 16, chromeH = 44;
            var scale = Math.Min(1.0, max / Math.Max(w, h));
            note.Width  = Math.Max(min, w * scale + padW);
            note.Height = Math.Max(min, h * scale + chromeH);
        }
        catch { /* keep default size */ }
    }

    [RelayCommand]
    private void ToggleRecycleBin()
    {
        ShowingRecycleBin = !ShowingRecycleBin;
        if (ShowingRecycleBin) LoadRecycleBin();
    }

    [RelayCommand]
    private void EmptyRecycleBin()
    {
        if (_shellServices is null) { EmptyRecycleBinConfirmed(); return; }

        _shellServices.ShowConfirmation(
            "Empty Recycle Bin",
            "Permanently delete all notes in the recycle bin?",
            onConfirm: EmptyRecycleBinConfirmed,
            onCancel:  () => { });
    }

    private void EmptyRecycleBinConfirmed()
    {
        _store.EmptyRecycleBin();
        RecycleBinNotes.Clear();
    }

    [RelayCommand]
    private void RestoreNote(PostItViewModel vm)
    {
        _store.RestoreFromRecycleBin(vm.Note);
        RecycleBinNotes.Remove(vm);
        vm.ExpiresAt        = null;
        vm.RequestRemove    = RemoveNote;
        vm.RequestSave      = ScheduleSave;
        vm.GetMaxZIndex     = () => Notes.Count > 0 ? Notes.Max(n => n.ZIndex) : 0;
        vm.GetNoteLifetime  = () => _config.GetNoteLifetime();
        vm.OpenUrl          = HandleLink;
        vm.AttachmentDirectory = _store.AttachmentDir(vm.Note.Id);
        vm.RequestUrlPreview   = (url, onReady) => QueueUrlPreview(vm.Note.Id, url, onReady);
        Notes.Add(vm);
        UpdateStatus();
    }

    [RelayCommand]
    private void DeleteFromBin(PostItViewModel vm)
    {
        _store.Delete(vm.Note);
        RecycleBinNotes.Remove(vm);
    }

    public void ZoomToFitWithViewport(double viewW, double viewH)
    {
        if (Notes.Count == 0) { Scale = 1; OffsetX = 0; OffsetY = 0; return; }

        var minX = Notes.Min(n => n.X);
        var minY = Notes.Min(n => n.Y);
        var maxX = Notes.Max(n => n.X + n.Width);
        var maxY = Notes.Max(n => n.Y + n.Height);

        var contentW = maxX - minX;
        var contentH = maxY - minY;
        const double padding = 60;

        var scaleX = (viewW - padding * 2) / contentW;
        var scaleY = (viewH - padding * 2) / contentH;
        Scale = Math.Clamp(Math.Min(scaleX, scaleY), 0.1, 4.0);

        OffsetX = padding - minX * Scale;
        OffsetY = padding - minY * Scale;
    }

    // ── Expiry ───────────────────────────────────────────────────────────

    private void CheckExpiry()
    {
        var now     = DateTimeOffset.Now;
        var expired = Notes.Where(n => n.ExpiresAt.HasValue && n.ExpiresAt.Value <= now).ToList();
        foreach (var vm in expired)
        {
            _store.MoveToRecycleBin(vm.Note);
            Notes.Remove(vm);
        }

        foreach (var vm in Notes)
            vm.RefreshTimeDisplay();

        if (expired.Count > 0) UpdateStatus();
    }

    private void PurgeRecycleBinOnStartup()
        => _store.PurgeRecycleBin(_config.GetRetentionDays());

    // ── Persistence ──────────────────────────────────────────────────────

    private void RemoveNote(PostItViewModel vm)
    {
        _store.MoveToRecycleBin(vm.Note);
        Notes.Remove(vm);
        CancelSave(vm.Note.Id);
        UpdateStatus();
    }

    private void ScheduleSave(PostItViewModel vm)
    {
        var id = vm.Note.Id;
        if (_saveTimers.TryGetValue(id, out var existing))
        {
            existing.Stop();
            existing.Start();
            return;
        }

        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _saveTimers.Remove(id);
            _store.Save(vm.Note);
        };
        _saveTimers[id] = timer;
        timer.Start();
    }

    private void CancelSave(Guid id)
    {
        if (_saveTimers.Remove(id, out var t)) t.Stop();
    }

    private void LoadRecycleBin()
    {
        RecycleBinNotes.Clear();
        foreach (var note in _store.LoadRecycleBin())
            RecycleBinNotes.Add(new PostItViewModel(note));
    }

    private void UpdateStatus()
        => StatusText = $"{Notes.Count} note{(Notes.Count == 1 ? "" : "s")}";

    private Point ViewportCenter() => new(-OffsetX / Scale + 400, -OffsetY / Scale + 300);

    public void Dispose()
    {
        _expiryTimer.Stop();
        foreach (var t in _saveTimers.Values) t.Stop();
        _saveTimers.Clear();
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        if (Notes.Count == 0) return "Scratchpad: empty.";

        // A per-colour breakdown so the model knows what's on the board before reading anything
        // (e.g. it can act on "look at the green notes" directly).
        var byColour = Notes.GroupBy(n => n.Color)
                            .OrderByDescending(g => g.Count())
                            .Select(g => $"{g.Count()} {g.Key}");
        return $"Scratchpad: {Notes.Count} note(s) ({string.Join(", ", byColour)}).";
    }

    /// <summary>The scratchpad is a single per-workspace board — a stable constant scope so its note tools
    /// don't collapse first-wins against another pinned page (aspect-4 disambiguation).</summary>
    public string? GetSecurityContext() => "scratchpad";

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new ScratchpadListNotesTool(this),
        new ScratchpadReadNotesTool(this),
        new ScratchpadAddNoteTool(this),
    ];

    public string GetAiSystemPromptGuidance() =>
        "This is a scratchpad of sticky notes — each has a colour, a shape and markdown content. Use " +
        "scratchpad_list_notes to see what's on the board, scratchpad_read_notes to read note content, and " +
        "scratchpad_add_note to add one. Reads can be filtered by colour (Yellow/Blue/Green/Pink/Orange/" +
        "Purple/White) or shape, so a request like \"look at the green notes\" maps to a colour filter.";

    /// <summary>Snapshot of the notes currently on the board (excludes the recycle bin), front-most first,
    /// for the read-only AI tools. A copy, so the tools never touch the live UI collection.</summary>
    public IReadOnlyList<NoteSnapshot> SnapshotNotes()
        => Notes.OrderByDescending(n => n.ZIndex).Select(ToSnapshot).ToList();

    private static NoteSnapshot ToSnapshot(PostItViewModel n) => new(
        n.Note.Id.ToString("N")[..8],
        n.Color, n.Shape, n.IsPinned, TimeLeftText(n),
        (int)n.X, (int)n.Y, (int)n.Width, (int)n.Height,
        n.Content ?? string.Empty);

    private static string TimeLeftText(PostItViewModel n)
    {
        if (n.IsPinned || n.ExpiresAt is not { } exp) return "pinned";
        var rem = exp - DateTimeOffset.Now;
        if (rem <= TimeSpan.Zero) return "expiring";
        return rem.TotalHours >= 1 ? $"{(int)rem.TotalHours}h {rem.Minutes}m left" : $"{rem.Minutes}m left";
    }
}

/// <summary>An at-a-glance view of one board note for the AI tools — id, appearance, placement, content.</summary>
public sealed record NoteSnapshot(
    string Id, string Color, string Shape, bool Pinned, string TimeLeft,
    int X, int Y, int Width, int Height, string Content);
