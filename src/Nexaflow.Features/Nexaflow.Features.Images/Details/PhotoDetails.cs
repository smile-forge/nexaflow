using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Metadata;
using System.IO;

namespace Nexaflow.Features.Images.Details;

/// <summary>What a picture's file says about it: the file itself, the camera's facts, and the colour profile it carries.</summary>
public sealed record PhotoDetails(string FileName, ImageContainer Container, long FileSize, DateTime? Modified, CameraFacts Camera, string? ColourProfile);

/// <summary>
/// Reads <see cref="PhotoDetails"/> from a picture on disk or inside an archive, through the same metadata readers a
/// save carries the picture's metadata over with — so what the panel shows is what a save keeps.
/// </summary>
public sealed class PhotoDetailsReader(MetadataTransfer metadata)
{
    public PhotoDetails Read(string path)
    {
        var file = ImageEditingServices.ReadSource(path);
        var container = ImageContainers.Sniff(file);
        var blocks = metadata.Read(file, container);
        var onDisk = File.Exists(path) ? new FileInfo(path) : null;

        return new PhotoDetails(Path.GetFileName(path), container, onDisk?.Length ?? file.LongLength, onDisk?.LastWriteTime,
                                ExifFacts.Read(blocks.Exif), IccDescription.Read(blocks.Icc));
    }
}
