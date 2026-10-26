using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Audio;

/// <summary>
/// Claims a <see cref="DraftFile"/> that is a recording — text read aloud for the user — from
/// <see cref="IShellServices.HandleObject"/> and opens it as a draft Audio tab, with Save as to keep it.
/// </summary>
public sealed class AudioDraftHandler(IShellServices shell) : IGenericObjectHandler
{
    public bool CanHandleObject(object obj) => obj is DraftFile { IsValid: true } draft && AudioFileTypes.IsAudio(draft.Path);

    public void Handle(object obj)
    {
        if (obj is not DraftFile draft) return;
        shell.OpenTab(AudioTabRegistration.StaticPageKind, new Dictionary<string, string>
        {
            ["paths"]                     = draft.Path,
            [DraftFile.PageParameterName] = draft.OfferedFolder ?? string.Empty,
        });
    }
}
