using System.Windows.Documents;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Locate;

/// <summary>
/// A <c>locate:</c> link — <c>[the Help button](locate:Chrome_HelpButton)</c> — points at the screen rather than going
/// anywhere: it names controls by their <c>AutomationProperties.AutomationId</c>, and following it lassoes each in turn
/// (<see cref="LocateTour"/>). A comma-separated list is a chain: <c>locate:Chrome_OptionsButton,Options_Language</c>
/// lassoes the first, then the second once the reader has clicked — the click that opens the panel it lives in — or the
/// first has had its time.
/// <para>
/// AutomationIds are the handle because they already are the app's stable names for its controls: every button has one
/// (NXUI001), a journey names each, and they are compiled into the views, so a release build has every one of them.
/// </para>
/// </summary>
public static class LocateLink
{
    public const string Scheme = "locate";

    private const string PinGlyph = "\uE707";   // Segoe MDL2 Assets: MapPin
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    /// <summary>The ids a <c>locate:</c> link names, in order; false for any other link, or one that names nothing.</summary>
    public static bool TryParse(string? url, out IReadOnlyList<string> ids)
    {
        ids = [];
        var text = url?.Trim();
        if (text is null || !text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)) return false;

        var named = Uri.UnescapeDataString(text[(Scheme.Length + 1)..])
                       .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (named.Length == 0) return false;

        ids = named;
        return true;
    }

    /// <summary>
    /// Marks a rendered <c>locate:</c> link as one — a pin before its text, and <paramref name="tooltip"/> — so a reader
    /// can tell it points at the screen before following it. Made for a markdown host's link hook; any other link is left
    /// as it is.
    /// </summary>
    public static void Decorate(Hyperlink link, string url, string? tooltip)
    {
        if (!TryParse(url, out _)) return;

        var pin = new Run(PinGlyph) { FontFamily = IconFont };
        if (link.Inlines.FirstInline is { } first)
        {
            link.Inlines.InsertBefore(first, pin);
            link.Inlines.InsertAfter(pin, new Run(" "));
        }
        else
        {
            link.Inlines.Add(pin);
        }

        if (tooltip is not null) link.ToolTip = tooltip;
    }
}
