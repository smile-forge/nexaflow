using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.Editing.Wpf;
using Nexaflow.Features.Images.Services;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>One row of the layers panel: a layer, how deep in groups it lies, and its thumbnails.</summary>
public sealed partial class LayerItem(Guid id) : ObservableObject
{
    public Guid Id { get; } = id;

    /// <summary>The layer as last shown — what its thumbnail was drawn from.</summary>
    internal Layer? Shown { get; set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private LayerKind _kind;
    [ObservableProperty] private int _depth;
    [ObservableProperty] private bool _visible;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private bool _alphaLocked;
    [ObservableProperty] private bool _clipsToBelow;
    [ObservableProperty] private bool _hasMask;
    [ObservableProperty] private bool _maskEnabled;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isMaskActive;
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private BitmapSource? _thumbnail;
    [ObservableProperty] private BitmapSource? _maskThumbnail;

    /// <summary>How far in from the panel's edge the row is drawn, by its depth in groups.</summary>
    public double Indent => Depth * 14;

    partial void OnDepthChanged(int value) => OnPropertyChanged(nameof(Indent));
}

/// <summary>What a layer is, as the layers panel shows it.</summary>
public enum LayerKind
{
    Picture,
    Pixels,
    Markup,
    Group,
    Undrawable,
}

/// <summary>Where a layer dragged in the layers panel is dropped, beside the layer it is dropped on.</summary>
public enum LayerDrop
{
    Above,
    Below,
    Into,
}

/// <summary>Where the picture stays as its canvas grows or shrinks: a corner, an edge's middle, or the middle.</summary>
public enum CanvasAnchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Centre,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>A blend the active layer can take, with what it is called.</summary>
public sealed record LayerBlendChoice(LayerBlend Blend, string Label);
