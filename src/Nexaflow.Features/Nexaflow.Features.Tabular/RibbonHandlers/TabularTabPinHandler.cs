using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Features.Tabular.Streaming;
using Nexaflow.Features.Tabular.Views;

namespace Nexaflow.Features.Tabular.RibbonHandlers;

/// <summary>
/// Pins a Tabular tab to the ribbon. Captures the file path AND the current DataShape
/// transform chain so re-opening from the ribbon restores every merge, split,
/// evaluate-as and rename the user had applied — not just the file.
/// </summary>
public sealed class TabularTabPinHandler(IShellServices _shell) : IRibbonPinHandler
{
    public string ContentKind => TabularPageRegistration.StaticPageKind;

    public RibbonPinResult? Pin(object payload, int insertIndex = -1)
    {
        if (payload is not Page tab) return null;

        var pageParams = tab.PageParams is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(tab.PageParams);

        // Snapshot the live transform chain — overwrites any stale value from PageParams.
        if (tab.Content is TabularView tv && tv.VM.DataShape is { } shape && shape.Transforms.Count > 0)
        {
            pageParams["transforms"] = TransformSerializer.Serialize(shape.Transforms);
        }
        else
        {
            // No transforms applied — drop the key entirely so the pinned button doesn't
            // carry a stale "[]" payload.
            pageParams.Remove("transforms");
        }

        var path = pageParams.GetValueOrDefault("path");
        var label = string.IsNullOrEmpty(path) ? tab.Title : Path.GetFileName(path);

        return new RibbonPinResult
        {
            Label      = label,
            Icon       = tab.Icon,
            PageParams = pageParams,
        };
    }

    public void Execute(Dictionary<string, string>? pageParams, IRibbonExecutionContext context)
        => _shell.OpenTab(TabularPageRegistration.StaticPageKind, pageParams);
}
