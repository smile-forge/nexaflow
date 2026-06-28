using System;

namespace Nexaflow.Features.Audio.Models;

/// <summary>
/// The editable metadata for one track, read from / written to the file via TagLib. A plain mutable
/// carrier — the view-model binds editor fields to a copy and writes it back on save.
/// </summary>
public sealed class TrackTags
{
    public string Title   { get; set; } = string.Empty;
    public string Artist  { get; set; } = string.Empty;
    public string Album   { get; set; } = string.Empty;
    public string Genre   { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public uint   Year    { get; set; }
    public uint   Track   { get; set; }

    /// <summary>Track length from the file's audio properties (display only; not written).</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Embedded cover-art bytes, or null. Display only here; the editor replaces art separately.</summary>
    public byte[]? AlbumArt { get; set; }

    /// <summary>Embedded unsynced lyrics, used as a fallback when no sibling <c>.lrc</c> exists.</summary>
    public string? Lyrics { get; set; }

    public TrackTags Clone() => new()
    {
        Title = Title, Artist = Artist, Album = Album, Genre = Genre, Comment = Comment,
        Year = Year, Track = Track, Duration = Duration, AlbumArt = AlbumArt, Lyrics = Lyrics,
    };
}
