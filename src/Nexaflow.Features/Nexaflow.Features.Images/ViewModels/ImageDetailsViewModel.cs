using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Images.Details;
using Nexaflow.Visuals.Common.Localization;
using SkiaSharp;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// The Details panel: what the file of the picture on screen says about it. Nothing is read while the panel is shut;
/// open, the picture's file is read once off the UI thread and kept until the file changes, so stepping back to a
/// picture already read shows it at once.
/// </summary>
public sealed partial class ImageDetailsViewModel : ObservableObject
{
    /// <summary>How many pictures' details are kept before they are let go, to be read again when wanted.</summary>
    private const int Kept = 256;

    private readonly PhotoDetailsReader _reader;
    private readonly Func<Action, Task> _runOnUi;
    private readonly Dictionary<string, (DateTime? Stamp, PhotoDetails Details)> _read = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _reading;
    private string? _path;
    private SKSizeI? _original, _saved;
    private PhotoDetails? _details;
    private bool _unreadable;

    internal ImageDetailsViewModel(PhotoDetailsReader reader, Func<Action, Task> runOnUi)
    {
        _reader  = reader;
        _runOnUi = runOnUi;
    }

    /// <summary>The panel is showing.</summary>
    [ObservableProperty] private bool _isOpen;

    [ObservableProperty] private IReadOnlyList<DetailsSection> _sections = [];

    /// <summary>A line under the facts: the file holds no camera details, or could not be read; null when there is nothing to say.</summary>
    [ObservableProperty] private string? _notice;

    [ObservableProperty] private bool _isReading;

    partial void OnIsOpenChanged(bool value)
    {
        if (value) Look();
        else Cancel();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>
    /// The picture on screen is <paramref name="path"/> (null: none), <paramref name="original"/> pixels upright once
    /// decoded, and <paramref name="saved"/> as its edits would save it. Another picture is read when the panel is
    /// open; the same one only has its sizes brought up to date.
    /// </summary>
    public void Follow(string? path, SKSizeI? original, SKSizeI? saved)
    {
        bool sized = original != _original || saved != _saved;
        _original = original;
        _saved = saved;

        if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
        {
            Cancel();
            _path = path;
            _details = null;
            _unreadable = false;
            if (IsOpen) Look();
        }
        else if (sized && IsOpen && _details is not null) Show();
    }

    /// <summary>Shows the picture's details: kept from an earlier read while its file is unchanged, otherwise read now.</summary>
    private void Look()
    {
        if (_path is not { } path) { Show(); return; }

        var stamp = Stamp(path);
        if (_read.TryGetValue(path, out var kept) && kept.Stamp == stamp)
        {
            _details = kept.Details;
            _unreadable = false;
        }
        else if (!_unreadable)
        {
            _details = null;
            if (_reading is null) _ = ReadAsync(path, stamp);
        }
        Show();
    }

    private async Task ReadAsync(string path, DateTime? stamp)
    {
        var cancel = _reading = new CancellationTokenSource();
        IsReading = true;
        PhotoDetails? details = null;
        try { details = await Task.Run(() => _reader.Read(path), cancel.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OperationCanceledException) { }

        await _runOnUi(() =>
        {
            if (!ReferenceEquals(_reading, cancel)) return;
            _reading = null;
            IsReading = false;
            if (details is not null)
            {
                if (_read.Count >= Kept) _read.Clear();
                _read[path] = (stamp, details);
            }
            _details = details;
            _unreadable = details is null;
            Show();
        }).ConfigureAwait(false);
    }

    private void Show()
    {
        if (_details is not { } details)
        {
            Sections = [];
            Notice = _unreadable ? Str.Get("Images.Details.Unreadable") : null;
            return;
        }
        Sections = PhotoDetailsSections.Build(details, _original, _saved);
        Notice = details.Camera.DescribesTheShot ? null : Str.Get("Images.Details.NoCamera");
    }

    private void Cancel()
    {
        _reading?.Cancel();
        _reading = null;
        IsReading = false;
    }

    /// <summary>When the file last changed — a save replaces it — or null for a picture inside an archive, which does not change under the viewer.</summary>
    private static DateTime? Stamp(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
