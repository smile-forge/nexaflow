using Nexaflow.Features.Common;

namespace Nexaflow.Features.Hex.FileActions;

/// <summary>
/// Opens any binary file in the hex editor tab.
/// Experience "/binary" matches all file types mapped to the binary experience
/// (e.g. executables, firmware, unknown extensions).
/// </summary>
public sealed class ShowBinaryAction : IFileAction
{
    private readonly IShellServices _shell;

    public ShowBinaryAction(IShellServices shell) => _shell = shell;

    public string ExperienceId          => "/binary";
    public string ExperienceDescription => "Open in hex editor";
    public string DisplayName           => "Open in Hex Editor";
    public string Icon                  => "⬡";
    public bool   IsDestructive         => false;
    public bool   SupportsMultipleFiles => false;
    public bool   RequiresRefresh       => false;
    public bool   CanPerformAction      => true;

    public bool PerformAction(string filePath)
    {
        _shell.OpenTab("Hex", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var first = filePaths.FirstOrDefault();
        return first is not null && PerformAction(first);
    }
}
