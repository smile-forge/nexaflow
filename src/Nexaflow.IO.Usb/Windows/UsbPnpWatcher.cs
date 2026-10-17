using System.Runtime.InteropServices;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>
/// Follows every devnode the PnP manager enumerates, starts or removes (CM_Register_Notification with
/// CM_NOTIFY_FILTER_FLAG_ALL_DEVICE_INSTANCES) and hands each on, saying whether it was enumerated or removed — a
/// devnode that comes and goes under the same id has re-enumerated. Every devnode, not only the <c>USB\</c> ones: what
/// Windows makes of a device (a stick's disk, a phone's portable device once it is unlocked) arrives as devnodes of
/// other buses beneath it, and the listener decides which concern it. A take-over is a remove-and-start of the
/// function devnode, so driver changes arrive on the same path as plug and unplug. Callbacks come on a system thread
/// pool thread and must not block, so this only hands the instance id on.
/// </summary>
internal sealed unsafe class UsbPnpWatcher : IDisposable
{
    readonly Action<string, bool> _onDevNode;
    GCHandle _self;
    nint _registration;

    /// <param name="onDevNode">The devnode's instance id, and true when it was enumerated or removed rather than started.</param>
    public UsbPnpWatcher(Action<string, bool> onDevNode)
    {
        _onDevNode = onDevNode;
        _self = GCHandle.Alloc(this);
        var filter = new CmNotifyFilter
        {
            Size = (uint)sizeof(CmNotifyFilter),
            Flags = CmNotifyFilter.FlagAllDeviceInstances,
            FilterType = CmNotifyFilter.TypeDeviceInstance,
        };
        uint cr = CfgMgr32.CM_Register_Notification(in filter, GCHandle.ToIntPtr(_self), &OnNotification, out _registration);
        if (cr != CfgMgr32.CrSuccess)
        {
            _self.Free();
            throw new InvalidOperationException($"CM_Register_Notification failed: CR 0x{cr:X2}");
        }
    }

    [UnmanagedCallersOnly]
    static uint OnNotification(nint notification, nint context, uint action, CmNotifyEventData* data, uint size)
    {
        // An exception escaping an unmanaged callback ends the process, so nothing leaves this method.
        try
        {
            if (action is CmNotifyEventData.ActionDeviceInstanceEnumerated or CmNotifyEventData.ActionDeviceInstanceStarted or CmNotifyEventData.ActionDeviceInstanceRemoved
                && size > (uint)sizeof(CmNotifyEventData) && GCHandle.FromIntPtr(context).Target is UsbPnpWatcher watcher)
            {
                var instanceId = new string((char*)((byte*)data + sizeof(CmNotifyEventData)));
                watcher._onDevNode(instanceId, action != CmNotifyEventData.ActionDeviceInstanceStarted);
            }
        }
        catch (Exception)
        {
            // a notification lost here is recovered by the next one, or by RefreshAsync
        }
        return 0; // ERROR_SUCCESS
    }

    public void Dispose()
    {
        if (_registration == 0) return;
        // Waits for in-flight callbacks to finish, so the handle below is never freed under one.
        CfgMgr32.CM_Unregister_Notification(_registration);
        _registration = 0;
        _self.Free();
    }
}
