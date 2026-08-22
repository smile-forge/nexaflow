using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Audio.ViewModels;

/// <summary>Which slide-out panel the side drawer shows.</summary>
public enum AudioPanel { Tags, Lyrics }

/// <summary>One entry in the playlist drawer.</summary>
public partial class PlaylistItemViewModel : ObservableObject
{
    public PlaylistItemViewModel(string path)
    {
        Path = path;
        Display = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    public string Path { get; }
    public string Display { get; }

    /// <summary>True for the track currently loaded/playing — highlighted in the list.</summary>
    [ObservableProperty] private bool _isCurrent;
}
