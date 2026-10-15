using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// Gives the photo on screen the restoration its recipe asks for: the kept one where it is made, else made now in the
/// background — once the picture tools are here — and then shown. What it is doing is said in the viewer's banner:
/// enhancing and how far, the tools to download first, or why the photo shows unrestored.
/// </summary>
public sealed partial class PhotoRestorer : ObservableObject
{
    private readonly ImageEditingServices _services;
    private readonly IPictureTools? _tools;
    private readonly Func<Action, Task> _runOnUi;
    private ImageEditSession? _session;
    private CancellationTokenSource? _making;
    private (string Photo, RecipeRestore Restore)? _makingFor;
    private (string Photo, RecipeRestore Restore)? _shown;
    private (string Photo, RecipeRestore Restore)? _failedFor;

    /// <param name="tools">The picture tools, where the app has them.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the session and this are read.</param>
    public PhotoRestorer(ImageEditingServices services, IPictureTools? tools, Func<Action, Task> runOnUi)
    {
        _services = services;
        _tools = tools;
        _runOnUi = runOnUi;
        if (tools is not null) tools.Changed += (_, _) => _ = _runOnUi(() => Follow(_session));
    }

    /// <summary>A restoration is ready to show: the viewer renders again.</summary>
    public event Action? Restored;

    /// <summary>Whether this machine can restore pictures at all — what the editor's AI controls show for.</summary>
    public bool Available => _tools is { State: not ToolModelsState.Unavailable };

    /// <summary>Whether a <paramref name="width"/> × <paramref name="height"/> photo restores at <paramref name="scale"/> here.</summary>
    public bool Restores(int width, int height, int scale) => Available && _tools!.Restores(width, height, scale);

    /// <summary>The photo's recipe restores it and its restoration is not here yet: it cannot be saved until it is.</summary>
    public bool IsPending => _session?.Recipe.Restore is not null && _session.RestoredPath is null;

    /// <summary>What the banner says; null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    /// <summary>The banner shows.</summary>
    public bool HasNotice => Notice is not null;

    /// <summary>The restoration being made, 0…100, while <see cref="IsMaking"/>.</summary>
    [ObservableProperty] private int _percent;

    [ObservableProperty] private bool _isMaking;

    /// <summary>The picture tools are not here, and can be fetched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction), nameof(ActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    private bool _canDownload;

    /// <summary>The restoration failed; it is tried again when asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction), nameof(ActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    private bool _failed;

    /// <summary>The banner has something to offer: the tools to fetch, or the restoration to try again.</summary>
    public bool HasAction => CanDownload || Failed;

    /// <summary>What the banner's button says.</summary>
    public string? ActionLabel => CanDownload ? Str.Get("Images.Restore.Download") : Failed ? Str.Get("Images.Restore.Retry") : null;

    /// <summary>The photo whose recipe to follow; called again whenever its recipe moves.</summary>
    public void Follow(ImageEditSession? session)
    {
        if (!ReferenceEquals(session, _session))
        {
            Stop();
            _shown = _failedFor = null;
            Failed = false;
        }
        _session = session;
        CanDownload = false;
        if (session?.Recipe.Restore is not { } restore)
        {
            Stop();
            _shown = null;
            session?.UseRestored(null);
            Say(null);
            return;
        }

        // Only the strength moved: the restoration shown is still the one asked for.
        if (Same(_shown, session.Path, restore) && session.RestoredPath is not null) return;
        if (_makingFor is not null && !Same(_makingFor, session.Path, restore)) Stop();
        if (_failedFor is not null && !Same(_failedFor, session.Path, restore))
        {
            _failedFor = null;
            Failed = false;
        }

        if (_tools is null || _tools.State == ToolModelsState.Unavailable)
        {
            Unshown(session, Str.Get("Images.Restore.Unavailable"));
            return;
        }
        if (_tools.State != ToolModelsState.Ready)
        {
            CanDownload = _tools.State == ToolModelsState.NotDownloaded;
            Unshown(session, _tools.State == ToolModelsState.Downloading
                ? Str.Format("Images.Restore.Downloading", _tools.DownloadPercent)
                : Str.Format("Images.Restore.NeedsTools", SizeFormatter.FormatBytes(_tools.DownloadBytes)));
            return;
        }

        if (_services.Restored.Find(session.Path, restore, _tools.ModelsVersion) is { } kept)
        {
            session.UseRestored(kept);
            _shown = (session.Path, restore);
            Say(null);
            Restored?.Invoke();
            return;
        }
        // Not made yet: made now, unless it is being made or failed — then the banner keeps saying so.
        Unshown(session, Notice);
        if (_makingFor is null && _failedFor is null) Make(session, restore);
    }

    private static bool Same((string Photo, RecipeRestore Restore)? key, string photo, RecipeRestore restore)
        => key is { } k && k.Photo == photo && k.Restore.MakesSameAs(restore);

    /// <summary>The photo shows unrestored, for now, and the banner says why.</summary>
    private void Unshown(ImageEditSession session, string? notice)
    {
        session.UseRestored(null);
        _shown = null;
        Say(notice);
    }

    /// <summary>The kept restoration would not decode — let go of since it was found: it is made again.</summary>
    public void Lost(ImageEditSession session)
    {
        if (!ReferenceEquals(session, _session)) return;
        session.UseRestored(null);
        _shown = null;
        if (session.Recipe.Restore is { } restore && _tools is not null)
            try { File.Delete(_services.Restored.PathOf(session.Path, restore, _tools.ModelsVersion)); } catch (IOException) { }
        Follow(session);
    }

    private void Make(ImageEditSession session, RecipeRestore restore)
    {
        var cts = new CancellationTokenSource();
        _making = cts;
        _makingFor = (session.Path, restore);
        IsMaking = true;
        Percent = 0;
        Say(Str.Format("Images.Restore.Restoring", 0));
        var progress = new Inline(p => _ = _runOnUi(() =>
        {
            if (!ReferenceEquals(_making, cts)) return;
            Percent = (int)Math.Round(p * 100);
            Say(Str.Format("Images.Restore.Restoring", Percent));
        }));

        _ = Task.Run(async () =>
        {
            string? made = null, failure = null;
            try
            {
                made = await _services.Restored.MakeAsync(_services, session.Path, restore, _tools!, progress, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                failure = ex.Message;
            }
            await _runOnUi(() =>
            {
                if (!ReferenceEquals(_making, cts)) return;
                _making = null;
                _makingFor = null;
                IsMaking = false;
                if (failure is not null)
                {
                    _failedFor = (session.Path, restore);
                    Failed = true;
                    Say(Str.Format("Images.Restore.Failed", failure));
                    return;
                }
                Follow(_session);   // the recipe may have moved on while it was made; what it asks now is shown
            });
        });
    }

    private void Stop()
    {
        _making?.Cancel();
        _making = null;
        _makingFor = null;
        IsMaking = false;
    }

    private void Say(string? notice) => Notice = notice;

    /// <summary>The banner's button: fetches the tools, or tries the restoration again.</summary>
    [RelayCommand(CanExecute = nameof(HasAction))]
    private void Act()
    {
        if (CanDownload) _tools?.Download();
        else if (Failed) Retry();
    }

    private void Retry()
    {
        _failedFor = null;
        Failed = false;
        Follow(_session);
    }

    /// <summary>Progress reported at once, on the reporting thread.</summary>
    private sealed class Inline(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
