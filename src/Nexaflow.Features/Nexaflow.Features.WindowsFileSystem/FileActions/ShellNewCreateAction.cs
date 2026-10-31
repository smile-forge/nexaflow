using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>A discovered HKCR ShellNew entry, projected into a create-action by the picker.</summary>
public sealed record ShellNewEntry(string Extension, string DisplayName, ShellIconRef? IconResource, ShellNewSpec Spec);

/// <summary>
/// A create-action backed by an HKCR <c>ShellNew</c> entry. Runtime-constructed
/// (one per discovered extension) so it is NOT <see cref="ICacheable"/> — the
/// picker materialises these from <c>ShellNewRegistry</c> each time it opens.
/// </summary>
public sealed class ShellNewCreateAction(ShellNewEntry entry, IVirtualFileSystem vfs) : IFileCreateAction
{
    public IconRef      Icon          => IconRef.Emoji("📄");
    public string       DisplayName   => string.IsNullOrWhiteSpace(entry.DisplayName)
        ? Str.Format("WindowsFileSystem.Create.FileTypeFormat", entry.Extension.TrimStart('.').ToUpperInvariant())
        : entry.DisplayName;

    public string       AutomationId  => $"ShellNew_{entry.Extension.TrimStart('.')}";
    public string       FileExtension => entry.Extension;
    public ShellIconRef? IconResource => entry.IconResource;

    public async Task<string?> CreateItemAsync(string folderPath, string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return null;

        var dest    = Path.Combine(folderPath, fileName);
        var content = ShellNewContentResolver.Resolve(entry.Spec);

        // The template is this machine's (ShellNew folder or the registry); the new file goes wherever the folder is.
        if (content.TemplatePath is not null && content.Bytes is null)
            await vfs.CopyFileAsync(content.TemplatePath, dest, replace: false, ct);
        else
            await vfs.CreateFileAsync(dest, content.Bytes ?? [], ct);

        return dest;
    }
}
