using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Text.FileActions;

/// <summary>
/// Creates an empty plain-text (.txt) file. Discovered cross-assembly by the file-system
/// feature registry — no reference to WindowsFileSystem is needed (both contracts live in
/// <c>Features.Common</c>).
/// </summary>
public sealed class BlankTextCreateAction(IVirtualFileSystem vfs) : IFileCreateAction, ICacheable
{
    public string Icon          => "📄";
    public string DisplayName   => Str.Get("Text.Viewer.Action.NewTextFile");
    public string FileExtension => ".txt";

    public async Task<string?> CreateItemAsync(string folderPath, string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return null;

        var dest = Path.Combine(folderPath, fileName);
        await vfs.CreateFileAsync(dest, ReadOnlyMemory<byte>.Empty, ct);
        return dest;
    }
}
