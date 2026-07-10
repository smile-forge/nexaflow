using Nexaflow.Features.Common;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Features.ProductManager.Services;

/// <summary>
/// Runs a full snaplink scan off the UI thread through the shell's background-activity queue.
/// </summary>
/// <remarks>
/// A whole-tree validation tree-sitter-parses every referenced source file — seconds of work on a real
/// product — so it must never run on the dispatcher. Handing it to <see cref="IShellServices.QueueBackgroundTask"/>
/// also surfaces it in the activity area, so the user can see the scan is running.
/// </remarks>
public sealed class ValidateSnaplinksTask(ProductState state, string productRoot) : IBackgroundTask
{
    public string Description => "Validating snaplinks";

    /// <summary>Read from the completion callback (which the shell invokes on the UI thread).</summary>
    public IntegrityReport? Result { get; private set; }

    public Task RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        Result = SnaplinkValidator.Validate(state, productRoot);
    }, ct);
}
