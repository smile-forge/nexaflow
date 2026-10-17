namespace Nexaflow.IO.Common.Stores;

/// <summary>What a store browser asks of whatever hosts it: questions, pickers, opening a file, a log line, the UI thread.</summary>
public interface IStoreBrowserHost
{
    /// <summary>Asks the user to agree to a change; <paramref name="confirmLabel"/> names the button that goes ahead.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken ct);

    /// <summary>A line of text — a new folder's name — or null when the user cancels.</summary>
    Task<string?> PromptAsync(string title, string label, string initialValue);

    Task<string?> PickFolderAsync();

    /// <summary>One or several files on this PC; empty when the user cancels.</summary>
    Task<IReadOnlyList<string>> PickOpenFilesAsync();

    /// <summary>Opens a file on this PC in the app's viewer for it; false when nothing in the app opens it.</summary>
    bool OpenFile(string path);

    /// <summary>Records what was done to the store, for whoever keeps a log of it.</summary>
    void Log(string message);

    /// <summary>Runs <paramref name="action"/> on the UI thread — where a store's own events are brought to change what is bound.</summary>
    Task RunOnUiAsync(Action action);
}
