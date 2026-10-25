using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Email.FileActions;

/// <summary>Opens an email file in the Email viewer tab. Matched to <c>.eml</c>/<c>.msg</c> via the
/// <c>/email</c> experience (see default-filemap.json).</summary>
public sealed class OpenAsEmailAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/email";
    public string ExperienceId => "/email";
    public string ExperienceDescription => Str.Get("Email.Action.ExperienceDescription");

    public string DisplayName => Str.Get("Email.Action.AsEmail");
    public string Icon => "✉️";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool OpensViewer => true;

    // Both overloads filter. Filtering only the selection one meant "As Email" opened a message tab on a
    // text file from the file list and silently did nothing on a one-item selection of that same file.
    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => PerformActionAsync([filePath], ct);

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        var first = filePaths.FirstOrDefault(EmailFileTypes.IsEmail);
        if (first is null) return Task.FromResult(false);

        shell.OpenTab(EmailTabRegistration.StaticPageKind,
            new Dictionary<string, string> { ["path"] = first });
        return Task.FromResult(true);
    }
}
