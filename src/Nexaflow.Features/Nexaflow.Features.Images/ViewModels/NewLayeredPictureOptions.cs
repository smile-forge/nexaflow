using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Views;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// What "New → Layered picture" makes: a blank canvas of the size and background set up in the picker
/// (<see cref="CanvasSetup"/>), written as an <c>.ora</c> into the folder in the background and opened for editing once it is.
/// </summary>
public sealed class NewLayeredPictureOptions : ObservableObject, IFileCreateOptions
{
    private readonly IShellServices _shell;
    private readonly ImageEditingServices _editing;
    private readonly string _folder;

    public NewLayeredPictureOptions(IShellServices shell, ImageEditingServices editing, string folder)
    {
        _shell   = shell;
        _editing = editing;
        _folder  = folder;
        Setup.PropertyChanged += SetupChanged;
    }

    /// <summary>The canvas's size and background.</summary>
    public CanvasSetup Setup { get; } = new();

    /// <summary>Made when the picker first shows it.</summary>
    public FrameworkElement View => _view ??= new CanvasSetupView { DataContext = Setup };

    private FrameworkElement? _view;

    public bool CanCreate => Setup.IsValid;

    private void SetupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CanvasSetup.IsValid)) OnPropertyChanged(nameof(CanCreate));
    }

    public string? Create(string fileName)
    {
        if (Setup.Make(Str.Format("Images.Layers.PixelName", 1)) is not { } picture) return null;
        var path = Path.Combine(_folder, fileName);
        _ = MakeAsync(picture, path);
        return path;
    }

    // Written in the background, then opened for editing; a failure is said rather than thrown, as nothing awaits it.
    private async Task MakeAsync(LayeredPicture picture, string path)
    {
        try
        {
            await _editing.WriteLayeredAsync(picture, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _shell.ShowError(Str.Format("Images.Canvas.FailedFormat", Path.GetFileName(path), ex.Message));
            return;
        }
        _shell.OpenTab(ImageTabRegistration.StaticPageKind, new Dictionary<string, string> { ["paths"] = path, ["view"] = "edit" });
    }

    public void Dispose() => Setup.PropertyChanged -= SetupChanged;
}
