using System.Runtime.InteropServices;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>
/// Follows every devnode the PnP manager enumerates, starts or removes (CM_Register_Notification with
/// CM_NOTIFY_FILTER_FLAG_ALL_DEVICE_INSTANCES) and reports the USB ones. A take-over is a remove-and-start of
/// the function devnode, so driver changes arrive on the same path as plug and unplug. Callbacks come on a
/// system thread pool thread and must not block, so this only hands the instance id on.
/// </summary>
internal sealed unsafe class UsbPnpWatcher : IDisposable
{
    readonly Action<string> _onUsbDevNode;
    GCHandle _self;
    nint _registration;

    public UsbPnpWatcher(Action<string> onUsbDevNode)
    {
        _onUsbDevNode = onUsbDevNode;
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
                if (instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)) watcher._onUsbDevNode(instanceId);
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
