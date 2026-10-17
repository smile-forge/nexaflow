using System.ComponentModel;
using Nexaflow.Icons;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;

namespace Nexaflow.IO.Usb.Tools;

/// <summary>How much room a tool's tile wants: one column, or the full width of the Live tools tab.</summary>
public enum UsbToolSize { Compact, Wide }

/// <summary>One named input to a <see cref="UsbToolOperation"/>.</summary>
public sealed record UsbToolArgument(string Name, string Description, bool Required = true);

/// <summary>
/// Something a tool can be asked to do from outside its tile — by the agent. What it returns is text, because the
/// agent reads it; <see cref="ChangesSomething"/> marks the operations that write to the device or the disk, which
/// the host puts behind the user's approval.
/// </summary>
public sealed record UsbToolOperation(
    string Name, string Description, bool ChangesSomething, IReadOnlyList<UsbToolArgument> Arguments,
    Func<IReadOnlyDictionary<string, string>, CancellationToken, Task<string>> RunAsync);

/// <summary>
/// A live tool on one device: a mass-storage browser, an audio output tester, a HID input monitor. The device page
/// draws each as a collapsible tile, using the <c>DataTemplate</c> its provider ships for the tool's type.
/// </summary>
/// <remarks>
/// <para>
/// A tool is a view-model without WPF: it raises <see cref="INotifyPropertyChanged"/> and changes its observable
/// state only through <see cref="IUsbToolHost.RunOnUiAsync"/>, so its own assembly stays headless in everything but
/// its templates.
/// </para>
/// <para>
/// <see cref="ActivateAsync"/> runs when the tile is first opened and each time it is opened again;
/// <see cref="Deactivate"/> when it is collapsed or the page is hidden. Live things — a level meter, an input
/// monitor — stop there. Long jobs the user started — a copy, a recording — do not: they run to the end and the
/// tile shows how they went when it is next opened. <see cref="IDisposable.Dispose"/> stops everything; the host
/// calls it when the device goes or the page closes.
/// </para>
/// </remarks>
public interface IUsbTool : INotifyPropertyChanged, IDisposable
{
    /// <summary>Stable and invariant, e.g. <c>usb.audio.output</c>: what AutomationIds and the agent name it by.</summary>
    string ToolId { get; }

    /// <summary>Unique on the page: the tool id and what it works on, e.g. <c>usb.hid:HID\VID_046D…</c>.</summary>
    string Key { get; }

    string Title { get; }

    /// <summary>A glyph for the tile header.</summary>
    IconRef Icon { get; }

    /// <summary>What it does, in the user's words: the header's tooltip and what the agent reads.</summary>
    string Description { get; }

    /// <summary>The function devnode the tool works through, or null when it works on the whole device. The host uses
    /// it to tell which functions no tool covers.</summary>
    string? FunctionInstanceId { get; }

    UsbToolSize Size { get; }

    /// <summary>One line for the collapsed header: "E: · 14.2 GB free", "Monitoring · 31 reports".</summary>
    string Status { get; }

    /// <summary>True while something is happening the user may want to know about with the tile shut — playing,
    /// recording, copying, monitoring.</summary>
    bool IsBusy { get; }

    Task ActivateAsync(CancellationToken ct);

    void Deactivate();

    /// <summary>The tool's state as text for the agent: what it works on and what it currently shows.</summary>
    string Describe();

    IReadOnlyList<UsbToolOperation> Operations { get; }
}

/// <summary>
/// What a tool may do outside itself, granted by the page that holds it — the same reason the Network page's
/// actions get a host: a tool's reach is this interface.
/// </summary>
public interface IUsbToolHost
{
    /// <summary>usb.ids, for naming HID usages and classes; null when none is loaded.</summary>
    UsbIdsDatabase? Names { get; }

    /// <summary>Runs <paramref name="action"/> on the UI thread — the only place a tool changes what is bound.</summary>
    Task RunOnUiAsync(Action action);

    /// <summary>Asks the user to agree to a write. Every operation that changes the device or the disk asks first.
    /// <paramref name="confirmLabel"/> names the button that goes ahead — "Delete" — where the default would not say it.</summary>
    Task<bool> ConfirmAsync(string title, string message, CancellationToken ct, string? confirmLabel = null);

    /// <summary>Asks for a line of text — a name for a new folder, say — starting from <paramref name="initialValue"/>;
    /// null when the user cancels.</summary>
    Task<string?> PromptAsync(string title, string label, string initialValue);

    /// <summary>Opens a file the way the app opens one from its file browser: in the viewer for its type. False when
    /// nothing in the app opens it.</summary>
    bool OpenFile(string path);

    Task<string?> PickOpenFileAsync(IReadOnlyList<string>? extensions);

    /// <summary>One or several files, ticked in the app's picker; empty when the user cancels.</summary>
    Task<IReadOnlyList<string>> PickOpenFilesAsync(IReadOnlyList<string>? extensions);

    Task<string?> PickFolderAsync();

    Task<string?> PickSaveFileAsync(string defaultFileName, IReadOnlyList<string>? extensions);

    /// <summary>Opens another page — the serial terminal for a COM port, say — without the tool knowing which feature
    /// draws it.</summary>
    void OpenPage(string pageKind, IReadOnlyDictionary<string, string> parameters);

    /// <summary>Records what a tool did, for the device's Diagnostics tab.</summary>
    void Log(IUsbTool tool, string message);
}

/// <summary>
/// A family of live tools, as a subfeature plugin: <c>[Subfeature("usbprobe", …)]</c> on an implementation is all it
/// takes to appear on every device it applies to. The device page references none of them.
/// </summary>
public interface IUsbToolProvider
{
    /// <summary>
    /// The tools <paramref name="device"/> supports, one for each thing they work on — each audio endpoint, each HID
    /// collection, each disk. Called off the UI thread whenever the device's functions change. It may look at what
    /// Windows knows (devnodes, audio endpoints) but never talks to the device: that waits for
    /// <see cref="IUsbTool.ActivateAsync"/>.
    /// </summary>
    IReadOnlyList<IUsbTool> Offer(UsbDeviceNode device, IUsbToolHost host);

    /// <summary>A <c>ResourceDictionary</c> of <c>DataTemplate</c>s keyed by this provider's tool types — a relative
    /// component URI into the provider's own assembly, <c>/Assembly;component/Views/Templates.xaml</c>.</summary>
    Uri Templates { get; }
}
