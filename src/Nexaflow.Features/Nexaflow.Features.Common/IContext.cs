namespace Nexaflow.Features.Common;

/// <summary>Marker interface for strongly-typed context objects offered by tab views.</summary>
public interface IContext { }

/// <summary>
/// Context offered by a FileSystem tab via <see cref="IPageView.GetContextObject"/>.
/// Consumed by <c>WindowsSearchQueryHandler</c> to determine the search root and scope.
/// </summary>
public sealed class FileSystemContext : IContext
{
    public required string RootPath    { get; init; }
    public required string CurrentPath { get; init; }
    public IReadOnlyList<string> SelectedItems   { get; init; } = [];
    public IReadOnlyList<string> AvailableDrives { get; init; } = [];
}
