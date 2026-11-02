using System.Collections.Immutable;
using Nexaflow.IO.Common.OpenRaster;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// What an OpenRaster package held for the picture as a whole that Nexaflow does not understand: its <c>image</c> element's
/// attributes and elements, and the files no layer names, by their path inside it — written back as they were.
/// </summary>
public sealed record OpenRasterPictureKept(OpenRasterKept Image, ImmutableDictionary<string, byte[]> Entries)
{
    public bool Equals(OpenRasterPictureKept? other)
        => other is not null && Image.Equals(other.Image) && Entries.Count == other.Entries.Count
           && Entries.All(e => other.Entries.TryGetValue(e.Key, out var bytes) && bytes.AsSpan().SequenceEqual(e.Value));

    public override int GetHashCode() => HashCode.Combine(Image, Entries.Count);
}
