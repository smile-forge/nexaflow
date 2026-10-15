using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>A length a clip can be made in, as the box offers it.</summary>
public sealed record ClipLength(int Seconds)
{
    public string Label => Str.Format("Images.MakeVideo.SecondsFormat", Seconds);
}

/// <summary>
/// Making a video from the picture on screen, the user driving: a box on the toolbar for what happens, how long it runs
/// and which way up, then the clip made straight away as one of the workspace's background jobs, from the picture as it
/// is shown, edits and all. Offered only where the workspace has a model that carries a picture on into a clip.
/// </summary>
public sealed partial class MakeVideoViewModel : ObservableObject
{
    private readonly IAIService? _ai;
    private readonly Func<bool> _isTall;
    private readonly Func<byte[]?> _picture;

    /// <param name="isTall">Whether the picture on screen is taller than wide.</param>
    /// <param name="picture">The picture on screen, edits applied, as a PNG; null when none is shown.</param>
    public MakeVideoViewModel(IAIService? ai, Func<bool> isTall, Func<byte[]?> picture)
    {
        _ai      = ai;
        _isTall  = isTall;
        _picture = picture;
        Refresh();
    }

    /// <summary>Whether the workspace makes clips from a picture now — the button shows only then.</summary>
    [ObservableProperty] private bool _canMake;

    /// <summary>Whether the box is open.</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>What happens in the clip, in the user's words.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MakeCommand))]
    private string _description = string.Empty;

    /// <summary>The lengths the workspace's model makes, shortest first.</summary>
    public ObservableCollection<ClipLength> Lengths { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MakeCommand))]
    private ClipLength? _length;

    /// <summary>Taller than wide; otherwise wider than tall. Starts as the picture is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Landscape))]
    private bool _portrait;

    /// <summary>Wider than tall: the other choice to <see cref="Portrait"/>, for the box's pair of buttons.</summary>
    public bool Landscape
    {
        get => !Portrait;
        set => Portrait = !value;
    }

    /// <summary>Asks the workspace again what it makes clips from a picture with: its models may have changed.</summary>
    public void Refresh()
    {
        var offered = _ai?.PictureClipLengths ?? [];
        if (!offered.SequenceEqual(Lengths.Select(l => l.Seconds)))
        {
            Lengths.Clear();
            foreach (var seconds in offered) Lengths.Add(new ClipLength(seconds));
            // The length a clip is made at when the assistant is not told one: the one nearest eight seconds, the longer of two.
            Length = Lengths.OrderBy(l => Math.Abs(l.Seconds - 8)).ThenByDescending(l => l.Seconds).FirstOrDefault();
        }
        CanMake = Lengths.Count > 0;
        if (!CanMake) IsOpen = false;
    }

    [RelayCommand]
    private void Open()
    {
        Refresh();
        if (!CanMake) return;
        Portrait = _isTall();
        IsOpen   = true;
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    private bool CanStart => Description.Trim().Length > 0 && Length is not null;

    /// <summary>Makes the clip: it runs on in the background, listed with the workspace's other jobs, and opens when made.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Make()
    {
        if (_ai is null || Length is not { } length || _picture() is not { } png) return;
        try
        {
            _ai.MakeClipFromPicture(new ClipFromPicture(png, Description.Trim(), length.Seconds, Portrait));
        }
        catch (InvalidOperationException)
        {
            Refresh();   // the workspace's models changed since the box opened: it no longer makes clips from a picture
            return;
        }
        IsOpen      = false;
        Description = string.Empty;
    }
}
