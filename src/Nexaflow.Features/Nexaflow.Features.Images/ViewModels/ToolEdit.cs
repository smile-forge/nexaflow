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
/// An edit a picture tool makes: gives the photo on screen what its recipe asks of the tool — the kept result where it is
/// made, else made now in the background, once the tool is here — and then shown. What it is doing is said in the viewer's
/// banner: making it and how far, the tool to download first, or why the photo shows without it.
/// </summary>
/// <typeparam name="TAsk">The recipe's section that asks for it.</typeparam>
public abstract partial class ToolEdit<TAsk> : ObservableObject where TAsk : class
{
    private readonly IToolModels? _tools;
    private readonly Func<Action, Task> _runOnUi;
    private readonly string _texts;
    private ImageEditSession? _session;
    private CancellationTokenSource? _making;
    private (string Photo, TAsk Ask)? _makingFor;
    private (string Photo, TAsk Ask)? _shown;
    private (string Photo, TAsk Ask)? _failedFor;

    /// <param name="tools">The tool's models, where the app has them.</param>
    /// <param name="runOnUi">Hops to the UI thread, where the session and this are read.</param>
    /// <param name="texts">The prefix of what its banner says: <c>Making</c>, <c>NeedsTool</c>, <c>Downloading</c>,
    /// <c>Unavailable</c>, <c>Failed</c>, <c>Download</c> and <c>Retry</c> after it.</param>
    protected ToolEdit(ImageEditingServices services, IToolModels? tools, Func<Action, Task> runOnUi, string texts)
    {
        Services = services;
        _tools = tools;
        _runOnUi = runOnUi;
        _texts = texts;
        if (tools is not null) tools.Changed += (_, _) => _ = _runOnUi(() => Follow(_session));
    }

    protected ImageEditingServices Services { get; }

    /// <summary>The photo's result is ready to show: the viewer renders again.</summary>
    public event Action? Made;

    /// <summary>Whether this machine can make it at all — what the editor's controls for it show for.</summary>
    public bool Available => _tools is { State: not ToolModelsState.Unavailable };

    /// <summary>What <paramref name="ask"/> made of <paramref name="photo"/>, kept on disk where the tool is here to name
    /// its models; null otherwise — what a thumbnail of the edited photo is drawn from.</summary>
    public string? KeptFor(string photo, TAsk ask) => _tools is { State: ToolModelsState.Ready } ? Kept(photo, ask, _tools.ModelsVersion) : null;

    /// <summary>The photo's recipe asks for it and it is not here yet: the photo cannot be saved until it is.</summary>
    public bool IsPending => _session is { } session && AskOf(session.Recipe) is not null && UsedPath(session) is null;

    /// <summary>What the banner says; null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    /// <summary>The banner shows.</summary>
    public bool HasNotice => Notice is not null;

    /// <summary>How far it is made, 0…100, while <see cref="IsMaking"/>.</summary>
    [ObservableProperty] private int _percent;

    [ObservableProperty] private bool _isMaking;

    /// <summary>The tool is not here, and can be fetched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction), nameof(ActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    private bool _canDownload;

    /// <summary>Making it failed; it is tried again when asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction), nameof(ActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    private bool _failed;

    /// <summary>The banner has something to offer: the tool to fetch, or the result to try again.</summary>
    public bool HasAction => CanDownload || Failed;

    /// <summary>What the banner's button says.</summary>
    public string? ActionLabel => CanDownload ? Str.Get(_texts + "Download") : Failed ? Str.Get(_texts + "Retry") : null;

    // ── What the edit is ────────────────────────────────────────────────────

    /// <summary>What <paramref name="recipe"/> asks of the tool; null where it asks nothing.</summary>
    protected abstract TAsk? AskOf(ImageRecipe recipe);

    /// <summary>Whether <paramref name="ask"/> makes the same result as <paramref name="other"/> — what may change over a
    /// result without its being made again.</summary>
    protected abstract bool MakesSame(TAsk ask, TAsk other);

    /// <summary>Where <paramref name="ask"/> made of <paramref name="photo"/> by the models is kept, whether it is there or not.</summary>
    protected abstract string KeptPath(string photo, TAsk ask, string modelsVersion);

