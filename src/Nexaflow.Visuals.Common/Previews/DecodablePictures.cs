namespace Nexaflow.Visuals.Common.Previews;

/// <summary>
/// The picture files Windows' imaging decodes, by extension: what a feature offers to open, or accepts dropped, as a
/// picture.
/// </summary>
public static class DecodablePictures
{
    /// <summary>The extensions, lower case with their dot.</summary>
    public static IReadOnlyList<string> Extensions { get; } =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp"];
}
