using Nexaflow.Features.Common;
using System.IO;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// A create-action that copies a stored template file. Runtime-constructed from a
/// <see cref="TemplateDefinition"/> (one per entry) so it is NOT <see cref="ICacheable"/>.
/// </summary>
public sealed class TemplateCreateAction(string name, string icon, string extension, string templatePath)
    : IFileCreateAction
{
    public string Icon          => string.IsNullOrEmpty(icon) ? "📄" : icon;
    public string DisplayName   => name;
    public string FileExtension => extension;

    public string? Create(string folderPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return null;

        var dest = Path.Combine(folderPath, fileName);
        if (File.Exists(templatePath))
            File.Copy(templatePath, dest, overwrite: false);
        else
            File.WriteAllText(dest, string.Empty);  // template missing — fall back to an empty file
        return dest;
    }
}
