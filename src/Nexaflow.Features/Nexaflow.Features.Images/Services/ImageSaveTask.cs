using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Visuals.Common.Localization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Images.Services;

/// <summary>
/// A save in the background, its progress among the workspace's jobs: an edited photo exported to a file, or whatever else
/// a save writes — a layered picture's package, the picture flattened into a file of another format.
/// </summary>
public sealed class ImageSaveTask : IBackgroundTask
{
    private readonly Func<CancellationToken, Task> _write;

    /// <summary>The photo at <paramref name="sourcePath"/> with <paramref name="recipe"/> applied, written to
    /// <paramref name="destination"/> as <paramref name="target"/>.</summary>
    public ImageSaveTask(ImageEditingServices services, string sourcePath, ImageRecipe recipe, ImageContainer target, string destination,
                         MadeByTools? made = null)
        : this(destination, async ct =>
        {
            var bytes = await services.Exporter.ExportAsync(sourcePath, recipe, target, ct, made);
            ct.ThrowIfCancellationRequested();
            await services.FileSystem.ReplaceAsync(destination, bytes, ct);
        })
    {
    }

    /// <summary>Whatever <paramref name="write"/> writes to <paramref name="destination"/>.</summary>
    public ImageSaveTask(string destination, Func<CancellationToken, Task> write)
    {
        Destination = destination;
        _write = write;
    }

    public string Destination { get; }

    /// <summary>Why the save did not happen; null when it did.</summary>
    public Exception? Failure { get; private set; }

    public string Description => Str.Format("Images.Edit.Save.StatusFormat", Path.GetFileName(Destination));

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await _write(ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageDecodeException or InvalidDataException
                                       or NotSupportedException or InvalidOperationException or OperationCanceledException)
        {
            Failure = ex;
        }
    }
}
