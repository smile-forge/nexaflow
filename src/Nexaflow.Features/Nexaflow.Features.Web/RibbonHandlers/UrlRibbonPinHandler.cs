using System;
using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Interaction;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Icons;

namespace Nexaflow.Features.Web.RibbonHandlers;

/// <summary>
/// Pins a link dropped on the ribbon as a button that opens it in the HTML viewer. It takes the link kind alone, so
/// the ribbon lights up for an actual link, not arbitrary dragged text; the formats browsers carry one in are read
/// before it gets here (<see cref="WebLinks"/>). The button it produces carries the internal <c>Html</c> page kind,
/// so clicking it just opens an HTML tab (no executor needed).
/// </summary>
public sealed class UrlRibbonPinHandler : IRibbonPinHandler
{
    public IReadOnlyList<string> AcceptedKinds { get; } = [DragKinds.Link];

    public IReadOnlyList<RibbonPinResult> Pin(IDragPayload payload)
    {
        // Checked again here: a payload can be handed over without a drag (IShellServices.PinToRibbon).
        if (WebLinks.SingleAddress(payload.Link()) is not { } url) return [];

        var label = new Uri(url).Host is { Length: > 0 } host ? host : url;

        return
        [
            new RibbonPinResult
            {
                PageKind   = HtmlTabRegistration.StaticPageKind,
                Label      = label,
                Icon       = IconRef.Emoji("🌐"),
                PageParams = new() { ["path"] = url }
            },
        ];
    }
}
