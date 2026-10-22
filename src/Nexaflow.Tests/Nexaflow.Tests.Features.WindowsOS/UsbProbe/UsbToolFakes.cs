using System.ComponentModel;
using System.Text.Json.Nodes;
using Nexaflow.IO.Usb.Diagnostics;
using Nexaflow.Icons;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;
using Nexaflow.IO.Usb.Binding;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>A plugin handle over an instance the test made, loaded on first use as the real ones are.</summary>
internal sealed class FakeHandle<T>(T value, string id) : ISubfeatureHandle<T> where T : class
{
    public string Owner => "usbprobe";
    public string Id => id;
    public string DisplayName => id;
    public string Description => "";
    public int Order => 0;
    public bool DefaultEnabled => true;
    public string AssemblyFile => "test";
    public bool IsLoaded { get; private set; }

    public T Value
    {
        get
        {
            IsLoaded = true;
            return value;
        }
    }
}

/// <summary>A provider whose offer the test writes, counting how often it is asked.</summary>
internal sealed class FakeProvider(string name, Func<UsbDeviceNode, IUsbToolHost, IReadOnlyList<IUsbTool>> offer) : IUsbToolProvider
{
    public int Offers;
    public Uri Templates { get; } = new($"/Tests;component/{name}.xaml", UriKind.Relative);

    public IReadOnlyList<IUsbTool> Offer(UsbDeviceNode device, IUsbToolHost host)
    {
        Offers++;
        return offer(device, host);
    }
}

/// <summary>A tool that records what the page asks of it, with one operation that reads and one that writes.</summary>
internal class FakeTool(string id, string? function) : IUsbTool
{
    public int Activations, Deactivations;
    public bool Disposed;
    public readonly List<string> Written = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ToolId => id;
    public string Key => $"{id}:{function}";
    public string Title => $"Tool {id}";
    public IconRef Icon => IconRef.Fluent("wrench");
    public string Description => "a test tool";
    public string? FunctionInstanceId => function;
    public UsbToolSize Size => UsbToolSize.Compact;
    public string Status => $"{Written.Count} written";
    public bool IsBusy => false;

    public Task ActivateAsync(CancellationToken ct)
    {
        Activations++;
        return Task.CompletedTask;
    }

    public void Deactivate() => Deactivations++;

    public string Describe() => $"{id} with {Written.Count} write(s)";

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("peek", "reads", false, [], (_, _) => Task.FromResult($"{id} peeked")),
        new("poke", "writes", true, [new UsbToolArgument("value", "what to write")],
            (args, _) =>
            {
                Written.Add(args["value"]);
                PropertyChanged?.Invoke(this, new(nameof(Status)));
                return Task.FromResult($"{id} wrote {args["value"]}");
            }),
    ];

    public void Dispose() => Disposed = true;
}

/// <summary>A tool that adds to a device's debug details whatever the test has it answer.</summary>
internal sealed class FakeDebugTool(string id, string? function, Func<UsbDebugMask, CancellationToken, Task<JsonObject>> answer)
    : FakeTool(id, function), IUsbToolDebugDetails
{
    public Task<JsonObject> DebugDetailsAsync(UsbDebugMask mask, CancellationToken ct) => answer(mask, ct);
}

/// <summary>A tool that shows folders of this PC whole, as a USB disk's does.</summary>
internal sealed class FakeFolderTool(string id, string? function, params string[] folders) : FakeTool(id, function), IUsbToolShownFolders
{
    public IReadOnlyList<string> ShownFolders => folders;
}

/// <summary>A tool that works on a function's driver, as take-over does.</summary>
internal sealed class FakeDriverTool(string id, string? function) : FakeTool(id, function), IUsbDriverTool;

/// <summary>A tool host for a tool tested on its own: confirms as told, picks what it is given, records the rest.</summary>
internal sealed class RecordingHost : IUsbToolHost
{
    public bool Confirm = true;
    public string? PickedFile;
    public string? PickedFolder { get; set; }
    public string? SavedFile { get; set; }
    public readonly List<string> Confirmations = [], Logged = [];
    public readonly List<(string Kind, IReadOnlyDictionary<string, string> Parameters)> Opened = [];

    public UsbIdsDatabase? Names { get; init; } = UsbIdsDatabase.LoadBundled();

    public FakeBroker Broker { get; init; } = new();

    public IUsbDeviceBroker DriverBroker => Broker;

    public Task RunOnUiAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public string? Answer;
    public readonly List<string> OpenedFiles = [];

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct, string? confirmLabel = null)
    {
        Confirmations.Add(message);
        return Task.FromResult(Confirm);
    }

    public Task<string?> PromptAsync(string title, string label, string initialValue) => Task.FromResult(Answer);

    public bool OpenFile(string path)
    {
        OpenedFiles.Add(path);
        return true;
    }

    /// <summary>The run's own folder, so a fetched file is gone with the test host rather than left in this PC's temp space.</summary>
    public string FetchedFilesFolder => _fetched.Value;
    private readonly Lazy<string> _fetched = new(() => TestTemp.CreateSubdirectory("nexaflow-usb-fetched-").FullName);

    public Task<string?> PickOpenFileAsync(IReadOnlyList<string>? extensions) => Task.FromResult(PickedFile);
    public Task<IReadOnlyList<string>> PickOpenFilesAsync(IReadOnlyList<string>? extensions) =>
        Task.FromResult<IReadOnlyList<string>>(PickedFiles ?? (PickedFile is { } one ? [one] : []));
    public IReadOnlyList<string>? PickedFiles;
    public Task<string?> PickFolderAsync() => Task.FromResult(PickedFolder);
    public Task<string?> PickSaveFileAsync(string defaultFileName, IReadOnlyList<string>? extensions) => Task.FromResult(SavedFile);
    public void OpenPage(string pageKind, IReadOnlyDictionary<string, string> parameters) => Opened.Add((pageKind, parameters));
    public void Log(IUsbTool tool, string message) => Logged.Add(message);
}

/// <summary>A take-over broker that changes nothing: it refuses what the test tells it to, answers each change as the
/// test sets, keeps the bindings the test gives it, and records what it was asked.</summary>
internal sealed class FakeBroker : IUsbDeviceBroker
{
    public readonly Dictionary<string, string> Refusals = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<UsbBindingRecord> Bindings = [];
    public readonly List<string> Asked = [];
    public UsbBindingOutcomeKind Answer = UsbBindingOutcomeKind.Succeeded;

    public string? RefusalReason(string interfaceInstanceId) => Refusals.GetValueOrDefault(interfaceInstanceId);

    public Task<UsbBindingOutcome> BindGenericAsync(string interfaceInstanceId, GenericUsbDriverKind kind, CancellationToken ct) =>
        Outcome($"bind {interfaceInstanceId} {kind}");

    public Task<UsbBindingOutcome> RestoreAsync(string interfaceInstanceId, CancellationToken ct) => Outcome($"restore {interfaceInstanceId}");

    public Task<UsbBindingOutcome> RestoreAllAsync(CancellationToken ct) => Outcome("restore all");

    public IReadOnlyList<UsbBindingRecord> GetBindings() => Bindings;

    Task<UsbBindingOutcome> Outcome(string asked)
    {
        Asked.Add(asked);
        return Task.FromResult(new UsbBindingOutcome(Answer, $"{asked}: {Answer}", []));
    }
}
