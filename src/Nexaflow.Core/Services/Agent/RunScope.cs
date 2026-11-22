using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// What a run's tools may reach on disk: what the user has given the assistant by the tabs it sees — the active page's
/// <see cref="IPageViewModel.GetContextGrant"/>, which for a conversation is everything pinned to it — and what the
/// assistant is told when a path it names is not in it.
/// </summary>
internal static class RunScope
{
    /// <summary>The scope <paramref name="page"/> gives a run: its files and folders, or nothing without a page — names compared
    /// as <paramref name="files"/>, the workspace's file system, says they compare where each one is.</summary>
    public static PathScope Of(IPageViewModel? page, IVirtualFileSystem files)
        => page?.GetContextGrant() is { } grant ? new PathScope(grant.Files, grant.Folders, files.GetNameComparison) : PathScope.None;

    /// <summary>Why <paramref name="path"/> cannot be read or opened.</summary>
    public static string Outside(string path)
        => $"'{path}' is outside what the user has given you: only a file open in a tab, or one inside a folder open in a tab, " +
           "can be used. If it is needed, ask the user to open it.";

    /// <summary>Why nothing can be written at <paramref name="path"/>.</summary>
    public static string OutsideToWrite(string path)
        => $"'{path}' is outside what the user has given you: something made is saved only inside a folder open in a tab. " +
           "Leave 'path' out to open it for the user unsaved, for them to save where they choose.";
}
