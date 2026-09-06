using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.IO.Common;

/// <summary>What the progress row says: "Extracting photos.zip to photos".</summary>
/// <param name="Verb">The present participle the row leads with — "Extracting", "Compressing".</param>
/// <param name="Subject">What is being worked on: one item's name, or "3 items".</param>
/// <param name="TargetLabel">Where it is going, as a name rather than a path; empty for none.</param>
/// <param name="DestinationPath">Where the work lands. Picks the volume gate, so building an archive
/// and copying onto the same disk queue rather than interleaving into a slower pair.</param>
public sealed record FileOperationRequest(
    string Verb, string Subject, string TargetLabel, string DestinationPath);

/// <summary>
/// A surface that can show a long file operation with a progress bar and a cancel button.
/// <para>
/// Handed to a file action by whatever hosts it. A null host means there is no such surface and the
/// action does the work its own way — which is what lets one action be equally correct inside a file
/// browser and outside it, without having to know which it is in.
/// </para>
/// <para>
/// Declared here rather than in the contracts hub because the currency is
/// <see cref="TransferProgress"/>, which lives here: the same shape a copy already reports, so an
/// archive lands in the same row rather than inventing a parallel one.
/// </para>
/// </summary>
public interface IFileOperationHost
{
    /// <summary>
    /// Runs <paramref name="work"/> on a background thread behind a visible, cancellable row.
    /// <para>
    /// <paramref name="work"/> is handed the progress sink to report into and the row's own token; it
    /// is expected to describe every outcome — including cancellation and failure — in the
    /// <see cref="TransferResult"/> it returns, exactly as <see cref="FileTransferEngine.RunAsync"/>
    /// does, rather than by throwing. The returned task completes when the row does.
    /// </para>
    /// </summary>
    Task Run(FileOperationRequest request,
             Func<IProgress<TransferProgress>, CancellationToken, Task<TransferResult>> work);
}
