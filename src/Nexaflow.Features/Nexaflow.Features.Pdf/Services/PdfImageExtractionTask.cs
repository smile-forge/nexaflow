using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Pdf.Services;

/// <summary>What one PDF gave up.</summary>
/// <param name="Name">The document, by the name it goes by.</param>
/// <param name="Destination">Where its images were written, or null when it produced none.</param>
/// <param name="Extracted">Files written.</param>
/// <param name="Duplicates">Images skipped as byte-identical to one already written.</param>
/// <param name="Undecodable">Images no codec here could turn into a file.</param>
/// <param name="Error">Why nothing came out, when that's the reason.</param>
public sealed record PdfImageExtractionResult(
    string Name, string? Destination, int Extracted, int Duplicates, int Undecodable, string? Error);

/// <summary>
/// Writes the embedded images of one or more PDFs into a target folder, one subfolder per document.
/// <para>
/// Shell-owned rather than tab-owned on purpose: this is a job the user asked for, so it keeps running (and
/// still reports) after the tab that launched it is closed. It reports through the activity ticker and runs off
/// the UI thread. A draft it was handed — a copy written for it alone — it deletes once read, whatever the outcome.
/// </para>
/// </summary>
public sealed class PdfImageExtractionTask(IReadOnlyList<PdfImageSource> sources, string targetRoot, IVirtualFileSystem vfs) : IBackgroundTask
{
    /// <summary>PDFs as they are where they are, each going by its file name.</summary>
    public PdfImageExtractionTask(IReadOnlyList<string> pdfPaths, string targetRoot, IVirtualFileSystem vfs)
        : this(pdfPaths.Select(PdfImageSource.OfFile).ToList(), targetRoot, vfs)
    {
    }

    public string Description => sources.Count == 1
        ? Str.Format("Pdf.Extract.TaskOneFormat", sources[0].Name)
        : Str.Format("Pdf.Extract.TaskManyFormat", sources.Count);

    /// <summary>Per-document outcome, populated as the run proceeds and read by the completion callback.</summary>
    public List<PdfImageExtractionResult> Results { get; } = [];

    public async Task RunAsync(CancellationToken ct)
    {
        // The documents are read, and the images written, through the file system, so either may be in an archive or
        // on a location. PdfPig itself is synchronous, and parses on the background thread the shell called this on.
        try
        {
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();
                Results.Add(await ExtractOneAsync(source, ct));
                if (source.Draft) DeleteDraft(source);
            }
        }
        finally
        {
            // Cancelled or failed part-way: the drafts not yet read go too.
            foreach (var source in sources)
                if (source.Draft) DeleteDraft(source);
        }
    }

    private async Task<PdfImageExtractionResult> ExtractOneAsync(PdfImageSource source, CancellationToken ct)
    {
        using var scope = await PdfDocumentScope.TryOpenAsync(vfs, source.Path, ct);
        if (scope is null)
            return new PdfImageExtractionResult(source.Name, null, 0, 0, 0, Str.Get("Pdf.Extract.CouldNotBeRead"));

        // The folder is created only once the first image is in hand, so a PDF with no images leaves no
        // empty directory behind to explain.
        string? destination = null;
        var tally = default(PdfImageTally);

        try
        {
            foreach (var image in PdfImageReader.Read(scope.Document, t => tally = t, ct))
            {
                destination ??= await CreateDestinationAsync(source.Name, ct);
                await WriteAsync(Path.Combine(destination, image.SuggestedFileName), image.Bytes, ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new PdfImageExtractionResult(
                source.Name, destination, tally.Extracted, tally.Duplicates, tally.Undecodable, ex.Message);
        }

        return new PdfImageExtractionResult(
            source.Name, destination, tally.Extracted, tally.Duplicates, tally.Undecodable, Error: null);
    }

    // Written whole and then put in place, so cancelling or failing part-way through a large image never leaves a
    // truncated file that looks like a successful extraction.
    private async Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await using var write = await vfs.BeginWriteAsync(path, WriteMode.Replace, ct: ct);
        await write.Content.WriteAsync(bytes, ct);
        await write.CommitAsync(ct);
    }

    /// <summary>
    /// A subfolder named after the document, so extracting from several PDFs — or into a folder that already
    /// holds an earlier run — never interleaves or overwrites two sets of <c>p001-01.png</c>.
    /// </summary>
    private async Task<string> CreateDestinationAsync(string name, CancellationToken ct)
    {
        var baseName = Sanitize(Path.GetFileNameWithoutExtension(name));
        var dest     = Path.Combine(targetRoot, baseName);
        for (var n = 1; await vfs.ExistsAsync(dest, ct); n++)
            dest = Path.Combine(targetRoot, $"{baseName} ({n})");

        await vfs.CreateDirectoryAsync(dest, ct);
        return dest;
    }

    /// <summary>A draft goes with the folder of its own it was written into.</summary>
    [LocalDiskOnly("a draft written for the run alone, in a folder of its own under the temp folder")]
    private static void DeleteDraft(PdfImageSource draft)
    {
        try
        {
            if (Path.GetDirectoryName(draft.Path) is { } folder && Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // A PDF inside an archive can carry a virtual path segment that isn't a legal directory name.
    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim();
        return name.Length == 0 ? "pdf" : name;
    }
}
