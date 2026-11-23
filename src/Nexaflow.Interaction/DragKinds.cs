namespace Nexaflow.Interaction;

/// <summary>
/// The kinds of thing a drag can carry from any application, whatever formats it came in. A drag of the app's own adds
/// kinds by naming them — a kind is any string, and a payload holds one under the name it was dragged as.
/// </summary>
public static class DragKinds
{
    /// <summary>Files and folders by path: a <c>string[]</c>.</summary>
    public const string Files = "Files";

    /// <summary>
    /// The <see cref="Files"/> were cut rather than copied, so a paste moves them: <c>true</c>, held only when they were.
    /// It travels with <see cref="Files"/>, so it is not a kind of its own in <see cref="Shared"/>.
    /// </summary>
    public const string FilesCut = "FilesCut";

    /// <summary>Files that are only their content — a mail's attachment, a picture out of a browser, an entry in a zip
    /// Explorer shows: an <c>IReadOnlyList&lt;<see cref="CarriedFile"/>&gt;</c>.</summary>
    public const string FileContents = "FileContents";

    /// <summary>One web address: an http(s) address as a <c>string</c>.</summary>
    public const string Link = "Link";

    /// <summary>Plain words: a <c>string</c>.</summary>
    public const string Text = "Text";

    /// <summary>Marked-up words as the clipboard packages HTML: a <c>string</c>.</summary>
    public const string Html = "Html";

    /// <summary>A picture with no file of its own: PNG bytes.</summary>
    public const string Image = "Image";

    /// <summary>Every kind above: what another application can drag in. A drag with none of them is the app's own gesture.</summary>
    public static IReadOnlyList<string> Shared { get; } = [Files, FileContents, Link, Text, Html, Image];
}
