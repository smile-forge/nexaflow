using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>A colour kept on the palette, as the swatch it names or the colour it holds.</summary>
public sealed class PaletteColour(ColorSpec colour)
{
    public ColorSpec Colour { get; } = colour;

    public Brush? Brush { get; } = colour.ToBrush();

    /// <summary>What it is called: its swatch's hue, or its hex.</summary>
    public string Name => Colour.Kind == ColorSpecKind.Swatch ? SwatchPalette.HueName(Colour.SwatchKey!) : Colour.ToString();

    public string Tip => Str.Format("Images.Colour.SwatchFormat", Name);
}

/// <summary>
/// The one colour Markup, Paint and Haze share — the editor's ink — and the palette kept beside it, down the canvas's left edge:
/// a colour on the palette is chosen with a click and taken off with a right-click, and the current colour is added with +. The
/// palette is kept in the Images settings, so every Artist tab shows the same one.
/// </summary>
public sealed partial class PictureColours : ObservableObject
{
    private readonly ImageEditorViewModel _editor;
    private readonly SceneDepthTools _depth;
    private readonly ImagesConfig _config;
    private readonly IShellServices? _shell;

    internal PictureColours(ImageEditorViewModel editor, SceneDepthTools depth, ImagesConfig config, IShellServices? shell)
    {
        _editor = editor;
        _depth  = depth;
        _config = config;
        _shell  = shell;
        Palette = new(config.Palette.Select(text => ColorSpec.TryParse(text, out var spec) ? spec : ColorSpec.Default)
                                    .Where(spec => !spec.IsDefault)
                                    .Select(spec => new PaletteColour(spec)));
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ImageEditorViewModel.InkColour) or "")
            {
                OnPropertyChanged(nameof(Current));
                OnPropertyChanged(nameof(CurrentBrush));
            }
            if (e.PropertyName is nameof(ImageEditorViewModel.Tool) or "") OnPropertyChanged(nameof(IsShown));
        };
        depth.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsShown));
    }

    public ObservableCollection<PaletteColour> Palette { get; }

    /// <summary>The colour drawn, painted and hazed with.</summary>
    public ColorSpec Current
    {
        get => _editor.InkColour;
        set => _editor.InkColour = value;
    }

    public Brush? CurrentBrush => Current.ToBrush();

    /// <summary>Shown where the open tool uses the colour: Markup, Paint, and Adjust while the haze takes it.</summary>
    public bool IsShown => _editor.IsMarkupTool || _editor.IsPaintTool || _editor.IsAdjustTool && _depth.Haze && _depth.HazeTakesInk;

    /// <summary>The picker is open over the canvas, beside the current colour.</summary>
    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>The current colour put first on the palette — moved there if it is on it already.</summary>
    [RelayCommand]
    private void Add()
    {
        var current = Current;
        if (Palette.FirstOrDefault(p => p.Colour == current) is { } kept) Palette.Remove(kept);
        Palette.Insert(0, new PaletteColour(current));
        Keep();
    }

    [RelayCommand]
    private void Choose(PaletteColour colour) => Current = colour.Colour;

    [RelayCommand]
    private void Remove(PaletteColour colour)
    {
        if (Palette.Remove(colour)) Keep();
    }

    private void Keep()
    {
        _config.Palette = [.. Palette.Select(p => p.Colour.ToString())];
        _shell?.SaveFeatureConfig(_config);
    }
}
