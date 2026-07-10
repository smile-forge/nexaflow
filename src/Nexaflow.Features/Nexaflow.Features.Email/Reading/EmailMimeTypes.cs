namespace Nexaflow.Features.Email.Reading;

/// <summary>Maps a MIME content type to a file extension for naming a part that arrived without a filename.
/// Backed by MimeKit's table so both format readers synthesise the same extension.</summary>
internal static class EmailMimeTypes
{
    public static string Extension(string? mimeType)
        => !string.IsNullOrWhiteSpace(mimeType) && MimeKit.MimeTypes.TryGetExtension(mimeType, out var ext)
            ? ext
            : ".bin";
}
