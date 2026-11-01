using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// A create-action that copies a stored template file. Runtime-constructed from a
/// <see cref="TemplateDefinition"/> (one per entry) so it is NOT <see cref="ICacheable"/>.
/// </summary>
public sealed class TemplateCreateAction(string name, string icon, string extension, string templatePath, IVirtualFileSystem vfs)
    : IFileCreateAction
{
    public IconRef Icon         => string.IsNullOrEmpty(icon) ? IconRef.Emoji("📄") : IconRef.Parse(icon);

    public string AutomationId  => $"Template_{name}";
    public string DisplayName   => name;
    public string FileExtension => extension;

    public async Task<string?> CreateItemAsync(string folderPath, string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return null;

        var dest = Path.Combine(folderPath, fileName);
        if (await vfs.ExistsAsync(templatePath, ct))
            await vfs.CopyFileAsync(templatePath, dest, replace: false, ct);
        else
            await vfs.CreateFileAsync(dest, ReadOnlyMemory<byte>.Empty, ct);  // template missing — fall back to an empty file
        return dest;
    }
}
