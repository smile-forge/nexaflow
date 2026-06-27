using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Audio.Models;
using Nexaflow.Features.Audio.Services;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Audio.ViewModels;

/// <summary>
/// The editable ID3 / metadata panel for the current track. Holds the fields, tracks a dirty flag, and
/// delegates the actual write back to the parent via <see cref="_saveAsync"/> (the parent must release
/// the playback engine first so TagLib can open the file for writing).
/// </summary>
public partial class TagEditorViewModel : ObservableObject
{
    private readonly IShellServices _shell;
    private readonly Func<TrackTags, byte[]?, bool, Task<bool>> _saveAsync;

    private byte[]? _newArt;
    private bool _replaceArt;
    private bool _loading;

    public TagEditorViewModel(TrackTags tags, IShellServices shell,
                              Func<TrackTags, byte[]?, bool, Task<bool>> saveAsync)
    {
        _shell = shell;
        _saveAsync = saveAsync;
        Load(tags);
    }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private string _album = string.Empty;
    [ObservableProperty] private string _genre = string.Empty;
    [ObservableProperty] private string _comment = string.Empty;
    [ObservableProperty] private string _year = string.Empty;
    [ObservableProperty] private string _track = string.Empty;
    [ObservableProperty] private BitmapSource? _artPreview;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSaving;

    private void Load(TrackTags tags)
    {
        _loading = true;
        Title = tags.Title;
        Artist = tags.Artist;
        Album = tags.Album;
        Genre = tags.Genre;
        Comment = tags.Comment;
        Year = tags.Year > 0 ? tags.Year.ToString(CultureInfo.InvariantCulture) : string.Empty;
        Track = tags.Track > 0 ? tags.Track.ToString(CultureInfo.InvariantCulture) : string.Empty;
        ArtPreview = ImageHelpers.ToBitmap(tags.AlbumArt);
        _newArt = null;
        _replaceArt = false;
        IsDirty = false;
        _loading = false;
    }

    [RelayCommand]
    private async Task PickArtAsync()
    {
        var path = await _shell.PickOpenFileAsync([".jpg", ".jpeg", ".png", ".bmp"]);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            _newArt = await File.ReadAllBytesAsync(path);
            _replaceArt = true;
            ArtPreview = ImageHelpers.ToBitmap(_newArt);
            IsDirty = true;
        }
        catch
        {
            _shell.ShowError("Could not read that image.");
        }
    }

    [RelayCommand]
    private void RemoveArt()
    {
        _newArt = null;
        _replaceArt = true;
        ArtPreview = null;
        IsDirty = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        var tags = BuildTags();
        bool ok = await _saveAsync(tags, _newArt, _replaceArt);
        IsSaving = false;
        if (ok)
        {
            IsDirty = false;
            _replaceArt = false;
            _newArt = null;
        }
    }

    private TrackTags BuildTags() => new()
    {
        Title = Title?.Trim() ?? string.Empty,
        Artist = Artist?.Trim() ?? string.Empty,
        Album = Album?.Trim() ?? string.Empty,
        Genre = Genre?.Trim() ?? string.Empty,
        Comment = Comment ?? string.Empty,
        Year = ParseUint(Year),
        Track = ParseUint(Track),
    };

    private static uint ParseUint(string? s) =>
        uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    // ── Dirty tracking ──────────────────────────────────────────────────────
    partial void OnTitleChanged(string value) => Touch();
    partial void OnArtistChanged(string value) => Touch();
    partial void OnAlbumChanged(string value) => Touch();
    partial void OnGenreChanged(string value) => Touch();
    partial void OnCommentChanged(string value) => Touch();
    partial void OnYearChanged(string value) => Touch();
    partial void OnTrackChanged(string value) => Touch();

    private void Touch()
    {
        if (!_loading) IsDirty = true;
    }
}
