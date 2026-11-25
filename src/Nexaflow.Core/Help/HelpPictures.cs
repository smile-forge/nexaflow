using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Core.Localization;

namespace Nexaflow.Core.Help;

/// <summary>
/// The pictures help pages show, drawn by WPF: each decoded fully into memory and frozen — every window's UI thread may draw it —
/// and kept, so a page shown again costs nothing. Which picture a page means, and its bytes, are <see cref="HelpLibrary"/>'s; the
/// lot is let go when the language changes, as the pages are.
/// </summary>
internal sealed class HelpPictures
{
    private readonly HelpLibrary _library;
    private readonly ConcurrentDictionary<string, ImageSource?> _pictures = new(StringComparer.Ordinal);

    /// <param name="library">The help pages, which say which picture a page means and hold its bytes.</param>
    /// <param name="language">The UI language: a change of it lets every picture go.</param>
    public HelpPictures(HelpLibrary library, LanguageManager language)
    {
        _library = library;
        language.Changed += _pictures.Clear;
    }

    /// <summary>The picture <paramref name="page"/> shows as <paramref name="src"/>, or null when there is none or it is not one
    /// WPF can decode — the alt text shows instead.</summary>
    public ImageSource? Resolve(HelpTopic page, string src)
        => _library.PictureName(page, src) is { } name ? _pictures.GetOrAdd(name, Decode) : null;

    private ImageSource? Decode(string name)
    {
        if (_library.ReadPicture(name) is not { } bytes) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption  = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes, writable: false);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
