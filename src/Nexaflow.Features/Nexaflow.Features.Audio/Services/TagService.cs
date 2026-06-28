using System;
using System.Linq;
using Nexaflow.Features.Audio.Models;

namespace Nexaflow.Features.Audio.Services;

/// <summary>
/// Reads and writes track metadata through TagLib# — a single unified surface over ID3v2 (mp3),
/// Vorbis comments (flac/ogg), and MP4 atoms (m4a). The file must not be open for playback when
/// writing (TagLib needs write access), so the view-model releases the playback engine before saving.
/// </summary>
public static class TagService
{
    /// <summary>Reads all editable tags + duration + cover art. Never throws — returns blanks on failure.</summary>
    public static TrackTags Read(string path)
    {
        var t = new TrackTags();
        try
        {
            using var f = TagLib.File.Create(path);
            var tag = f.Tag;

            t.Title   = tag.Title ?? string.Empty;
            t.Artist  = tag.FirstPerformer ?? tag.FirstAlbumArtist ?? string.Empty;
            t.Album   = tag.Album ?? string.Empty;
            t.Genre   = tag.FirstGenre ?? string.Empty;
            t.Comment = tag.Comment ?? string.Empty;
            t.Year    = tag.Year;
            t.Track   = tag.Track;
            t.Lyrics  = string.IsNullOrWhiteSpace(tag.Lyrics) ? null : tag.Lyrics;
            t.Duration = f.Properties?.Duration ?? TimeSpan.Zero;

            if (tag.Pictures?.FirstOrDefault() is { } pic)
                t.AlbumArt = pic.Data?.Data;
        }
        catch
        {
            // leave defaults
        }
        return t;
    }

    /// <summary>
    /// Writes <paramref name="tags"/> back to <paramref name="path"/>. When <paramref name="replaceArt"/>
    /// is true the embedded cover art is set to <paramref name="newArt"/> (or cleared if null/empty);
    /// otherwise existing art is left untouched. Returns false on any failure.
    /// </summary>
    public static bool Save(string path, TrackTags tags, byte[]? newArt, bool replaceArt)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            var tag = f.Tag;

            tag.Title      = NullIfBlank(tags.Title);
            tag.Performers = AsArray(tags.Artist);
            tag.Album      = NullIfBlank(tags.Album);
            tag.Genres     = AsArray(tags.Genre);
            tag.Comment    = tags.Comment ?? string.Empty;
            tag.Year       = tags.Year;
            tag.Track      = tags.Track;
            tag.Lyrics     = NullIfBlank(tags.Lyrics ?? string.Empty);

            if (replaceArt)
            {
                tag.Pictures = newArt is { Length: > 0 }
                    ? [new TagLib.Picture(new TagLib.ByteVector(newArt)) { Type = TagLib.PictureType.FrontCover }]
                    : [];
            }

            f.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string[] AsArray(string? s) =>
        string.IsNullOrWhiteSpace(s) ? [] : [s.Trim()];
}
