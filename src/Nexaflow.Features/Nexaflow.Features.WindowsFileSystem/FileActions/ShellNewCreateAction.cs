using Nexaflow.Features.Common;
using System.IO;
using System.Windows.Media;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>A discovered HKCR ShellNew entry, projected into a create-action by the picker.</summary>
public sealed record ShellNewEntry(string Extension, string DisplayName, ImageSource? IconImage, ShellNewSpec Spec);

/// <summary>
/// A create-action backed by an HKCR <c>ShellNew</c> entry. Runtime-constructed
/// (one per discovered extension) so it is NOT <see cref="ICacheable"/> — the
/// picker materialises these from <c>ShellNewRegistry</c> each time it opens.
/// </summary>
public sealed class ShellNewCreateAction(ShellNewEntry entry) : IFileCreateAction
{
    public string       Icon          => "📄";
    public string       DisplayName   => entry.DisplayName;
    public string       FileExtension => entry.Extension;
    public ImageSource? IconImage     => entry.IconImage;

    public string? Create(string folderPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return null;

        var dest    = Path.Combine(folderPath, fileName);
        var content = ShellNewContentResolver.Resolve(entry.Spec);

        if (content.Bytes is not null)
            File.WriteAllBytes(dest, content.Bytes);
        else if (content.TemplatePath is not null && File.Exists(content.TemplatePath))
            File.Copy(content.TemplatePath, dest, overwrite: false);
        else
            File.WriteAllText(dest, string.Empty);

        return dest;
    }
}
