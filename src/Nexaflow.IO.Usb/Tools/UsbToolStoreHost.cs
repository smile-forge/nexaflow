using Nexaflow.IO.Common.Stores;

namespace Nexaflow.IO.Usb.Tools;

/// <summary>A store browser's questions — confirm, name, pick, open, log — put through the device page that hosts the tool.</summary>
public sealed class UsbToolStoreHost(IUsbTool tool, IUsbToolHost host) : IStoreBrowserHost
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken ct) => host.ConfirmAsync(title, message, ct, confirmLabel);
    public Task<string?> PromptAsync(string title, string label, string initialValue) => host.PromptAsync(title, label, initialValue);
    public Task<string?> PickFolderAsync() => host.PickFolderAsync();
    public Task<IReadOnlyList<string>> PickOpenFilesAsync() => host.PickOpenFilesAsync(null);
    public bool OpenFile(string path) => host.OpenFile(path);

    public string FetchedFilesFolder => host.FetchedFilesFolder;
    public void Log(string message) => host.Log(tool, message);
    public Task RunOnUiAsync(Action action) => host.RunOnUiAsync(action);
}
