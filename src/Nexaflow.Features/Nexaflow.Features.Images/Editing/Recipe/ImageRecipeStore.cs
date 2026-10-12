using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Images.Editing.Recipe;

/// <summary>What a photo's sidecar held when it was read.</summary>
public enum RecipeLoadStatus
{
    /// <summary>No sidecar: the photo is unedited.</summary>
    None,

    /// <summary>A recipe made against this exact image.</summary>
    Loaded,

    /// <summary>A recipe, but the photo has changed since it was made — the user decides whether it still applies.</summary>
    SourceChanged,

    /// <summary>A sidecar that cannot be read; left in place.</summary>
    Unreadable,

    /// <summary>A sidecar from a newer build; shown unedited and never overwritten.</summary>
    Newer,
}

/// <summary>A sidecar read back, with its recipe when there is one to apply.</summary>
public readonly record struct RecipeLoadResult(RecipeLoadStatus Status, ImageRecipe? Recipe)
{
    /// <summary>True when an edit may write the sidecar: nothing there yet, or a recipe this build owns.</summary>
    public bool IsWritable => Status is RecipeLoadStatus.None or RecipeLoadStatus.Loaded or RecipeLoadStatus.SourceChanged;
}

/// <summary>
/// Keeps each photo's recipe in a hidden <c>&lt;photo&gt;.nfedit</c> beside it. Writes are atomic (a temporary file
/// swapped in), debounced so a slider drag is one write, and a recipe that changes nothing deletes the file.
/// </summary>
/// <remarks>
/// Pending writes are keyed by photo, so the latest recipe for a photo always wins whatever order the timers fire in,
/// and <see cref="Flush"/> writes them out at once — when the viewer moves to another photo or closes.
/// </remarks>
public sealed class ImageRecipeStore : IDisposable
{
    public const string Extension = ".nfedit";

    /// <summary>The swap file a save writes first; a companion like the sidecar itself.</summary>
    public const string PendingExtension = Extension + ".tmp";

    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(400);

    private readonly TimeSpan _debounce;
    private readonly Lock _gate = new();
    private readonly Lock _writeGate = new();   // one file operation at a time, so swaps never interleave
    private readonly Dictionary<string, PendingWrite> _pending = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public ImageRecipeStore() : this(DefaultDebounce) { }

    public ImageRecipeStore(TimeSpan debounce) => _debounce = debounce;

    public static string SidecarPath(string imagePath) => imagePath + Extension;

