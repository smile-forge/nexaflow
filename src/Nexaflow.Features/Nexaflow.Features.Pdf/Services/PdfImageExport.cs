using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Pdf.Services;

/// <summary>
/// Drives "extract images": ask where, hand the work to the shell, then say what happened.
/// <para>
/// Separate from the action so the action can return at once: this awaits a folder pick, then hands the work to the
/// shell. The action fires this and returns immediately.
/// </para>
/// </summary>
internal static class PdfImageExport
{
    public static async Task RunAsync(IShellServices shell, IVirtualFileSystem vfs, IReadOnlyList<string> pdfPaths)
    {
        if (pdfPaths.Count == 0) return;

        // Default the picker to where the PDFs live, which is nearly always where the images should go.
        var target = await shell.PickFolderAsync(Path.GetDirectoryName(pdfPaths[0]));
        if (string.IsNullOrEmpty(target)) return;   // cancelled the picker — not an error, say nothing

        Queue(shell, vfs, pdfPaths.Select(PdfImageSource.OfFile).ToList(), target);
    }

    /// <summary>
    /// Hands the extraction of <paramref name="sources"/> into <paramref name="target"/> to the shell, which says what
    /// happened once it is done.
    /// </summary>
    public static void Queue(IShellServices shell, IVirtualFileSystem vfs, IReadOnlyList<PdfImageSource> sources, string target)
    {
        var task = new PdfImageExtractionTask(sources, target, vfs);

        // No token: the shell offers no way for the user to cancel a queued activity, and inventing a source
        // nothing can trip would only look like cancellation was supported. The task honours whatever token
        // it is handed, so it will obey one the day the ticker grows a cancel button.
        // The refresh happens in Report, not here: at this point the task has only been queued and there is
        // nothing on disk yet for a browser showing the target folder to pick up.
        shell.QueueBackgroundTask(task, ok => Report(shell, task, ok));
    }

    private static void Report(IShellServices shell, PdfImageExtractionTask task, bool ok)
    {
        var results = task.Results;

        if (!ok && results.Count == 0)
        {
            shell.ShowError(Str.Get("Pdf.Extract.Failed"));
            return;
        }

        var failed    = results.Where(r => r.Error is not null).ToList();
        var extracted = results.Sum(r => r.Extracted);

        // Every document failed: report the reason rather than "0 images", which reads like an empty PDF.
        if (extracted == 0 && failed.Count == results.Count)
        {
            shell.ShowError(results.Count == 1
                ? Str.Format("Pdf.Extract.FileErrorFormat", results[0].Name, results[0].Error)
                : Str.Format("Pdf.Extract.NoneReadableFormat", results.Count));
            return;
        }

        if (extracted == 0)
        {
            shell.ShowNotification(results.Count == 1
                ? Str.Format("Pdf.Extract.NoImagesOneFormat", results[0].Name)
                : Str.Format("Pdf.Extract.NoImagesManyFormat", results.Count));
            return;
        }

        var message = results.Count == 1
            ? Str.Format("Pdf.Extract.DoneOneFormat", Count(extracted), results[0].Name)
            : Str.Format("Pdf.Extract.DoneManyFormat", Count(extracted), results.Count - failed.Count);

        // Skipped images are worth naming: silence would make "40 images, 3 extracted" look like data loss.
        var duplicates = results.Sum(r => r.Duplicates);
        var undecodable = results.Sum(r => r.Undecodable);
        var notes = new List<string>();
        if (duplicates > 0)  notes.Add(Str.Format("Pdf.Extract.RepeatedFormat", duplicates));
        if (undecodable > 0) notes.Add(Str.Format("Pdf.Extract.UnsupportedFormat", undecodable));
        if (failed.Count > 0) notes.Add(Str.Format("Pdf.Extract.UnreadableFormat", failed.Count));

        shell.ShowNotification(notes.Count > 0
            ? Str.Format("Pdf.Extract.SkippedFormat", message, string.Join(", ", notes))
            : Str.Format("Pdf.Extract.DoneSentenceFormat", message));
        shell.RequestRefresh();
    }

    private static string Count(int n) => n == 1 ? Str.Get("Pdf.Extract.ImagesOne") : Str.Format("Pdf.Extract.ImagesManyFormat", n);
}
