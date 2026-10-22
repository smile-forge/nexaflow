using System.Collections.Generic;
using System.IO;

using Nexaflow.Features.Common;

namespace Nexaflow.Features.Markdown;

/// <summary>
/// Claims a <see cref="DraftFile"/> that is a markdown document — one made for the reader, a summary or a translation of a
/// document — from <see cref="IShellServices.HandleObject"/>, and opens it beside what the reader is working on, as a draft
/// whose Save offers the folder it was made from.
/// </summary>
public sealed class MarkdownDraftHandler(IShellServices shell) : IGenericObjectHandler
{
    public bool CanHandleObject(object obj) =>
        obj is DraftFile { IsValid: true } draft && Path.GetExtension(draft.Path).ToLowerInvariant() is ".md" or ".markdown";

    public void Handle(object obj)
    {
        if (obj is not DraftFile draft) return;

        shell.OpenTab(MarkdownTabRegistration.StaticPageKind, new Dictionary<string, string>
        {
            ["path"]                      = draft.Path,
            [DraftFile.PageParameterName] = draft.OfferedFolder ?? string.Empty,
        }, inRightPane: true);
    }
}
