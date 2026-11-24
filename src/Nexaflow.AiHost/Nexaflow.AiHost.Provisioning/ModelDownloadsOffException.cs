namespace Nexaflow.AiHost.Provisioning;

/// <summary>A file is not in the store, nor can it be moved in from an earlier one, and this run downloads nothing.</summary>
public sealed class ModelDownloadsOffException(string file)
    : InvalidOperationException($"{file} is not on this machine, and downloads are off in this run.")
{
    /// <summary>The file's name.</summary>
    public string File => file;
}
