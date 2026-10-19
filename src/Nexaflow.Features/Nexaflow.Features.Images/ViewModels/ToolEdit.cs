using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Features.Images.Editing.Erasing;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// An edit picture tools make: gives the photo on screen what its recipe asks of them — the kept result where it is
/// made, else made now in the background, once the tools it needs are here — and then shown. What it is doing is said in
/// the viewer's banner: making it and how far, the tools to download first, or why the photo shows without it. An edit
/// may need a different set of tools for each ask (<see cref="Needs"/>), none at all for one an algorithm makes, and may
/// work from a picture another edit makes (<see cref="Input"/>).
/// </summary>
/// <typeparam name="TAsk">The recipe's section that asks for it.</typeparam>
public abstract partial class ToolEdit<TAsk> : ObservableObject, IDownloadOffer where TAsk : class
{
    private readonly IReadOnlyList<IToolModels?> _tools;
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
        : this(services, [tools], runOnUi, texts)
    {
    }

    /// <param name="tools">Every tool's models the edit may use, each where the app has it.</param>
    protected ToolEdit(ImageEditingServices services, IReadOnlyList<IToolModels?> tools, Func<Action, Task> runOnUi, string texts)
    {
        Services = services;
        _tools = tools;
        _runOnUi = runOnUi;
        _texts = texts;
        foreach (var tool in tools)
            if (tool is not null) tool.Changed += (_, _) => _ = _runOnUi(() => Follow(_session));
    }

    protected ImageEditingServices Services { get; }

    /// <summary>The photo's result is ready to show: the viewer renders again.</summary>
    public event Action? Made;

    /// <summary>Whether this machine can make it at all — what the editor's controls for it show for.</summary>
    public virtual bool Available => _tools.All(t => t is { State: not ToolModelsState.Unavailable });

    /// <summary>What <paramref name="ask"/> made of <paramref name="input"/>, kept on disk where the tools it needs are here
    /// to name their models; null otherwise — what a thumbnail of the edited photo is drawn from.</summary>
    /// <param name="input">The picture it is made from (<see cref="Input"/>): the photo, or what another edit made of it.</param>
    public string? KeptFor(string input, TAsk ask) => VersionOf(Needs(ask)) is { } version ? Kept(input, ask, version) : null;

    /// <summary>The photo's recipe asks for it and it is not here yet: the photo cannot be saved until it is.</summary>
    public bool IsPending => _session is { } session && AskOf(session.Recipe) is not null && UsedPath(session) is null;

    /// <summary>The tools <paramref name="ask"/> needs — every tool the edit was given, unless an edit says otherwise; a
    /// null among them is one the app does not have.</summary>
    protected virtual IReadOnlyList<IToolModels?> Needs(TAsk ask) => _tools;

    /// <summary>The picture the edit is made from: the photo, unless it is made from what another edit makes — then null
    /// until that is made.</summary>
    protected virtual string? Input(ImageEditSession session) => session.Path;

    /// <summary>What names the models of <paramref name="tools"/> together, once every one of them is here; "" for none.</summary>
    protected static string? VersionOf(IReadOnlyList<IToolModels?> tools)
        => tools.All(t => t is { State: ToolModelsState.Ready }) ? string.Join("+", tools.Select(t => t!.ModelsVersion)) : null;

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
        // Made from what another edit makes, and that is not made yet: it is made once that is, which follows again.
        if (Input(session) is not { } input)
        {
            Stop();
            Unshown(session, null);
            return;
        }

        // What moved does not change what is made: the result shown is still the one asked for.
        if (Same(_shown, input, ask) && UsedPath(session) is not null) return;
        if (_makingFor is not null && !Same(_makingFor, input, ask)) Stop();
        if (_failedFor is not null && !Same(_failedFor, input, ask))
        {
            _failedFor = null;
            Failed = false;
        }

        var needs = Needs(ask);
        if (needs.Any(t => t is null || t.State == ToolModelsState.Unavailable))
        {
            Unshown(session, Str.Get(_texts + "Unavailable"));
            return;
        }
        if (needs.Any(t => t!.State != ToolModelsState.Ready))
        {
            CanDownload = needs.Any(t => t!.State == ToolModelsState.NotDownloaded);
            var downloading = needs.FirstOrDefault(t => t!.State == ToolModelsState.Downloading);
            var stopped = needs.Select(t => t!.DownloadFailure).FirstOrDefault(f => f is not null);
            Unshown(session, downloading is not null ? Str.Format(_texts + "Downloading", downloading.DownloadPercent)
                : stopped is not null ? Str.Format(_texts + "DownloadStopped", stopped)
                : Str.Format(_texts + "NeedsTool", SizeFormatter.FormatBytes(needs.Sum(t => t!.DownloadBytes))));
            return;
        }

        string version = VersionOf(needs)!;
        if (Kept(input, ask, version) is { } kept)
        {
            Use(session, kept);
            _shown = (input, ask);
            Say(null);
            Made?.Invoke();
            return;
        }
        // Not made yet: made now, unless it is being made or failed — then the banner keeps saying so.
        Unshown(session, Notice);
        if (_makingFor is null && _failedFor is null) Make(session, input, ask);
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
        if (AskOf(session.Recipe) is { } ask && Input(session) is { } input && VersionOf(Needs(ask)) is { } version)
            try { File.Delete(KeptPath(input, ask, version)); } catch (IOException) { }
        Follow(session);
    }

    private void Make(ImageEditSession session, string input, TAsk ask)
    {
        var cts = new CancellationTokenSource();
        _making = cts;
        _makingFor = (input, ask);
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
                await MakeAsync(input, ask, progress, cts.Token);
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
                    _failedFor = (input, ask);
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

    /// <summary>The banner's button: fetches the tools the photo's ask needs, or tries again.</summary>
    [RelayCommand(CanExecute = nameof(HasAction))]
    private void Act()
    {
        if (CanDownload && _session is { } session && AskOf(session.Recipe) is { } ask)
        {
            foreach (var tool in Needs(ask))
                if (tool is { State: ToolModelsState.NotDownloaded }) tool.Download();
        }
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

    /// <summary>The next time the photo's result is made — null — or fails — what the banner says of it. Asked before the
    /// recipe moves, so a result already kept is not missed.</summary>
    public async Task<string?> NextMadeAsync(CancellationToken ct)
    {
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMade() => done.TrySetResult(null);
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Notice) && Failed) done.TrySetResult(Notice);
        }
        Made += OnMade;
        PropertyChanged += OnChanged;
        try
        {
            using (ct.Register(() => done.TrySetCanceled(ct))) return await done.Task;
        }
        finally
        {
            Made -= OnMade;
            PropertyChanged -= OnChanged;
        }
    }
}
