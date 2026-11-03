using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Wpf;
using Nexaflow.Visuals.Common.Localization;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// The histogram at the head of Adjust: how the picture being edited spreads over its tones, read from each frame the editor
/// draws while the histogram is on screen, and whether its shadows or highlights clip — tinted on the picture while their clip
/// mark is pointed at, and kept tinted once it is pressed.
/// </summary>
/// <remarks>
/// Frames are read only while the histogram is shown, and what clips is marked only while it is asked to be seen, so a
/// histogram folded away — or another tool — costs a slider drag nothing.
/// </remarks>
public sealed partial class PictureTones : ObservableObject
{
    private readonly Action _render;

    /// <summary>The ends the masks were last written for: the frame on screen's.</summary>
    private ClipEnds _masked;

    /// <param name="render">Asks for a frame: what reads the tones again, marking what clips as now asked.</param>
    public PictureTones(Action render) => _render = render;

    /// <summary>The tones of the last frame read; none until one is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShadowsClip), nameof(HighlightsClip), nameof(ShadowsStatus), nameof(HighlightsStatus))]
    private ToneHistogram? _histogram;

    /// <summary>The histogram is on screen: only then are frames read.</summary>
    [ObservableProperty] private bool _isShown;

    /// <summary>The shadows' clipping is kept tinted on the picture: its mark was pressed.</summary>
    [ObservableProperty] private bool _keepsShadows;

    /// <summary>The highlights' clipping is kept tinted on the picture: its mark was pressed.</summary>
    [ObservableProperty] private bool _keepsHighlights;

    /// <summary>The shadows' mark is pointed at: their clipping is tinted while it is.</summary>
    [ObservableProperty] private bool _pointsAtShadows;

    /// <summary>The highlights' mark is pointed at: their clipping is tinted while it is.</summary>
    [ObservableProperty] private bool _pointsAtHighlights;

    partial void OnIsShownChanged(bool value) => Follow();

    partial void OnKeepsShadowsChanged(bool value) => Follow();

    partial void OnKeepsHighlightsChanged(bool value) => Follow();

    partial void OnPointsAtShadowsChanged(bool value) => Follow();

    partial void OnPointsAtHighlightsChanged(bool value) => Follow();

    /// <summary>What is asked changed: an end no longer asked for stops showing at once, and a frame is drawn for the rest.</summary>
    private void Follow()
    {
        OnPropertyChanged(nameof(ShowsShadows));
        OnPropertyChanged(nameof(ShowsHighlights));
        _render();
    }

    /// <summary>The ends whose clipping is to be tinted on the picture.</summary>
    public ClipEnds Marked => !IsShown ? ClipEnds.None
        : (KeepsShadows || PointsAtShadows ? ClipEnds.Shadows : ClipEnds.None) | (KeepsHighlights || PointsAtHighlights ? ClipEnds.Highlights : ClipEnds.None);

    /// <summary>What the display is asked to read of each frame.</summary>
    public ToneAsk Ask => new(IsShown, Marked);

    /// <summary>The masks the canvas tints, written from each frame read with an end marked.</summary>
    public ClipMaskBitmaps Masks { get; } = new();

    /// <summary>Opaque where the shadows clip, in the frame on screen.</summary>
    public BitmapSource? ShadowMask => Masks.Shadows;

    /// <summary>Opaque where the highlights clip, in the frame on screen.</summary>
    public BitmapSource? HighlightMask => Masks.Highlights;

    /// <summary>The shadows' clipping is tinted on the picture: asked for, and marked in the frame on screen.</summary>
    public bool ShowsShadows => Marked.HasFlag(ClipEnds.Shadows) && _masked.HasFlag(ClipEnds.Shadows);

    /// <summary>The highlights' clipping is tinted on the picture: asked for, and marked in the frame on screen.</summary>
    public bool ShowsHighlights => Marked.HasFlag(ClipEnds.Highlights) && _masked.HasFlag(ClipEnds.Highlights);

    /// <summary>The shadows clip in at least one channel: their mark is lit.</summary>
    public bool ShadowsClip => Histogram is { } tones && tones.ShadowsClipped != ToneChannels.None;

    /// <summary>The highlights clip in at least one channel: their mark is lit.</summary>
    public bool HighlightsClip => Histogram is { } tones && tones.HighlightsClipped != ToneChannels.None;

    /// <summary>Whether the shadows clip, and in which channels.</summary>
    public string ShadowsStatus => Status(Histogram?.ShadowsClipped ?? ToneChannels.None,
                                          "Images.Edit.Histogram.ShadowsClear", "Images.Edit.Histogram.ShadowsClip");

    /// <summary>Whether the highlights clip, and in which channels.</summary>
    public string HighlightsStatus => Status(Histogram?.HighlightsClipped ?? ToneChannels.None,
                                             "Images.Edit.Histogram.HighlightsClear", "Images.Edit.Histogram.HighlightsClip");

    private static string Status(ToneChannels clipped, string clearKey, string clipsKey)
        => clipped == ToneChannels.None ? Str.Get(clearKey) : Str.Format(clipsKey, ChannelNames(clipped));

    /// <summary>The channels named, red first, as a list.</summary>
    public static string ChannelNames(ToneChannels channels)
    {
        var names = new List<string>(3);
        if (channels.HasFlag(ToneChannels.Red)) names.Add(Str.Get("Images.Edit.Histogram.Red"));
        if (channels.HasFlag(ToneChannels.Green)) names.Add(Str.Get("Images.Edit.Histogram.Green"));
        if (channels.HasFlag(ToneChannels.Blue)) names.Add(Str.Get("Images.Edit.Histogram.Blue"));
        return string.Join(Str.Get("Images.Edit.Histogram.ListSeparator"), names);
    }

    /// <summary>A frame's tones, on the UI thread while what it marked is still its own. A frame drawn before the histogram was
    /// put away is let go.</summary>
    internal void Take(ToneReading reading)
    {
        if (!IsShown) return;
        Histogram = reading.Histogram;
        if (reading.Clipped is { } clipped && Masks.Write(clipped))
        {
            OnPropertyChanged(nameof(ShadowMask));
            OnPropertyChanged(nameof(HighlightMask));
        }
        var masked = reading.Clipped?.Ends ?? ClipEnds.None;
        if (masked == _masked) return;
        _masked = masked;
        OnPropertyChanged(nameof(ShowsShadows));
        OnPropertyChanged(nameof(ShowsHighlights));
    }
}
