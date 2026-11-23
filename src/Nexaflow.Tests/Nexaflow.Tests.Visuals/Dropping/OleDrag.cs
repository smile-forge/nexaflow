using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Nexaflow.Tests.Visuals.Dropping;

/// <summary>
/// Drags over a real window with no mouse: what Windows itself would call — the <c>IDropTarget</c> WPF registered for
/// the window — called with a point and a data object. So hit testing, the preview and bubble routes, text boxes' own
/// handling and the effect handed back to the source are all the real ones.
/// </summary>
internal sealed class OleDrag : IDisposable
{
    private const int EnterSlot = 3, OverSlot = 4, LeaveSlot = 5, DropSlot = 6;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int WithDataFn(IntPtr self, IntPtr data, int keys, long point, ref int effect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OverFn(IntPtr self, int keys, long point, ref int effect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LeaveFn(IntPtr self);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr window, string name);

    private readonly IntPtr _target, _data;
    private readonly DragDropEffects _allowed;
    private bool _entered;

    public OleDrag(Window window, IDataObject data,
                   DragDropEffects allowed = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link)
    {
        var registered = GetProp(new WindowInteropHelper(window).Handle, "OleDropTargetInterface");
        Assert.AreNotEqual(IntPtr.Zero, registered, "the window registered no drop target");

        var iid = new Guid("00000122-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(registered, in iid, out _target));
        _data    = Marshal.GetComInterfaceForObject((ComDataObject)data, typeof(ComDataObject));
        _allowed = allowed;
    }

    /// <summary>Moves over the middle of <paramref name="element"/>.</summary>
    public DragDropEffects Over(FrameworkElement element, DragDropKeyStates keys = DragDropKeyStates.LeftMouseButton)
    {
        var point = Middle(element);
        var effect = (int)_allowed;
        if (!_entered)
        {
            Slot<WithDataFn>(EnterSlot)(_target, _data, (int)keys, point, ref effect);
            _entered = true;
            effect = (int)_allowed;
        }
        Slot<OverFn>(OverSlot)(_target, (int)keys, point, ref effect);
        return (DragDropEffects)effect;
    }

    /// <summary>Lets go over the middle of <paramref name="element"/>; what the source is told.</summary>
    public DragDropEffects Drop(FrameworkElement element, DragDropKeyStates keys = DragDropKeyStates.None)
    {
        var effect = (int)_allowed;
        Slot<WithDataFn>(DropSlot)(_target, _data, (int)keys, Middle(element), ref effect);
        _entered = false;
        return (DragDropEffects)effect;
    }

    public void Leave()
    {
        if (!_entered) return;
        Slot<LeaveFn>(LeaveSlot)(_target);
        _entered = false;
    }

    public void Dispose()
    {
        Leave();
        Marshal.Release(_data);
        Marshal.Release(_target);
    }

    private T Slot<T>(int index) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(_target), index * IntPtr.Size));

    private static long Middle(FrameworkElement element)
    {
        var at = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return (long)(uint)(int)at.X | ((long)(int)at.Y << 32);
    }
}
