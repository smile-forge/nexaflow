using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.Reading;
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
public sealed class PdfImageExtractionTask(IReadOnlyList<PdfImageSource> sources, string targetRoot) : IBackgroundTask
{
    /// <summary>PDFs as they are on disk, each going by its file name.</summary>
    public PdfImageExtractionTask(IReadOnlyList<string> pdfPaths, string targetRoot)
        : this(pdfPaths.Select(PdfImageSource.OfFile).ToList(), targetRoot)
    {
    }

    public string Description => sources.Count == 1
        ? Str.Format("Pdf.Extract.TaskOneFormat", sources[0].Name)
        : Str.Format("Pdf.Extract.TaskManyFormat", sources.Count);

    /// <summary>Per-document outcome, populated as the run proceeds and read by the completion callback.</summary>
    public List<PdfImageExtractionResult> Results { get; } = [];

    public Task RunAsync(CancellationToken ct)
    {
        // PdfPig is synchronous throughout, so there is nothing to await — the shell already called this on a
        // background thread, and pretending otherwise would only add a hop.
        try
        {
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();
                Results.Add(ExtractOne(source, ct));
                if (source.Draft) DeleteDraft(source);
            }
        }
        finally
        {
            // Cancelled or failed part-way: the drafts not yet read go too.
            foreach (var source in sources)
                if (source.Draft) DeleteDraft(source);
        }

        return Task.CompletedTask;
    }

    private PdfImageExtractionResult ExtractOne(PdfImageSource source, CancellationToken ct)
    {
        using var scope = PdfDocumentScope.TryOpen(source.Path, ct);
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
                destination ??= CreateDestination(source.Name);
                Write(Path.Combine(destination, image.SuggestedFileName), image.Bytes.Span, ct);
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

    // Written to a sidecar name and then moved, so cancelling or failing part-way through a large image
    // never leaves a truncated file that looks like a successful extraction.
    private static void Write(string path, ReadOnlySpan<byte> bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var partial = path + ".part";
        try
        {
            File.WriteAllBytes(partial, bytes);
            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
            throw;
        }
    }

    /// <summary>
    /// A subfolder named after the document, so extracting from several PDFs — or into a folder that already
    /// holds an earlier run — never interleaves or overwrites two sets of <c>p001-01.png</c>.
    /// </summary>
    private string CreateDestination(string name)
    {
        var baseName = Sanitize(Path.GetFileNameWithoutExtension(name));
        var dest     = Path.Combine(targetRoot, baseName);
        for (var n = 1; Directory.Exists(dest) || File.Exists(dest); n++)
            dest = Path.Combine(targetRoot, $"{baseName} ({n})");

        Directory.CreateDirectory(dest);
        return dest;
    }

    /// <summary>A draft goes with the folder of its own it was written into.</summary>
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
