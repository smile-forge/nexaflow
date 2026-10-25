using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// The check every picture made passes while Enhanced AI Protection Mode is on (<see cref="ProtectedAudience"/>):
/// whether it is fit for a general audience, before it is saved or shown.
/// </summary>
internal interface IPictureCheck
{
    /// <summary>Whether <paramref name="picture"/> is fit for a general audience — the check fetched first, the first
    /// time it is asked, where it is not on this machine yet.</summary>
    /// <exception cref="InvalidOperationException">The check could not run: it would not download, or the AI host could not.</exception>
    Task<bool> FitForGeneralAudienceAsync(MediaBytes picture, CancellationToken ct);
}
