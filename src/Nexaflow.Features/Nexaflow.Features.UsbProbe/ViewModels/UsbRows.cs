using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Icons;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Features.UsbProbe.ViewModels;

/// <summary>
/// The icon a location is drawn with — in the tree, the listing, the detail pane and the device page alike — from the
/// app's icon font, so it takes the theme's colours. A device is drawn as what it is: a disk, a keyboard, a camera.
/// </summary>
public static class UsbIcons
{
    public static IconRef Of(UsbLocation location) => IconRef.Fluent(location.Kind switch
    {
        UsbLocationKind.Root => "usb_plug",
        UsbLocationKind.Bus => "developer_board",
        UsbLocationKind.Port => "circle_small",
        UsbLocationKind.Device => Device(location.Device!),
        UsbLocationKind.Configuration => "settings",
        UsbLocationKind.Bos => "document_bullet_list",
        _ => location.Entry?.Type switch
        {
            UsbDescriptorType.Interface => "puzzle_piece",
            UsbDescriptorType.InterfaceAssociation => "group",
            UsbDescriptorType.Endpoint => "arrow_swap",
            _ => "circle_small",
        },
    });

    /// <summary>
    /// What a device is, by the first thing it does in this order — so a webcam with a microphone is a camera, and a
    /// stick with a firmware interface a disk. Keyboards and mice are told apart by what Windows made of them.
    /// </summary>
    static string Device(UsbDeviceNode device)
    {
        if (device.IsEjected) return "plug_disconnected";
        if (device.IsHub) return "branch_fork";
        var functions = device.Functions.Where(f => f.Mode != UsbFunctionMode.CompositeParent).ToList();
        bool Has(byte cls) => functions.Any(f => device.Speaks(f, cls));
        if (Has(UsbClassCodes.Video)) return "camera";
        if (Has(UsbClassCodes.MassStorage)) return "usb_stick";
        if (Has(UsbClassCodes.Image)) return "camera";
        if (Has(UsbClassCodes.Printer)) return "print";
        if (Has(UsbClassCodes.Audio)) return "speaker_2";
        if (Has(UsbClassCodes.WirelessController)) return "bluetooth";
        if (Has(UsbClassCodes.SmartCard)) return "contact_card";
        if (Has(UsbClassCodes.CdcControl)) return "serial_port";
        // A HID device's first interface says what it is at boot (1 a keyboard, 2 a mouse): a gaming mouse that also
        // offers a keyboard collection for its macro keys is still a mouse.
        if (functions.FirstOrDefault(f => device.Speaks(f, UsbClassCodes.Hid)) is { } hid)
        {
            var boot = device.ClassOf(hid);
            if (boot is { Class: UsbClassCodes.Hid, SubClass: 1, Protocol: 2 }) return "cursor_click";
            if (boot is { Class: UsbClassCodes.Hid, SubClass: 1, Protocol: 1 }) return "keyboard";
            var made = functions.SelectMany(f => f.Children).Select(c => c.ClassName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (made.Contains("Mouse")) return "cursor_click";
            if (made.Contains("Keyboard")) return "keyboard";
            return "games";
        }
        return "usb_plug";
    }
}

/// <summary>
/// One place in the folder-style tree: the buses and the devices plugged in, a hub opening onto what hangs off it.
/// What a device is made of, and an empty port, are rows in the listing, as a file is in the file browser.
/// </summary>
public sealed partial class UsbTreeNode : ObservableObject, IClipboardText
{
    public string ClipboardText => $"{Path}\t{Name}\t{Summary}";
    internal const string PlaceholderPath = "\0";
    readonly Action<UsbTreeNode> _expand;

    public UsbTreeNode(string path, Action<UsbTreeNode> expand)
    {
        Path = path;
        _expand = expand;
    }

    public string Path { get; }

    public ObservableCollection<UsbTreeNode> Children { get; } = [];

    /// <summary>True once <see cref="Children"/> holds real children rather than the placeholder that shows the expander.</summary>
    internal bool IsLoaded { get; set; }

    internal bool IsPlaceholder => Path == PlaceholderPath;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private IconRef _icon;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isNew;

    internal void Describe(UsbLocation location, bool isNew)
    {
        Name = location.Name;
        Summary = location.Summary;
        Icon = UsbIcons.Of(location);
        IsNew = isNew;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !IsLoaded) _expand(this);
    }
}

/// <summary>One row of the listing: a child of the open location, with the columns its kind has values for.</summary>
public sealed partial class UsbListRow : ObservableObject, IClipboardText
{
    public string ClipboardText => $"{Path}\t{Name}\t{Summary}";
    public UsbListRow(UsbLocation location, bool isNew)
    {
        Path = location.Path;
        Update(location, isNew);
    }

    public string Path { get; }

    /// <summary>The location as of the latest snapshot.</summary>
    public UsbLocation Location { get; private set; } = null!;

    [ObservableProperty] private bool _isNew;

    public IconRef Icon => UsbIcons.Of(Location);
    public string Name => Location.Name;
    public string Summary => Location.Summary;
    public string Kind => Location.Entry is { } entry && Location.Kind == UsbLocationKind.Descriptor ? entry.Kind : Location.Kind.ToString();
    public string Port => Location.Kind switch
    {
        UsbLocationKind.Device or UsbLocationKind.Port => Location.Path,
        UsbLocationKind.Bus => Location.Path,
        _ => "",
    };
    public string Ids => Location.Device is { } d && Location.Kind == UsbLocationKind.Device ? $"{d.VendorId:X4}:{d.ProductId:X4}" : "";
    public string Speed => Location.Kind == UsbLocationKind.Device ? Location.Device!.Speed.ToString() : "";
    public string Driver => Location.Kind == UsbLocationKind.Device ? UsbLocationText.Modes(Location.Device!) : "";
    public string Power => Location.Kind == UsbLocationKind.Device ? UsbLocationText.Power(Location.Device!.Power) : "";
    public string Offset => Location.Kind == UsbLocationKind.Descriptor && Location.Entry is { } e ? e.Offset.ToString() : "";
    public string Length => Location.Entry is { } entry ? entry.Length.ToString() : "";

    /// <summary>Points the row at the same place in a newer snapshot, so the row — and its selection — survive the refresh.</summary>
    internal void Update(UsbLocation location, bool isNew)
    {
        Location = location;
        IsNew = isNew;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The device a row stands for, if it is one — what makes it count as "new".</summary>
    internal UsbDeviceNode? Device => Location.Kind == UsbLocationKind.Device ? Location.Device : null;
}
