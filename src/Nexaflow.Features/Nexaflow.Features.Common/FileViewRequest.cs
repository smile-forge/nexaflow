namespace Nexaflow.Features.Common;

/// <summary>
/// Asks, through <see cref="IShellServices.HandleObject"/>, for a file to be shown in the viewer for its type — what a
/// file dropped on the tabs asks. Looking is all it grants: unlike a path handed over, which opens as a double-click
/// does, this never launches the file, so a script dragged out of a web page is read rather than run. A folder opens
/// as a folder.
/// </summary>
public sealed record FileViewRequest(string Path);
