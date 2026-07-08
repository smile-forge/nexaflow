using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Font.ViewModels;

/// <summary>
/// Shell-modal overlay for choosing a font to add to the compare list: a searchable, virtualized list
/// of the machine's installed families (each previewing itself), plus a "Load from file…" route.
/// Rendered by the shell's overlay host via the <c>DataTemplate</c> in <c>Theming/FontTheme.xaml</c>.
/// </summary>
public sealed partial class FontPickerViewModel : ObservableObject, IShellOverlay
{
    private readonly IShellServices _shell;
    private readonly Action<FontItemViewModel> _onPickInstalled;
    private readonly Action<string> _onPickFile;

    public bool DismissOnBackdrop => true;

    public ObservableCollection<FontItemViewModel> AllFonts { get; } = [];
    public ICollectionView FontsView { get; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading = true;

    public FontPickerViewModel(IShellServices shell,
                               Action<FontItemViewModel> onPickInstalled,
                               Action<string> onPickFile)
    {
        _shell = shell;
        _onPickInstalled = onPickInstalled;
        _onPickFile = onPickFile;

        FontsView = CollectionViewSource.GetDefaultView(AllFonts);
        FontsView.Filter = o =>
            o is FontItemViewModel f &&
            (string.IsNullOrWhiteSpace(SearchText) ||
             f.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value) => FontsView.Refresh();

    private async Task LoadAsync()
    {
        // SystemFontFamilies are frozen Freezables — safe to enumerate + wrap off the UI thread.
        var items = await Task.Run(() => Fonts.SystemFontFamilies
            .Select(FontItemViewModel.Installed)
            .OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList());

        await _shell.RunOnUiAsync(() =>
        {
            foreach (var item in items) AllFonts.Add(item);
            IsLoading = false;
        });
    }

    [RelayCommand]
    private void Pick(FontItemViewModel? item)
    {
        if (item is null) return;
        _onPickInstalled(item);
        _shell.CloseOverlay();
    }

    [RelayCommand]
    private async Task LoadFromFileAsync()
    {
        var path = await _shell.PickOpenFileAsync(FontFileTypes.PickerExtensions);
        if (string.IsNullOrEmpty(path)) return;
        _onPickFile(path);
        _shell.CloseOverlay();
    }

    [RelayCommand]
    private void Cancel() => _shell.CloseOverlay();
}
