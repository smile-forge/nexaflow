using Nexaflow.Features.Common;
using Nexaflow.Features.Hex.ViewModels;
using Nexaflow.Features.Hex.Views;
using System.Globalization;
using System.IO;

namespace Nexaflow.Features.Hex;

/// <summary>
/// Registers the Hex viewer page. Beyond <c>path</c> it accepts an optional <c>offset</c> and
/// <c>length</c>, so another feature can open the file <em>at</em> a byte range rather than at the
/// top — the PE inspector jumping to a section, a resource or a TLS callback.
/// <para>
/// Only <c>path</c> identifies the tab: the byte range says where you are looking, not which file
/// this is. So jumping again from the inspector re-points the open tab (see
/// <see cref="Views.HexView"/>'s <c>Reinitialize</c>) rather than stacking up a tab per offset.
/// </para>
/// </summary>
public sealed class HexTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "Hex";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path",   "The file to open."),
        new("offset", "Byte offset to reveal and select from. Decimal, or 0x-prefixed hex.",
            Required: false, Identity: false),
        new("length", "How many bytes to select at that offset. Omit to just place the cursor.",
            Required: false, Identity: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path  = pageParams?.GetValueOrDefault("path") ?? string.Empty;
        var title = string.IsNullOrEmpty(path) ? "Hex" : Path.GetFileName(path);

        long offset = ParseOffset(pageParams?.GetValueOrDefault("offset"));
        long length = ParseOffset(pageParams?.GetValueOrDefault("length"));

        var page = new Page
        {
            Title          = title,
            Icon           = "⬡",
            PageParams     = pageParams,
            ContentFactory = () =>
            {
                var vm = new HexViewModel(path, shell);
                if (offset >= 0) vm.RevealRange(offset, Math.Max(0, length));
                return new HexView(vm);
            },
        };
        page.SetFileBreadcrumbs(path, title);
        return page;
    }

    /// <summary>Accepts decimal or <c>0x</c> hex; -1 when absent or unparseable.</summary>
    internal static long ParseOffset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;

        text = text.Trim();
        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hex) text = text[2..];

        return long.TryParse(text,
                             hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out long value) && value >= 0
            ? value
            : -1;
    }
}
