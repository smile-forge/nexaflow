using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Core.Services;

/// <summary>Where this build stands against its newest release.</summary>
public enum AppUpdateStage
{
    /// <summary>Nothing has asked yet this run.</summary>
    Unchecked,

    /// <summary>A check is reading the releases.</summary>
    Checking,

    /// <summary>The last check found nothing newer than this build.</summary>
    Current,

    /// <summary>A newer release is offered — <see cref="AppUpdateStatus.Offer"/>.</summary>
    Available,

    /// <summary>The last check could not reach or read the releases, and nothing was offered before it.</summary>
    CheckFailed,

    /// <summary>The offered release is downloading, or its installer is taking over the process.</summary>
    Installing,

    /// <summary>Installing the offered release failed before the installer took over; the offer stands.</summary>
    InstallFailed,
}

/// <summary>A release newer than this build, with an installer for it.</summary>
/// <param name="Version">The release's tag, as the releases page names it.</param>
/// <param name="Changelog">What changed in every release ahead of this build, as markdown; empty when none is given.</param>
public sealed record AppUpdateOffer(string Version, string Changelog);

/// <summary>
/// One moment of <see cref="AppUpdates"/>: the stage, the offer and the last failure read together, so a reader never
/// pairs a stage with another moment's offer. What a page offers follows from it — an Update while a release is offered,
/// a Check for updates otherwise.
/// </summary>
/// <param name="Failure">Why the last check or install failed, in the words of what failed; null when it did not.</param>
public sealed record AppUpdateStatus(AppUpdateStage Stage, AppUpdateOffer? Offer, string? Failure = null)
{
    /// <summary>A newer release is known, so installing it — not checking again — is what is offered.</summary>
    public bool IsOffered => Offer is not null
                             && Stage is AppUpdateStage.Available or AppUpdateStage.Installing or AppUpdateStage.InstallFailed;

    /// <summary>The offered release can be installed now: it is offered and no install is under way.</summary>
    public bool CanInstall => Offer is not null && Stage is AppUpdateStage.Available or AppUpdateStage.InstallFailed;

    /// <summary>A check can start now: nothing is offered and nothing is under way.</summary>
    public bool CanCheck => Stage is AppUpdateStage.Unchecked or AppUpdateStage.Current or AppUpdateStage.CheckFailed;
}

/// <summary>Where the app's releases come from, and how one replaces the running build.</summary>
public interface IAppReleaseSource
{
    /// <summary>
    /// The newest release when it is newer than this build and has an installer for it; null when this build is
    /// current. Throws when the releases cannot be reached or read.
    /// </summary>
    Task<AppUpdateOffer?> FindNewerAsync(CancellationToken ct);

    /// <summary>
    /// Downloads the release the last <see cref="FindNewerAsync"/> offered and hands the process over to its
    /// installer, which ends it. Throws when that fails before the hand-over, leaving the app as it was. Called on the
    /// UI thread, because the hand-over closes every window.
    /// </summary>
    Task InstallAsync(CancellationToken ct);
}

/// <summary>
/// Where this build stands against its newest release — one per process, so the startup check's toast and
/// Options → About read and drive the same state. An offer, once found, stays until a check says otherwise: the
/// toast that announced it can be dismissed and the page still offers the update.
/// </summary>
public sealed class AppUpdates
{
    private readonly IAppReleaseSource _source;
    private readonly Action<Action> _toUi;

    private readonly Action<string, Exception> _report;
    private readonly object _gate = new();

    private AppUpdateStage _stage = AppUpdateStage.Unchecked;
    private AppUpdateOffer? _offer;

    private string? _failure;
    private Task<AppUpdateOffer?>? _checking;

    /// <param name="source">Where the releases come from.</param>
    /// <param name="toUi">Runs an action on the UI thread — <see cref="Changed"/> is raised through it.</param>
    /// <param name="report">Records a check or install that failed — what was tried, and the exception — for diagnosis.</param>
    public AppUpdates(IAppReleaseSource source, Action<Action> toUi, Action<string, Exception> report)
    {
        _source = source;
        _toUi   = toUi;
        _report = report;
    }

    /// <summary>The stage, offer and last failure as they stand, read together.</summary>
    public AppUpdateStatus Status { get { lock (_gate) return new AppUpdateStatus(_stage, _offer, _failure); } }

    /// <summary>Raised on the UI thread after every change of <see cref="Status"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Asks the releases whether a newer one exists, off the calling thread, and answers the offer that stands
    /// afterwards. A call while a check runs shares it; a call while an install runs starts nothing. A check that
    /// fails keeps an offer an earlier one found, so a dropped connection never withdraws a known update. Never throws.
    /// </summary>
    public Task<AppUpdateOffer?> CheckAsync(CancellationToken ct = default)
    {
        Task<AppUpdateOffer?> check;
        lock (_gate)
        {
            if (_checking is { IsCompleted: false } running) return running;
            if (_stage is AppUpdateStage.Installing) return Task.FromResult(_offer);

            _stage    = AppUpdateStage.Checking;
            _failure  = null;
            _checking = check = Task.Run(() => FinishCheckAsync(ct), CancellationToken.None);
        }

        RaiseChanged();
        return check;
    }

    /// <summary>
    /// Installs the offered release: downloads it and hands the process over to its installer. Does nothing unless
    /// <see cref="AppUpdateStatus.CanInstall"/>. A failure before the hand-over leaves the offer standing, to try
    /// again, and is reported. Call it on the UI thread.
    /// </summary>
    public async Task InstallAsync(CancellationToken ct = default)
    {
        AppUpdateOffer offer;
        lock (_gate)
        {
            if (!new AppUpdateStatus(_stage, _offer).CanInstall || _offer is null) return;
            offer    = _offer;
            _stage   = AppUpdateStage.Installing;
            _failure = null;
        }
        RaiseChanged();

        try
        {
            await _source.InstallAsync(ct);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _stage   = AppUpdateStage.InstallFailed;
                _failure = ex.Message;
            }
            _report($"installing update {offer.Version}", ex);
            RaiseChanged();
        }
    }

    private async Task<AppUpdateOffer?> FinishCheckAsync(CancellationToken ct)
    {
        AppUpdateOffer? found = null;
        Exception? failed = null;
        try
        {
            found = await _source.FindNewerAsync(ct);
        }
        catch (Exception ex)
        {
            failed = ex;
        }

        AppUpdateOffer? standing;
        lock (_gate)
        {
            if (failed is null) _offer = found;
            _failure = failed?.Message;
            _stage   = _offer is not null ? AppUpdateStage.Available
                     : failed is null     ? AppUpdateStage.Current
                                          : AppUpdateStage.CheckFailed;
            standing = _offer;
        }

        if (failed is not null) _report("checking for updates", failed);
        RaiseChanged();
        return standing;
    }

    private void RaiseChanged() => _toUi(() => Changed?.Invoke(this, EventArgs.Empty));
}
