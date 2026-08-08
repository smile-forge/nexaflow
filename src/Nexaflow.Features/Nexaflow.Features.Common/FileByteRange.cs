namespace Nexaflow.Features.Common;

/// <summary>
/// A span of bytes inside a file — "show me <em>this part</em> of that file".
/// <para>
/// Dispatched through <c>IShellServices.HandleObject</c>, exactly like the clicked
/// link a post-it hands the shell: the feature that owns byte-level viewing claims it and the caller
/// stays ignorant of which one that is. A plain path already routes to the default opener, but a
/// path cannot carry an offset — hence a typed payload rather than a string with a fragment glued
/// on, which the file-system handler would strip on its way past.
/// </para>
/// </summary>
/// <param name="Path">The file to open.</param>
/// <param name="Offset">Byte offset to reveal and place the cursor at.</param>
/// <param name="Length">How many bytes to select. Zero just moves the cursor.</param>
/// <param name="Label">Optional description of what the range is, for notifications.</param>
public sealed record FileByteRange(string Path, long Offset, long Length = 0, string? Label = null)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Path) && Offset >= 0 && Length >= 0;

    public override string ToString()
        => Length > 0 ? $"{Path} @0x{Offset:X} +{Length}" : $"{Path} @0x{Offset:X}";
}
