using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.Text.FileActions;

/// <summary>
/// Opens a file in the read-write Edit Text editor (full load, save, syntax highlighting / spell-check by
/// type). Sits alongside "As Raw Text" on text files (same <c>/text</c> experience).
/// </summary>
public sealed class EditTextAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public EditTextAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "✏";
    public string DisplayName            => "Edit Text";
    public static string? StaticExperienceId => "/text";
    public string ExperienceId           => "/text";
    public string ExperienceDescription  => "Editable text/code editor with save";
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("TextEditor", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("TextEditor", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}
