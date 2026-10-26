using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Metadata;
using Nexaflow.IO.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Images.Details;

/// <summary>What a picture's file says about it: the file itself, the camera's facts, and the colour profile it carries.</summary>
public sealed record PhotoDetails(string FileName, ImageContainer Container, long FileSize, DateTime? Modified, CameraFacts Camera, string? ColourProfile);

/// <summary>
/// Reads <see cref="PhotoDetails"/> from a picture wherever the runtime's file system finds it — on this disk, inside an
/// archive, at a location — through the same metadata readers a save carries the picture's metadata over with, so what
/// the panel shows is what a save keeps.
/// </summary>
public sealed class PhotoDetailsReader(MetadataTransfer metadata, IVirtualFileSystem vfs)
{
    public async Task<PhotoDetails> ReadAsync(string path, CancellationToken ct = default)
    {
        var file = await vfs.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var entry = await vfs.GetEntryAsync(path, ct).ConfigureAwait(false);
        var container = ImageContainers.Sniff(file);
        var blocks = metadata.Read(file, container);

        return new PhotoDetails(Path.GetFileName(path), container, entry?.Size ?? file.LongLength, entry?.Modified,
                                ExifFacts.Read(blocks.Exif), IccDescription.Read(blocks.Icc));
    }
}
