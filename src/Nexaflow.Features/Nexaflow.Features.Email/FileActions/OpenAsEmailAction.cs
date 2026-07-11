using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Email.FileActions;

/// <summary>Opens an email file in the Email viewer tab. Matched to <c>.eml</c>/<c>.msg</c> via the
/// <c>/email</c> experience (see default-filemap.json).</summary>
public sealed class OpenAsEmailAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/email";
    public string ExperienceId => "/email";
    public string ExperienceDescription => "Email message";

    public string DisplayName => "As Email";
    public string Icon => "✉️";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    public bool PerformAction(string filePath)
    {
        shell.OpenTab(EmailTabRegistration.StaticPageKind,
            new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var first = filePaths.FirstOrDefault(EmailFileTypes.IsEmail);
        return first is not null && PerformAction(first);
    }
}
