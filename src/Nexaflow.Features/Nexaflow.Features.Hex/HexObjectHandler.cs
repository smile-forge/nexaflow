using Nexaflow.Features.Common;
using System.Globalization;
using System.IO;

namespace Nexaflow.Features.Hex;

/// <summary>
/// Claims a <see cref="FileByteRange"/> from <see cref="IShellServices.HandleObject"/> and shows it
/// in the hex viewer.
/// <para>
/// This is the same dispatch a clicked post-it link goes through — the caller says "open this byte
/// range" and never names the Hex feature, exactly as the scratchpad says "open this URL" without
/// naming the web feature. A bare path could not express the offset, and the file-system handler
/// would have claimed it first anyway; a typed payload falls past every string handler to this one.
/// </para>
/// <para>
/// An already-open tab on the same file is re-pointed rather than duplicated, so repeatedly jumping
/// from an inspector does not leave a trail of tabs.
/// </para>
/// </summary>
public sealed class HexObjectHandler(IShellServices shell) : IGenericObjectHandler
{
    public bool CanHandleObject(object obj)
        => obj is FileByteRange { IsValid: true } range && File.Exists(range.Path);

    public void Handle(object obj)
    {
        if (obj is not FileByteRange range) return;

        var pageParams = new Dictionary<string, string>
        {
            ["path"]   = range.Path,
            ["offset"] = range.Offset.ToString(CultureInfo.InvariantCulture),
            ["length"] = range.Length.ToString(CultureInfo.InvariantCulture),
        };

        // OpenTab reuses a matching tab and re-inits it, so an already-open hex view of this file
        // re-seeks (see HexView.Reinitialize) instead of a second tab appearing.
        shell.OpenTab(HexTabRegistration.StaticPageKind, pageParams);
    }
}
