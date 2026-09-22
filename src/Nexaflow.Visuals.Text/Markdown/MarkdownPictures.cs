using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Where the picture an <c>![](…)</c> names comes from.
///
/// <para>
/// <strong>The host is asked first, and the folder is only what is left.</strong> A document does not always
/// live on disk — the help pane reads its showcases, pictures included, out of a language pack, and a viewer
/// showing a file inside an archive has the bytes and no path at all — so a host that can answer is the right
/// answer, and a file beside the document is the fallback rather than the rule.
/// </para>
/// <para>
/// One chain, in one place, because both surfaces ask the same question and a document whose pictures
/// appeared in one view and not the other would be a document nobody could explain.
/// </para>
/// </summary>
public static class MarkdownPictures
{
    /// <summary>
    /// What a name resolves to for this showing of a document: <paramref name="asked"/> if it answers,
    /// otherwise a file under <paramref name="from"/>.
    /// </summary>
    public static Func<string, ImageSource?> Found(Func<string, ImageSource?>? asked, string? from) =>
        named =>
        {
            // A host that threw looking for a picture has answered nothing, not "there is none" — losing the
            // document over it would be the renderer taking the host's bug personally — so the folder is still
            // asked, and the alt text is what is left when that finds nothing either.
            if (asked is not null)
            {
                try
                {
                    if (asked(named) is { } answered) return answered;
                }
                catch
                {
                    // Fall through to the file.
                }
            }

            return Loaded(Under(named, from));
        };

    /// <summary>
    /// Which file a name means, or null where it means none: an absolute <c>file:</c> URL, a rooted path, or
    /// a path read against the document's own folder. Anything else — <c>http</c>, <c>data</c> — is somewhere
    /// this does not go.
    /// </summary>
    public static string? Under(string? named, string? from)
    {
        if (string.IsNullOrWhiteSpace(named)) return null;

        if (Uri.TryCreate(named, UriKind.Absolute, out var whole))
            return whole.IsFile && File.Exists(whole.LocalPath) ? whole.LocalPath : null;

        var where = named;

        if (!Path.IsPathRooted(where) && !string.IsNullOrEmpty(from))
            where = Path.Combine(from, Uri.UnescapeDataString(named));

        return File.Exists(where) ? Path.GetFullPath(where) : null;
    }

    /// <summary>
    /// A file, read whole and frozen — read whole so the file is not held open behind the document, and
    /// frozen so any thread can draw it and the same picture can be shared by every view of the page.
    /// </summary>
    public static ImageSource? Loaded(string? path)
    {
        if (path is null) return null;

        try
        {
            var read = new BitmapImage();

            read.BeginInit();
            read.CacheOption = BitmapCacheOption.OnLoad;
            read.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            read.UriSource = new Uri(path, UriKind.Absolute);
            read.EndInit();
            read.Freeze();

            return read;
        }
        catch
        {
            // Not a picture, or not one this machine can read. The words written instead of it are the answer.
            return null;
        }
    }
}
