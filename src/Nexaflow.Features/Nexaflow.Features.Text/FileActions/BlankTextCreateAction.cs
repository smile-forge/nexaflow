using Nexaflow.Features.Common;
using System.IO;

namespace Nexaflow.Features.Text.FileActions;

/// <summary>
/// Creates an empty plain-text (.txt) file. Discovered cross-assembly by the file-system
/// feature registry — no reference to WindowsFileSystem is needed (both contracts live in
/// <c>Features.Common</c>).
/// </summary>
public sealed class BlankTextCreateAction : IFileCreateAction, ICacheable
{
    public string Icon          => "📄";
    public string DisplayName   => "Text File";
    public string FileExtension => ".txt";

    public string? Create(string folderPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return null;

        var dest = Path.Combine(folderPath, fileName);
        File.WriteAllText(dest, string.Empty);
        return dest;
    }
}
