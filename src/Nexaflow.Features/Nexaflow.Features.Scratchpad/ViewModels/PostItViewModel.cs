using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Scratchpad.Models;

namespace Nexaflow.Features.Scratchpad.ViewModels;

public sealed partial class PostItViewModel : ObservableObject
{
    public PostItNote Note { get; }

    [ObservableProperty] private string  _content;
    [ObservableProperty] private double  _x;
    [ObservableProperty] private double  _y;
    [ObservableProperty] private double  _width;
    [ObservableProperty] private double  _height;
    [ObservableProperty] private double  _rotation;
    [ObservableProperty] private int     _zIndex;
    [ObservableProperty] private string  _color;
    [ObservableProperty] private string  _shape;
    [ObservableProperty] private DateTimeOffset? _expiresAt;

    /// <summary>Transient (not persisted): the note's attachment folder, used as the base
    /// directory for resolving relative <c>![](file.png)</c> image paths in the editor.
    /// Set by ScratchpadViewModel from the store.</summary>
    [ObservableProperty] private string? _attachmentDirectory;

    public bool IsPinned => ExpiresAt is null;

    /// <summary>Transient (not persisted): the view enters edit mode on load when set —
    /// used so a freshly created empty note is immediately editable.</summary>
    public bool StartInEdit { get; set; }

    /// <summary>First 15 characters of the first non-empty content line — shown in the recycle bin.</summary>
    public string RecycleBinLabel
    {
        get
        {
            var line = (Content ?? string.Empty)
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            return line.Length <= 15 ? line : line[..15];
        }
    }

    public string TimeRemainingText
    {
        get
        {
            if (ExpiresAt is null) return "📌";
            var remaining = ExpiresAt.Value - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero) return "⏰";
            if (remaining.TotalHours >= 1)
                return $"⏱ {(int)remaining.TotalHours}h {remaining.Minutes:D2}m";
            return $"⏱ {remaining.Minutes}m";
        }
    }

    /// <summary>Invoked when this note should be removed from the board.</summary>
    public Action<PostItViewModel>? RequestRemove { get; set; }

    /// <summary>Invoked after any property change that needs persistence.</summary>
    public Action<PostItViewModel>? RequestSave { get; set; }

    /// <summary>Returns the highest ZIndex among all sibling notes (for BringToFront).</summary>
    public Func<int>? GetMaxZIndex { get; set; }

    /// <summary>Returns the configured lifetime for new/unpinned notes.</summary>
    public Func<TimeSpan>? GetNoteLifetime { get; set; }

    /// <summary>Handles a clicked link (file path or URL) via the shell's object dispatch.
    /// Returns true if a feature claimed it; false lets the renderer fall back to the OS browser.
    /// Wired by ScratchpadViewModel via IShellServices.</summary>
    public Func<string, bool>? OpenUrl { get; set; }

    /// <summary>Fetches a URL preview (title/description/image) in the background and calls back with the
    /// markdown to swap in for the pasted/dropped URL block. Wired by ScratchpadViewModel (which owns the
    /// store + shell). No-op when unavailable.</summary>
    public Action<string, Action<string>>? RequestUrlPreview { get; set; }

    public PostItViewModel(PostItNote note)
    {
        Note      = note;
        _content  = note.Content;
        _x        = note.X;
        _y        = note.Y;
        _width    = note.Width;
        _height   = note.Height;
        _rotation = note.Rotation;
        _zIndex   = note.ZIndex;
        _color    = note.Color;
        _shape    = note.Shape;
        _expiresAt = note.ExpiresAt;
    }

    public void RefreshTimeDisplay() => OnPropertyChanged(nameof(TimeRemainingText));

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(Content):
                Note.Content = Content;
                OnPropertyChanged(nameof(RecycleBinLabel));
                break;
            case nameof(X):        Note.X        = X;        break;
            case nameof(Y):        Note.Y        = Y;        break;
            case nameof(Width):    Note.Width    = Width;    break;
            case nameof(Height):   Note.Height   = Height;   break;
            case nameof(Rotation): Note.Rotation = Rotation; break;
            case nameof(ZIndex):   Note.ZIndex   = ZIndex;   break;
            case nameof(Color):    Note.Color    = Color;    break;
            case nameof(Shape):    Note.Shape    = Shape;    break;
            case nameof(ExpiresAt):
                Note.ExpiresAt = ExpiresAt;
                OnPropertyChanged(nameof(IsPinned));
                OnPropertyChanged(nameof(TimeRemainingText));
                break;
        }

        var skip = new[] { nameof(TimeRemainingText), nameof(IsPinned), nameof(RecycleBinLabel), nameof(AttachmentDirectory) };
        if (!skip.Contains(e.PropertyName))
            RequestSave?.Invoke(this);
    }

    [RelayCommand]
    private void ChangeColor(string color) => Color = color;

    [RelayCommand]
    private void ChangeShape(string shape) => Shape = shape;

    /// <summary>
    /// Toggles pin state. If pinned → unpin (restarts timer). If counting down → pin permanently.
    /// Clicking the timer badge calls this.
    /// </summary>
    [RelayCommand]
    private void TogglePin()
    {
        if (IsPinned)
            ExpiresAt = DateTimeOffset.Now + (GetNoteLifetime?.Invoke() ?? TimeSpan.FromHours(2));
        else
            ExpiresAt = null;
    }

    [RelayCommand]
    private void BringToFront()
    {
        ZIndex = (GetMaxZIndex?.Invoke() ?? 0) + 1;
    }

    [RelayCommand]
    private void SendToBack() => ZIndex = 0;

    [RelayCommand]
    private void Remove() => RequestRemove?.Invoke(this);
}
