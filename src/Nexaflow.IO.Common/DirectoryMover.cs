namespace Nexaflow.IO.Common;

/// <summary>
/// A safe recursive directory move: the tree is copied to the destination and each source item is
/// removed only once its own copy is verified. Cross-volume safe (never assumes a rename), and each
/// source file is opened <see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/> so a file
/// held open elsewhere is still copied.
/// <para>
/// This is a narrow front door onto <see cref="FileTransferEngine"/> — the whole-or-nothing shape its
/// callers were written against, where any failure is an exception and a partial result is not a
/// thing you can be handed. Callers that want per-item tolerance, progress, cancellation or a pause
/// when the disk fills should use the engine directly.
/// </para>
/// </summary>
public static class DirectoryMover
{
    /// <summary>
    /// Moves <paramref name="source"/> to <paramref name="destination"/>. Throws if the destination
    /// already exists (the caller decides how to resolve a name clash) and if any part of the tree
    /// could not be moved — in which case whatever could not be copied is still at the source.
    /// </summary>
    public static async Task MoveAsync(string source, string destination, CancellationToken ct = default)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source folder '{source}' does not exist.");
        if (Directory.Exists(destination))
            throw new IOException($"A folder already exists at '{destination}'.");

        var result = await FileTransferEngine.RunAsync(
            new FileTransferRequest(TransferKind.Move, [new TransferItem(source, destination)], ConflictPolicy.Fail),
            ct: ct);

        if (!result.Completed) throw new TaskCanceledException();

        if (result.Failures.Count > 0)
            throw new IOException(string.Join(Environment.NewLine, result.Failures.Select(f => f.Message)));
    }
}
