using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.Services;
using System.IO;

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

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm?.GetContextObject() is not FileSystemContext fs) return 0f;
        if (string.IsNullOrEmpty(fs.RootPath) && fs.AvailableDrives.Count == 0) return 0f;

        var score = SearchQueryScorer.Score(input);

        // A bare absolute path (not quoted) should route to the navigation handler, not search.
        var trimmed = input.Trim();
        if (score > 0 && IsUnquotedAbsolutePath(trimmed))
            score *= 0.25f;

        return score;
    }

    private static bool IsUnquotedAbsolutePath(string input)
    {
        if (input.Length < 2) return false;
        if (input[0] == '"' || input[0] == '\'') return false;
        if (input.Contains('*') || input.Contains('?')) return false; // globs stay as search
        return Path.IsPathRooted(input);
    }

    public Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm?.GetContextObject() is not FileSystemContext fs)
            return Task.FromResult<string?>("No file system context is available for search.");

        shellServices.OpenTab("Search", new Dictionary<string, string>
        {
            ["query"]  = input,
            ["root"]   = fs.RootPath,
            ["drives"] = string.Join(";", fs.AvailableDrives)
        });
        return Task.FromResult<string?>(null);
    }
}
