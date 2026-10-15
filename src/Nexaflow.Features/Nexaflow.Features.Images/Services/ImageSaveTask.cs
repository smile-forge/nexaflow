using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Visuals.Common.Localization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Images.Services;

/// <summary>
/// Saves an edited photo off the UI thread: renders the recipe at full resolution, encodes it with the photo's
/// metadata, and writes it — over the original atomically when it replaces a file, so a failure part-way leaves the
/// original whole. A failure is kept in <see cref="Failure"/> for the caller to report, rather than thrown.
/// </summary>
/// <param name="restored">The photo's restoration, for a recipe that restores it.</param>
public sealed class ImageSaveTask(ImageEditingServices services, string sourcePath, ImageRecipe recipe,
                                  ImageContainer target, string destination, bool replace, string? restored = null) : IBackgroundTask
{
    public string Destination { get; } = destination;

    /// <summary>Why the save did not happen; null when it did.</summary>
    public Exception? Failure { get; private set; }

    public string Description => Str.Format("Images.Edit.Save.StatusFormat", Path.GetFileName(Destination));

    public Task RunAsync(CancellationToken ct)
    {
        try
        {
            var bytes = services.Exporter.Export(sourcePath, recipe, target, ct, restored);
            ct.ThrowIfCancellationRequested();
            if (replace) ImageExporter.ReplaceFile(Destination, bytes);
            else File.WriteAllBytes(Destination, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageDecodeException
                                       or NotSupportedException or InvalidOperationException or OperationCanceledException)
        {
            Failure = ex;
        }
        return Task.CompletedTask;
    }
}
