using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;
using System.IO;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// Creates a new folder. Has no file extension, so the host never appends one —
/// the name the user types is used verbatim as the directory name.
/// </summary>
public sealed class NewFolderCreateAction(IVirtualFileSystem vfs) : IFileCreateAction, ICacheable
{
    public string Icon          => "📁";
    public string DisplayName   => Str.Get("WindowsFileSystem.Create.FolderType");
    public string FileExtension => string.Empty;

    public async Task<string?> CreateItemAsync(string folderPath, string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return null;

        var dest = Path.Combine(folderPath, fileName);
        await vfs.CreateDirectoryAsync(dest, ct);
        return dest;
    }
}
