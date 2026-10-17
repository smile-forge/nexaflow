using System.IO;
using System.Reflection;
using Nexaflow.Features.Common;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.IO.Common.Stores;
using Nexaflow.IO.Usb.Diagnostics;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Plugins;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe;

/// <summary>
/// Takes a device's debug details (<see cref="UsbDebugDetails"/>) — for the developers, not the person using the app:
/// what a tester sends so a device nobody here has can be supported. Copied, or saved as a .json.
/// </summary>
/// <remarks>
/// On the device page the tools are the ones open there; from the USB page each provider is offered the device afresh,
/// through a host that asks nothing of anyone, and the tools are let go once they have answered. A tool that takes too
/// long or fails is recorded as such and the rest still answer.
/// </remarks>
sealed class UsbDebugCollector(IReadOnlyList<ISubfeatureHandle<IUsbToolProvider>> providers, IShellServices shell, UsbIdsStore names)
{
    /// <summary>How long a tool is waited on before it is recorded as giving no answer.</summary>
    internal TimeSpan Patience { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The details, copied; the status line saying so.</summary>
    public async Task<string> CopyAsync(UsbDeviceNode device, IReadOnlyList<IUsbTool>? live, IReadOnlyList<string> log, CancellationToken ct)
    {
        var text = await TextAsync(device, live, log, ct);
        try { System.Windows.Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { return Str.Get("UsbProbe.Status.ClipboardBusy"); }
        return Str.Format("UsbProbe.Status.DebugCopied", device.DisplayName);
    }

    /// <summary>Where to save the details, asked before they are read so the reading is not waited on for nothing.</summary>
    public Task<string?> PickSaveFileAsync(UsbDeviceNode device) => shell.PickSaveFileAsync(FileNameOf(device), [".json"]);

    /// <summary>The details, saved to <paramref name="path"/>; the status line saying where.</summary>
    public async Task<string> SaveAsync(string path, UsbDeviceNode device, IReadOnlyList<IUsbTool>? live, IReadOnlyList<string> log, CancellationToken ct)
    {
        var text = await TextAsync(device, live, log, ct);
        await File.WriteAllTextAsync(path, text, ct);
        return Str.Format("UsbProbe.Status.DebugSaved", path);
    }

    /// <summary>"usb-046d-c08b-debug.json".</summary>
    public static string FileNameOf(UsbDeviceNode device) => $"usb-{device.VendorId:x4}-{device.ProductId:x4}-debug.json";

    /// <summary>The details as indented JSON.</summary>
    public async Task<string> TextAsync(UsbDeviceNode device, IReadOnlyList<IUsbTool>? live, IReadOnlyList<string> log, CancellationToken ct)
    {
        var mask = UsbDebugMask.For(device);
        var notes = new List<string>(log);
        var offered = live is null
            ? await Task.Run(() => UsbToolOffers.From(providers, device, new QuietHost(shell, names, notes),
                (provider, e) => { lock (notes) notes.Add($"{provider}: offering tools failed — {e.GetType().Name}: {e.Message}"); }), ct)
            : null;
        try
        {
            IReadOnlyList<IUsbTool> tools = offered is null ? live! : [.. offered.Select(o => o.Tool)];
            var answered = new List<UsbDebugTool>();
            foreach (var tool in tools) answered.Add(await DetailsOf(tool, mask, ct));
            var hid = await Task.Run(() => UsbHidReportReader.Read(device), ct);
            var notCovered = UsbDeviceViewModel.Uncovered(device, tools.Select(t => t.FunctionInstanceId))
                .Select(u => $"{u.Name} ({u.ClassName}): {u.Guidance}").ToList();
            List<string> logged;
            lock (notes) logged = [.. notes];
            return UsbDebugDetails.ToText(UsbDebugDetails.Build(device, mask, AppVersion(), names.Current?.Version, answered, notCovered, logged, hid));
        }
        finally
        {
            foreach (var (_, tool) in offered ?? []) tool.Dispose();
        }
    }

    async Task<UsbDebugTool> DetailsOf(IUsbTool tool, UsbDebugMask mask, CancellationToken ct)
    {
        if (tool is not IUsbToolDebugDetails source)
            return new UsbDebugTool(tool.ToolId, tool.Title, tool.FunctionInstanceId, tool.Status, null, null);
        try
        {
            var details = await source.DebugDetailsAsync(mask, ct).WaitAsync(Patience, ct);
            return new UsbDebugTool(tool.ToolId, tool.Title, tool.FunctionInstanceId, tool.Status, details, null);
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            var error = e is TimeoutException ? $"no answer in {Patience.TotalSeconds:0} s" : $"0x{e.HResult:X8} {e.GetType().Name}: {e.Message}";
            return new UsbDebugTool(tool.ToolId, tool.Title, tool.FunctionInstanceId, tool.Status, null, error);
        }
    }

    static string AppVersion()
    {
        var app = Assembly.GetEntryAssembly() ?? typeof(UsbDebugCollector).Assembly;
        return app.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? app.GetName().Version?.ToString() ?? "";
    }

    /// <summary>The host tools offered for their details get: they are only asked what they see, so nothing they could
    /// ask of the user is put to anyone — a confirm is declined, a prompt or a picker is cancelled — and what they log
    /// goes into the details.</summary>
    sealed class QuietHost(IShellServices shell, UsbIdsStore names, List<string> notes) : IUsbToolHost
    {
        public UsbIdsDatabase? Names => names.Current;
        public Task RunOnUiAsync(Action action) => shell.RunOnUiAsync(action);
        public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct, string? confirmLabel = null) => Task.FromResult(false);
        public Task<string?> PromptAsync(string title, string label, string initialValue) => Task.FromResult<string?>(null);
        public bool OpenFile(string path) => false;

        public string FetchedFilesFolder => IStoreBrowserHost.DefaultFetchedFilesFolder;
        public Task<string?> PickOpenFileAsync(IReadOnlyList<string>? extensions) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickOpenFilesAsync(IReadOnlyList<string>? extensions) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string defaultFileName, IReadOnlyList<string>? extensions) => Task.FromResult<string?>(null);
        public void OpenPage(string pageKind, IReadOnlyDictionary<string, string> parameters) { }

        public void Log(IUsbTool tool, string message)
        {
            lock (notes) notes.Add($"{tool.Title}: {message}");
        }
    }
}
