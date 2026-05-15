using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Scratchpad.Models;
using Nexaflow.Features.Scratchpad.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace Nexaflow.Features.Scratchpad.ViewModels;

public sealed partial class ScratchpadViewModel : ObservableObject, IDisposable
{
    private readonly PostItStore      _store;
    private readonly ScratchpadConfig _config;
    private readonly ITabOpener?      _tabOpener;
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

    public Func<string, string, bool>? ConfirmAction { get; set; }

    public ScratchpadViewModel(ScratchpadConfig config, ITabOpener? tabOpener = null)
    {
        _config    = config;
        _tabOpener = tabOpener;
        _store     = new PostItStore();

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
            RequestRemove   = RemoveNote,
            RequestSave     = ScheduleSave,
            GetMaxZIndex    = () => Notes.Count > 0 ? Notes.Max(n => n.ZIndex) : 0,
            GetNoteLifetime = () => _config.GetNoteLifetime(),
            OpenUrl         = url => _tabOpener?.OpenTab("Web", new() { ["url"] = url })
        };
        Notes.Add(vm);
        return vm;
    }

    // ── Commands ─────────────────────────────────────────────────────────

    public PostItViewModel AddNote(Point canvasPosition)
    {
        var note = new PostItNote
        {
            X         = canvasPosition.X - 100,
            Y         = canvasPosition.Y - 100,
            ZIndex    = Notes.Count > 0 ? Notes.Max(n => n.ZIndex) + 1 : 1,
            Rotation  = (_rng.NextDouble() * 16) - 8,
            ExpiresAt = DateTimeOffset.Now + _config.GetNoteLifetime()
        };

        _store.Save(note);
        var vm = AddNoteViewModel(note);
        UpdateStatus();
        return vm;
    }

    [RelayCommand]
    private void PasteAsNote()
    {
        string text;
        try { text = Clipboard.GetText(); } catch { return; }
        if (string.IsNullOrEmpty(text)) return;

        var center = ViewportCenter();
        var note = new PostItNote
        {
            Content   = text,
            X         = center.X - 100,
            Y         = center.Y - 100,
            ZIndex    = Notes.Count > 0 ? Notes.Max(n => n.ZIndex) + 1 : 1,
            Rotation  = (_rng.NextDouble() * 16) - 8,
            ExpiresAt = DateTimeOffset.Now + _config.GetNoteLifetime()
        };

        _store.Save(note);
        AddNoteViewModel(note);
        UpdateStatus();
    }

    public void AddNoteWithContent(string content, Point canvasPosition)
    {
        var note = new PostItNote
        {
            Content   = content,
            X         = canvasPosition.X - 100,
            Y         = canvasPosition.Y - 100,
            ZIndex    = Notes.Count > 0 ? Notes.Max(n => n.ZIndex) + 1 : 1,
            Rotation  = (_rng.NextDouble() * 16) - 8,
            ExpiresAt = DateTimeOffset.Now + _config.GetNoteLifetime()
        };

        _store.Save(note);
        AddNoteViewModel(note);
        UpdateStatus();
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
        var confirmed = ConfirmAction?.Invoke("Empty Recycle Bin",
            "Permanently delete all notes in the recycle bin?") ?? true;
        if (!confirmed) return;

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
        vm.OpenUrl          = url => _tabOpener?.OpenTab("Web", new() { ["url"] = url });
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
}
