using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Markdown.FileActions;

/// <summary>
/// Opens a music notation file (.abc) in a Markdown tab, as the one block it is.
///
/// <para>
/// The same tab and the same editor as a markdown document, told by
/// <see cref="SingleBlockFiles"/> that this file is written in one language rather than being a document. So the
/// tune renders engraved, the caret edits it in place, and what is saved is the ABC — no fence, no
/// wrapper, byte-for-byte the kind of file that was opened.
/// </para>
/// <para>
/// A separate action rather than another extension on <see cref="ShowMarkdownAction"/> because the
/// experience is a different one: a reader picking a default for <c>.abc</c> is choosing a music editor,
/// and calling that "Markdown" would be describing the implementation to them.
/// </para>
/// </summary>
public class ShowMusicAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowMusicAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public IconRef Icon                  => IconRef.Emoji("🎼");
    public string DisplayName            => Str.Get("Markdown.Action.Music");
    public static string? StaticExperienceId => "/text/music";
    public string ExperienceId           => "/text/music";
    public string ExperienceDescription  => Str.Get("Markdown.Action.MusicDescription");
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Markdown", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Markdown", new Dictionary<string, string> { ["path"] = path });
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}