    /// <summary>True for a sidecar or a sidecar's swap file.</summary>
    public static bool IsSidecar(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(PendingExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The photo a sidecar (or its swap file) belongs to.</summary>
    public static string? ImagePathOf(string sidecarPath)
    {
        if (sidecarPath.EndsWith(PendingExtension, StringComparison.OrdinalIgnoreCase)) return sidecarPath[..^PendingExtension.Length];
        if (sidecarPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) return sidecarPath[..^Extension.Length];
        return null;
    }

    /// <summary>Reads the photo's sidecar, checking it was made against the photo as it is now.</summary>
    public RecipeLoadResult Load(string imagePath)
    {
        lock (_gate)
        {
            // A write still waiting on its debounce is newer than the file.
            if (_pending.TryGetValue(imagePath, out var waiting)) return new(RecipeLoadStatus.Loaded, waiting.Recipe);
        }

        var sidecar = SidecarPath(imagePath);
        string json;
        try
        {
            if (!File.Exists(sidecar)) return new(RecipeLoadStatus.None, null);
            json = File.ReadAllText(sidecar);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(RecipeLoadStatus.Unreadable, null);
        }

        var read = ImageRecipeJson.Deserialize(json);
        return read.Status switch
        {
            RecipeReadStatus.Newer      => new(RecipeLoadStatus.Newer, null),
            RecipeReadStatus.Unreadable => new(RecipeLoadStatus.Unreadable, null),
            _ => read.Recipe!.Source is { } source && !SourceMatches(source, imagePath)
                ? new(RecipeLoadStatus.SourceChanged, read.Recipe)
                : new(RecipeLoadStatus.Loaded, read.Recipe),
        };
    }

    /// <summary>Schedules the recipe to be written once edits pause; the latest recipe for a photo wins.</summary>
    public void SaveDebounced(string imagePath, ImageRecipe recipe)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending.TryGetValue(imagePath, out var existing))
            {
                existing.Recipe = recipe;
                existing.Timer.Change(_debounce, Timeout.InfiniteTimeSpan);
                return;
            }
            var pending = new PendingWrite(recipe);
            pending.Timer = new Timer(_ => WritePending(imagePath), null, _debounce, Timeout.InfiniteTimeSpan);
            _pending[imagePath] = pending;
        }
    }

    /// <summary>Writes the recipe now, superseding any pending write for the photo.</summary>
    public void Save(string imagePath, ImageRecipe recipe)
    {
        Cancel(imagePath);
        Write(imagePath, recipe);
    }

    /// <summary>Removes the photo's sidecar, superseding any pending write.</summary>
    public void Delete(string imagePath)
    {
        Cancel(imagePath);
        Write(imagePath, ImageRecipe.Identity);
    }

    /// <summary>Writes every pending recipe now.</summary>
    public void Flush()
    {
        List<string> paths;
        lock (_gate) paths = [.. _pending.Keys];
        foreach (var path in paths) WritePending(path);
    }

    /// <summary>Writes the photo's pending recipe now, if it has one.</summary>
    public void Flush(string imagePath) => WritePending(imagePath);

    /// <summary>Flushes off the calling thread.</summary>
    public Task FlushAsync() => Task.Run(Flush);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Flush();
    }

    private void Cancel(string imagePath)
    {
        lock (_gate)
        {
            if (_pending.Remove(imagePath, out var pending)) pending.Timer.Dispose();
        }
    }

    private void WritePending(string imagePath)
    {
        ImageRecipe recipe;
        lock (_gate)
        {
            if (!_pending.Remove(imagePath, out var pending)) return;
            pending.Timer.Dispose();
            recipe = pending.Recipe;
        }
        Write(imagePath, recipe);
    }

    private void Write(string imagePath, ImageRecipe recipe)
    {
        var sidecar = SidecarPath(imagePath);
        lock (_writeGate)
        try
        {
            if (recipe.IsIdentity)
            {
                if (File.Exists(sidecar))
                {
                    File.SetAttributes(sidecar, FileAttributes.Normal);
                    File.Delete(sidecar);
                }
            }
            else
            {
                // A recipe first written is anchored to the photo as it is now.
                var anchored = recipe.Source is null ? recipe with { Source = SourceFingerprint.Of(imagePath) } : recipe;
                WriteAtomically(sidecar, ImageRecipeJson.Serialize(anchored));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The photo's folder is read-only or the file is locked: the edit stays live in the viewer and the next
            // edit tries again, so nothing is lost for this session.
        }
    }

    private static void WriteAtomically(string sidecar, string json)
    {
        var swap = sidecar[..^Extension.Length] + PendingExtension;
        File.WriteAllText(swap, json);
        File.SetAttributes(swap, FileAttributes.Hidden);

        if (File.Exists(sidecar))
        {
            // Replacing onto a hidden file is refused, so it is made ordinary for the swap and hidden again after.
            File.SetAttributes(sidecar, FileAttributes.Normal);
            File.Replace(swap, sidecar, null);
        }
        else
        {
            File.Move(swap, sidecar);
        }
        File.SetAttributes(sidecar, FileAttributes.Hidden);
    }

    private static bool SourceMatches(SourceFingerprint source, string imagePath)
    {
        try { return source.Matches(imagePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private sealed class PendingWrite(ImageRecipe recipe)
    {
        public ImageRecipe Recipe { get; set; } = recipe;
        public Timer Timer { get; set; } = null!;
    }
}
