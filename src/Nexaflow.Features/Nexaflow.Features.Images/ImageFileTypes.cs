using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Previews;

namespace Nexaflow.Features.Images;

/// <summary>
/// The image formats the viewer understands, in one place: the extension set used to filter a
/// selection and the matching glob list used by the folder actions' content check.
/// </summary>
public static class ImageFileTypes
{
    public static readonly HashSet<string> Extensions = new(DecodablePictures.Extensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>Globs (one per extension) for <see cref="Common.IFolderAction.ContainsFileGlobs"/>.</summary>
    public static readonly string[] Globs = Extensions.Select(e => "*" + e).ToArray();

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>Top-level image files in <paramref name="folderPath"/>, ordered by name. Empty on error.</summary>
    public static async Task<List<string>> EnumerateImagesAsync(IVirtualFileSystem vfs, string folderPath, CancellationToken ct = default)
    {
        try
        {
            var names = new List<string>();
            await foreach (var entry in vfs.EnumerateAsync(folderPath, ct))
                if (!entry.IsDirectory && IsImage(entry.Name)) names.Add(entry.Name);
            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => Path.Combine(folderPath, n)).ToList();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return [];
        }
    }
}
