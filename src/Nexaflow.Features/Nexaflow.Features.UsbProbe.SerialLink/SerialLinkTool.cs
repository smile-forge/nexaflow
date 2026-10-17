using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Icons;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.SerialLink;

/// <summary>
/// H5, CDC serial: the COM port Windows made of a serial function, and the way to the terminal for it. The terminal is
/// the Serial page — any COM port's page, not this device's — opened by page kind, so this assembly needs no reference
/// to it.
/// </summary>
public sealed partial class SerialLinkTool(UsbDeviceNode device, UsbFunctionNode function, IUsbToolHost host) : ObservableObject, IUsbTool
{
    /// <summary>The Serial page's kind and its port parameter, as that page registers them.</summary>
    internal const string SerialPageKind = "Serial", PortParam = "port";

    public string ToolId => "usb.serial";
    public string Key => $"{ToolId}:{function.InstanceId}";
    public string Title => Str.Get("SerialLink.Title");
    public IconRef Icon => IconRef.Fluent("serial_port");
    public string Description => Str.Get("SerialLink.Description");
    public string? FunctionInstanceId => function.InstanceId;
    public UsbToolSize Size => UsbToolSize.Compact;
    public bool IsBusy => false;

    public string Port => function.ComPort ?? "";
    public string Status => Port;
    public string Driver => function.Driver.Service ?? "";
    public string Kind => device.ClassOf(function).Name;

    public Task ActivateAsync(CancellationToken ct) => Task.CompletedTask;

    public void Deactivate() { }

    [RelayCommand]
    private void OpenTerminal()
    {
        host.OpenPage(SerialPageKind, new Dictionary<string, string> { [PortParam] = Port });
        host.Log(this, $"Opened the serial terminal on {Port}");
    }

    public string Describe() => $"{Kind} on {Port}, driver {Driver}. The Serial page is its terminal: open_terminal opens it.";

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("open_terminal", "Opens the Serial page on this port, without connecting.", false, [],
            (_, _) => { OpenTerminal(); return Task.FromResult($"The Serial page is open on {Port}; it connects when asked."); }),
    ];

    public void Dispose() { }
}

/// <summary>Offers the serial link for every function Windows gave a COM port.</summary>
[Subfeature("usbprobe", "serial",
    DisplayName = "Serial port",
    Description = "Names the COM port of a USB serial device and opens the serial terminal on it.",
    Order = 3)]
public sealed class UsbSerialToolProvider : IUsbToolProvider
{
    public Uri Templates { get; } = new("/Nexaflow.Features.UsbProbe.SerialLink;component/Views/SerialTemplates.xaml", UriKind.Relative);

    public IReadOnlyList<IUsbTool> Offer(UsbDeviceNode device, IUsbToolHost host) =>
    [
        .. device.Functions
            .Where(f => f.Mode != UsbFunctionMode.CompositeParent && !string.IsNullOrEmpty(f.ComPort))
            .Select(f => new SerialLinkTool(device, f, host)),
    ];
}
