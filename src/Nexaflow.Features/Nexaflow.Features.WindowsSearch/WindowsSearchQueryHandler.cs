using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.Services;

namespace Nexaflow.Features.WindowsSearch;

/// <summary>
/// Globally registered query handler.
/// Activates only when the active tab is a FileSystem tab (provides <see cref="FileSystemContext"/>).
/// Opens a new Search tab with Windows Search results.
/// Auto-discovered by <see cref="Nexaflow.Core.FeatureManager.Register"/> and injected
/// with <see cref="IShellServices"/> via its constructor.
/// </summary>
public sealed class WindowsSearchQueryHandler(IShellServices shellServices) : IQueryHandler
{
    public string  Symbol      => "?";
    public string Description =>
        "Searches for files under the current directory using the Windows Search index. " +
        "Opens a new Search tab with results. " +
        "Use for globs (*.cs), quoted terms (\"TODO\"), or filters (size:>1mb, +required -excluded).";

    public float CanProcess(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm?.GetContextObject() is not FileSystemContext fs
            || string.IsNullOrEmpty(fs.RootPath)) return 0f;
        return SearchQueryScorer.Score(input);
    }

    public Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm?.GetContextObject() is not FileSystemContext fs)
            return Task.FromResult<string?>("No file system context is available for search.");

        shellServices.OpenTab("Search", new Dictionary<string, string>
        {
            ["query"] = input,
            ["root"]  = fs.RootPath
        });
        return Task.FromResult<string?>(null);
    }
}
