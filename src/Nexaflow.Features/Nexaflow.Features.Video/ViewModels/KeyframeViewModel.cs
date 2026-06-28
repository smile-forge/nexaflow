using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Video.ViewModels;

/// <summary>
/// One thumbnail in the horizontally-scrollable scene strip: the frame image plus the timestamp it
/// was sampled at. Clicking it seeks the player to <see cref="Position"/>.
/// </summary>
public sealed partial class KeyframeViewModel : ObservableObject
{
    public required TimeSpan Position { get; init; }

    /// <summary>"01:23" / "1:02:03" label shown under the thumbnail.</summary>
    public string TimeLabel => Position.TotalHours >= 1
        ? Position.ToString(@"h\:mm\:ss")
        : Position.ToString(@"m\:ss");

    [ObservableProperty] private ImageSource? _thumbnail;
}