    /// <summary>The kept result, if it is here.</summary>
    protected abstract string? Kept(string photo, TAsk ask, string modelsVersion);

    /// <summary>Makes the result and keeps it, reporting how far from 0 to 1.</summary>
    protected abstract Task<string> MakeAsync(string photo, TAsk ask, IProgress<double> progress, CancellationToken ct);

    /// <summary>The result the session shows; null where it shows none.</summary>
    protected abstract string? UsedPath(ImageEditSession session);

    /// <summary>Has the session show <paramref name="path"/>'s result, or none.</summary>
    protected abstract void Use(ImageEditSession session, string? path);

    // ── Following the photo ─────────────────────────────────────────────────

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
        if (session is null || AskOf(session.Recipe) is not { } ask)
        {
            Stop();
            _shown = null;
            if (session is not null) Use(session, null);
            Say(null);
            return;
        }

        // What moved does not change what is made: the result shown is still the one asked for.
        if (Same(_shown, session.Path, ask) && UsedPath(session) is not null) return;
        if (_makingFor is not null && !Same(_makingFor, session.Path, ask)) Stop();
        if (_failedFor is not null && !Same(_failedFor, session.Path, ask))
        {
            _failedFor = null;
            Failed = false;
        }

        if (_tools is null || _tools.State == ToolModelsState.Unavailable)
        {
            Unshown(session, Str.Get(_texts + "Unavailable"));
            return;
        }
        if (_tools.State != ToolModelsState.Ready)
        {
            CanDownload = _tools.State == ToolModelsState.NotDownloaded;
            Unshown(session, _tools.State == ToolModelsState.Downloading
                ? Str.Format(_texts + "Downloading", _tools.DownloadPercent)
                : _tools.DownloadFailure is { } failure ? Str.Format(_texts + "DownloadStopped", failure)
                : Str.Format(_texts + "NeedsTool", SizeFormatter.FormatBytes(_tools.DownloadBytes)));
            return;
        }

        if (Kept(session.Path, ask, _tools.ModelsVersion) is { } kept)
        {
            Use(session, kept);
            _shown = (session.Path, ask);
            Say(null);
            Made?.Invoke();
            return;
        }
        // Not made yet: made now, unless it is being made or failed — then the banner keeps saying so.
        Unshown(session, Notice);
        if (_makingFor is null && _failedFor is null) Make(session, ask);
    }

    private bool Same((string Photo, TAsk Ask)? key, string photo, TAsk ask) => key is { } k && k.Photo == photo && MakesSame(k.Ask, ask);

    /// <summary>The photo shows without it, for now, and the banner says why.</summary>
    private void Unshown(ImageEditSession session, string? notice)
    {
        Use(session, null);
        _shown = null;
        Say(notice);
    }

    /// <summary>The kept result would not decode — let go of since it was found: it is made again.</summary>
    public void Lost(ImageEditSession session)
    {
        if (!ReferenceEquals(session, _session)) return;
        Use(session, null);
        _shown = null;
        if (AskOf(session.Recipe) is { } ask && _tools is not null)
            try { File.Delete(KeptPath(session.Path, ask, _tools.ModelsVersion)); } catch (IOException) { }
        Follow(session);
    }

    private void Make(ImageEditSession session, TAsk ask)
    {
        var cts = new CancellationTokenSource();
        _making = cts;
        _makingFor = (session.Path, ask);
        IsMaking = true;
        Percent = 0;
        Say(Str.Format(_texts + "Making", 0));
        var progress = new Inline(p => _ = _runOnUi(() =>
        {
            if (!ReferenceEquals(_making, cts)) return;
            Percent = (int)Math.Round(p * 100);
            Say(Str.Format(_texts + "Making", Percent));
        }));

        _ = Task.Run(async () =>
        {
            string? failure = null;
            try
            {
                await MakeAsync(session.Path, ask, progress, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException)
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
                    _failedFor = (session.Path, ask);
                    Failed = true;
                    Say(Str.Format(_texts + "Failed", failure));
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

    /// <summary>The banner's button: fetches the tool, or tries again.</summary>
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
