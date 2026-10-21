using System.IO;

namespace Nexaflow.Features.Pdf.Services;

/// <summary>
/// A document whose images are extracted: the file it is read from, and the name it goes by — what the folder its images
/// are written to is called after, and what the reader is told about. A <see cref="Draft"/> is a copy written for the
/// extraction alone, in a folder of its own, deleted with that folder once read.
/// </summary>
public sealed record PdfImageSource(string Path, string Name, bool Draft = false)
{
    /// <summary>A PDF as it is on disk, going by its file name.</summary>
    public static PdfImageSource OfFile(string path) => new(path, System.IO.Path.GetFileName(path));
}
