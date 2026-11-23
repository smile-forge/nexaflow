using System.Runtime.InteropServices;
using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Interaction;
using Nexaflow.Visuals.Common.Dropping;

namespace Nexaflow.Core.Services;

/// <summary>
/// The shell's <see cref="IShellClipboard"/>: the Windows clipboard, read through <see cref="WindowsDragPayload"/> and
/// written through <see cref="WindowsDataObject"/>.
/// </summary>
internal sealed class ShellClipboard : IShellClipboard
{
    public IDragPayload Read()
    {
        try { return Clipboard.GetDataObject() is { } data ? new WindowsDragPayload(data) : ComposedPayload.Empty; }
        catch (ExternalException) { return ComposedPayload.Empty; }
    }

    public bool Write(IDragPayload payload)
    {
        try
        {
            Clipboard.SetDataObject(WindowsDataObject.From(payload), copy: true);
            return true;
        }
        catch (ExternalException) { return false; }
    }

    public bool Clear()
    {
        try
        {
            Clipboard.Clear();
            return true;
        }
        catch (ExternalException) { return false; }
    }
}
