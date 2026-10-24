namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// Watches a real folder — or one file, by its folder and a filter — and reports each change by the path the
/// watch was asked with, so a mounted folder reports mounted paths.
/// </summary>
internal sealed class DirectoryLocationWatch : ILocationWatch
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Func<string?, string> _report;

    /// <param name="report">The reported path for a name relative to <paramref name="directory"/>; null when the
    /// watcher lost track (its buffer overflowed) and cannot say which.</param>
    public DirectoryLocationWatch(string directory, string? filter, bool recursive, System.Func<string?, string> report)
    {
        _report  = report;
        _watcher = new FileSystemWatcher(directory, filter ?? "*")
        {
            IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnRename;
        _watcher.Error   += OnError;
        _watcher.EnableRaisingEvents = true;
    }

    public event System.Action<string>? Changed;

    private void OnChange(object sender, FileSystemEventArgs e) => Changed?.Invoke(_report(e.Name));

    private void OnRename(object sender, RenamedEventArgs e)
    {
        Changed?.Invoke(_report(e.OldName));
        Changed?.Invoke(_report(e.Name));
    }

    private void OnError(object sender, ErrorEventArgs e) => Changed?.Invoke(_report(null));

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}

/// <summary>A session's watch, reporting in the VFS's path form rather than the provider's.</summary>
internal sealed class RoutedLocationWatch : ILocationWatch
{
    private readonly ILocationWatch _inner;
    private readonly System.Func<string, string> _toVirtual;

    public RoutedLocationWatch(ILocationWatch inner, System.Func<string, string> toVirtual)
    {
        _inner     = inner;
        _toVirtual = toVirtual;
        _inner.Changed += OnChanged;
    }

    public event System.Action<string>? Changed;

    private void OnChanged(string providerPath) => Changed?.Invoke(_toVirtual(providerPath));

    public void Dispose()
    {
        _inner.Changed -= OnChanged;
        _inner.Dispose();
    }
}
