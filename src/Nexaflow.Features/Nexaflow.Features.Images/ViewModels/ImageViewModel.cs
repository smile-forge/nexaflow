using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>Represents a single dot in the image-navigator indicator strip.</summary>
public partial class ImageDotItem : ObservableObject
{
    public int   Index     { get; init; }
    [ObservableProperty] private bool _isCurrent;
}

public partial class ImageViewModel : ObservableObject
{
    // ── Image collection ──────────────────────────────────────────────────

    private readonly IReadOnlyList<string> _paths;

    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private BitmapSource? _currentImage;
    [ObservableProperty] private string _currentFileName = string.Empty;
    [ObservableProperty] private int _totalImages;

    // ── View state ────────────────────────────────────────────────────────

    /// <summary>When true the image is scaled to fit the viewer; when false it is shown at 100%.</summary>
    [ObservableProperty] private bool _fitToWindow = true;

    /// <summary>Rotation angle in degrees (multiples of 90).</summary>
    [ObservableProperty] private double _rotationAngle;

    // ── Dot indicator ─────────────────────────────────────────────────────

    public ObservableCollection<ImageDotItem> Dots { get; } = [];

    public bool HasMultiple => _paths.Count > 1;

    partial void OnCurrentIndexChanged(int value) => LoadImage(value);

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous()
    {
        if (CurrentIndex > 0) CurrentIndex--;
    }
    private bool CanGoPrevious() => CurrentIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (CurrentIndex < _paths.Count - 1) CurrentIndex++;
    }
    private bool CanGoNext() => CurrentIndex < _paths.Count - 1;

    [RelayCommand]
    private void RotateLeft() => RotationAngle = (RotationAngle - 90 + 360) % 360;

    [RelayCommand]
    private void RotateRight() => RotationAngle = (RotationAngle + 90) % 360;

    [RelayCommand]
    private void ToggleFit() => FitToWindow = !FitToWindow;

    [RelayCommand]
    private void GoToIndex(int index)
    {
        if (index >= 0 && index < _paths.Count)
            CurrentIndex = index;
    }

    // ── Construction ──────────────────────────────────────────────────────

    public ImageViewModel(IReadOnlyList<string> paths)
    {
        _paths      = paths;
        TotalImages = paths.Count;

        for (int i = 0; i < paths.Count; i++)
            Dots.Add(new ImageDotItem { Index = i, IsCurrent = i == 0 });

        LoadImage(0);
    }

    // ── Image loading ─────────────────────────────────────────────────────

    private void LoadImage(int index)
    {
        if (index < 0 || index >= _paths.Count) return;

        var path = _paths[index];
        CurrentFileName = Path.GetFileName(path);

        // Update dot states
        foreach (var dot in Dots)
            dot.IsCurrent = dot.Index == index;

        // Notify navigation commands
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();

        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource     = new Uri(path, UriKind.Absolute);
            bi.CacheOption   = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.EndInit();
            bi.Freeze();
            CurrentImage = bi;
        }
        catch
        {
            CurrentImage = null;
        }
    }
}
