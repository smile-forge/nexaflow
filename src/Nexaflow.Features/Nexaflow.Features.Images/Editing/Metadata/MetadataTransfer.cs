using Nexaflow.Features.Images.Editing.Codecs;
using System.IO;

namespace Nexaflow.Features.Images.Editing.Metadata;

/// <summary>Lifts a container's metadata out into <see cref="MetadataBlocks"/>.</summary>
public interface IMetadataReader
{
    bool Reads(ImageContainer container);
    MetadataBlocks Read(byte[] file);
}

/// <summary>
/// Puts <see cref="MetadataBlocks"/> into a freshly encoded file of one container — the encoders write pixels only, and
/// this is where everything else goes back in.
/// </summary>
public interface IMetadataWriter
{
    bool Writes(ImageContainer container);
    byte[] Write(byte[] encoded, MetadataBlocks blocks);
}

/// <summary>The readers and writers, by container. A container with no reader carries no metadata worth keeping.</summary>
public sealed class MetadataTransfer(IReadOnlyList<IMetadataReader> readers, IReadOnlyList<IMetadataWriter> writers)
{
    public MetadataBlocks Read(byte[] file, ImageContainer container)
    {
        var reader = readers.FirstOrDefault(r => r.Reads(container));
        if (reader is null) return MetadataBlocks.None;
        try { return reader.Read(file); }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            // Damaged metadata is metadata that cannot be kept; the picture itself is still saved.
            return MetadataBlocks.None;
        }
    }

    /// <summary>True when a file of <paramref name="container"/> can carry metadata on its way out.</summary>
    public bool CanWrite(ImageContainer container) => writers.Any(w => w.Writes(container));

    public byte[] Write(byte[] encoded, ImageContainer container, MetadataBlocks blocks) =>
        writers.FirstOrDefault(w => w.Writes(container)) is { } writer && !blocks.IsEmpty
            ? writer.Write(encoded, blocks)
            : encoded;
}
