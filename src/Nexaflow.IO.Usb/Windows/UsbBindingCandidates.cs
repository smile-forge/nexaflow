using Nexaflow.IO.Usb.Binding;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>Reads the facts <see cref="UsbBindingPolicy"/> decides on from the live devnode.</summary>
public static class UsbBindingCandidates
{
    public static UsbBindingCandidate Read(string instanceId)
    {
        if (DevNodes.Locate(instanceId) is not { } devInst) return new UsbBindingCandidate(instanceId, false, null, null, null, []);
        var parent = DevNodes.String(devInst, DevPropKeys.Parent) is { } parentId && DevNodes.Locate(parentId) is { } p
            ? DevNodes.String(p, DevPropKeys.Service) : null;
        var childClasses = DevNodes.Strings(devInst, DevPropKeys.Children)
            .Select(id => DevNodes.Locate(id) is { } c ? DevNodes.String(c, DevPropKeys.Class) : null)
            .OfType<string>().ToList();
        return new UsbBindingCandidate(instanceId, true, DevNodes.String(devInst, DevPropKeys.Service), DevNodes.String(devInst, DevPropKeys.Class), parent, childClasses);
    }
}
