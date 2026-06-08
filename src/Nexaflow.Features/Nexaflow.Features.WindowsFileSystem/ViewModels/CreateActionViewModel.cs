using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using System;
using System.Windows.Media;

namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

/// <summary>
/// Wraps an <see cref="IFileCreateAction"/> for the new-file picker overlay: an icon
/// (glyph or shell image), a label, a radio-style selected state, and a select command.
/// </summary>
public partial class CreateActionViewModel : ObservableObject
{
    private readonly Action<CreateActionViewModel> _onSelect;

    public CreateActionViewModel(IFileCreateAction action, Action<CreateActionViewModel> onSelect)
    {
        Action    = action;
        _onSelect = onSelect;
    }

    public IFileCreateAction Action { get; }

    public string       Icon          => Action.Icon;
    public ImageSource? IconImage     => Action.IconImage;
    public string       DisplayName   => Action.DisplayName;
    public string       FileExtension => Action.FileExtension;

    /// <summary>True when this is the currently chosen type in the picker.</summary>
    [ObservableProperty] private bool _isSelected;

    [RelayCommand]
    private void Select() => _onSelect(this);
}
