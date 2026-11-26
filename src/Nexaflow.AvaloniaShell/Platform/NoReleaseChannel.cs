namespace Nexaflow.AvaloniaShell.Platform;

/// <summary>
/// Where this build stands against its releases: the spike is never released, so no newer one is ever found. The session's
/// <see cref="UpdatumReleaseSource"/> is the WPF app's channel — it would offer, and install, Nexaflow's installer over this app.
/// </summary>
internal sealed class NoReleaseChannel : IAppReleaseSource
{
    public Task<AppUpdateOffer?> FindNewerAsync(CancellationToken ct) => Task.FromResult<AppUpdateOffer?>(null);

    public Task InstallAsync(CancellationToken ct)
        => throw new InvalidOperationException("The Avalonia shell is not released, so there is nothing to install.");
}
