using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Video;

/// <summary>
/// Claims a <see cref="DraftFile"/> that is a clip — one made for the user — from <see cref="IShellServices.HandleObject"/>
/// and opens it as a draft Video tab, with Save as to keep it.
/// </summary>
public sealed class VideoDraftHandler(IShellServices shell) : IGenericObjectHandler
{
    public bool CanHandleObject(object obj) => obj is DraftFile { IsValid: true } draft && VideoFileTypes.IsVideo(draft.Path);

    public void Handle(object obj)
    {
        if (obj is not DraftFile draft) return;
        shell.OpenTab(VideoTabRegistration.StaticPageKind, new Dictionary<string, string>
        {
            ["path"]                      = draft.Path,
            [DraftFile.PageParameterName] = draft.OfferedFolder ?? string.Empty,
        });
    }
}
