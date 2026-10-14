using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services.Agent;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Providers.Common;
using System.IO;
using System.Text;
using Nexaflow.Core.Localization;
using System.Diagnostics;
using Nexaflow.AiHost.Client;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.IO.Common.Hardware;

namespace Nexaflow.Core.Services;

public sealed class AIService : IAIService
{
    // ── Provider registry ─────────────────────────────────────────────────
    // Keyed by ability-grid column id: each entry is a model-bound execution instance for that column.

    private readonly Dictionary<string, IAiProvider> _providers = [];

    private AiConfig? _abilityConfig;

    /// <summary>Registers the execution provider for an ability-grid column. Called by WorkspaceManager.</summary>
    public void Register(string columnId, IAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_providers.TryGetValue(columnId, out var replaced)) replaced.AvailabilityChanged -= OnProviderAvailabilityChanged;
        _providers[columnId] = provider;
        provider.AvailabilityChanged += OnProviderAvailabilityChanged;
    }

    /// <summary>Clears all registered execution providers (before a hot re-registration).</summary>
    public void ClearProviders()
    {
        foreach (var provider in _providers.Values) provider.AvailabilityChanged -= OnProviderAvailabilityChanged;
        _providers.Clear();
    }

    private void OnProviderAvailabilityChanged(object? sender, EventArgs e) => _workspace.RaiseProvidersChanged();

    /// <summary>The speech-to-text model that serves Speech In now — the first available in its chain — or null.</summary>
    public ISpeechToTextProvider? SpeechToText => Candidates<ISpeechToTextProvider>(AiAbility.SpeechIn).FirstOrDefault();

    /// <summary>The Speech Out chain's first model that can speak now, or null.</summary>
    public ITextToSpeechProvider? TextToSpeech => Candidates<ITextToSpeechProvider>(AiAbility.SpeechOut).FirstOrDefault();

    /// <summary>The Image Generation chain's first model that can draw now, or null.</summary>
    public IImageGenerationProvider? ImageGenerator => Candidates<IImageGenerationProvider>(AiAbility.ImageGeneration).FirstOrDefault();

    /// <summary>The Image Generation (HQ) chain's first model that can draw now, or null.</summary>
    public IImageGenerationProvider? HighQualityImageGenerator => Candidates<IImageGenerationProvider>(AiAbility.HighQualityImageGeneration).FirstOrDefault();

    /// <summary>The Video Generation chain's first model that can make a clip now, or null.</summary>
    public IVideoGenerationProvider? VideoGenerator => Candidates<IVideoGenerationProvider>(AiAbility.VideoGeneration).FirstOrDefault();

    /// <summary>
    /// Whether a spoken conversation can be held now in <paramref name="language"/>: something listens that hears when a turn
    /// is over — anything but <see cref="LocalColumns.Dictation"/>, which only dictates — a voice speaks the language, and a
    /// model answers. Where it cannot, the mic dictates instead.
    /// </summary>
    public bool CanConverse(string language)
        => CandidateColumns<ISpeechToTextProvider>(AiAbility.SpeechIn).FirstOrDefault() is { Column: { } listening } && !LocalColumns.Dictates(listening)
           && TextToSpeech is { } voice && voice.Speaks(language) && GetProvider(AiAbility.Conversation) is not null;

    /// <summary>Loads the ability-to-column mapping. Called once per context when providers are ready.</summary>
    public void LoadAbilityConfig(AiConfig config) => _abilityConfig = config;

    /// <summary>
    /// The providers that can serve <paramref name="ability"/> now, in its chain's order: each column the chain
    /// names whose provider is registered, of the ability's kind, and available. Unavailability moves a chain
    /// on; a request that fails does not — it has cost its whole latency already.
    /// </summary>
    private IEnumerable<T> Candidates<T>(AiAbility ability) where T : class, IAiProvider
        => CandidateColumns<T>(ability).Select(c => c.Provider);

    /// <summary>The providers that can serve <paramref name="ability"/> now, as <see cref="Candidates"/> gives them, each with
    /// the column that holds it.</summary>
    private IEnumerable<(ProviderModelPair Column, T Provider)> CandidateColumns<T>(AiAbility ability) where T : class, IAiProvider
    {
        if (_abilityConfig is null || !_abilityConfig.Assignments.TryGetValue(ability.ToString(), out var chain))
            yield break;

        foreach (var columnId in chain.ColumnIds)
            if (_providers.TryGetValue(columnId, out var p) && p is T served && p.IsAvailable
                && _abilityConfig.Columns.FirstOrDefault(c => c.Id == columnId) is { } column)
                yield return (column, served);
    }

    /// <summary>The language model that serves <paramref name="ability"/>: the first available in its chain, or null.</summary>
    private ILlmProvider? GetProvider(AiAbility ability) => Candidates<ILlmProvider>(ability).FirstOrDefault();

    /// <summary>
    /// The Image Recognition model when it is not the conversation's own — null when none is set or it is the same.
    /// The SAME pooled instance serves two abilities on the same (config, model), so reference equality means "the
    /// image model and the conversation model are the same model".
    /// </summary>
    private ILlmProvider? SeparateImageModel(ILlmProvider conversation)
        => GetProvider(AiAbility.ImageRecognition) is { } image && !ReferenceEquals(image, conversation) ? image : null;

    /// <summary>
    /// Where a request to <paramref name="ability"/> goes, or null when no model serves it now. A row of models of one kind —
    /// all on this machine, or all elsewhere — gives it to the first available. A row holding both has it judged
    /// (<see cref="RequestTriage"/>): light work goes to the machine's model, with the larger one to hand it over to should it get
    /// stuck; the rest goes to the larger model, which may hand reading to the machine's.
    /// </summary>
    private async Task<ModelRoute?> RouteAsync(AiAbility ability, string request, IReadOnlyList<LlmMessage> history, CancellationToken ct)
    {
        var models = Candidates<ILlmProvider>(ability).ToList();
        if (models.Count == 0) return null;

        var local  = models.FirstOrDefault(m => m.Residency == DataResidency.StaysOnMachine);
        var larger = models.FirstOrDefault(m => m.Residency == DataResidency.LeavesMachine);
        if (local is null || larger is null) return new(models[0]);

        var suits = await RequestTriage.SuitsAsync(GetProvider(AiAbility.Disambiguation), local, request, history, ct);
        return (suits ?? ReferenceEquals(models[0], local)) ? new(local, HandOverTo: larger) : new(larger, Reader: local);
    }

    /// <summary>
    /// The context and tools a run offers the model: the shell's (time, workspace, open tabs, its tools, the help the
    /// app ships) and the page's own, when <paramref name="includeContext"/>; nothing when not.
    /// </summary>
    private (string Context, List<IClientTool> Tools) GatherContext(IPageViewModel? page, bool includeContext)
    {
        if (!includeContext) return ("No specific context.", []);

        var pageContext = page?.GetContext() ?? "No specific context.";
        var tools       = new List<IClientTool>(page?.GetClientTools() ?? []);

        // A searchable page gets its search tools automatically — declaring ISearchable is the whole
        // opt-in, so no feature has to remember to hand them out in GetClientTools.
        if (page is ISearchable searchable)
            tools.AddRange(new SearchClientTools(searchable).Tools);

        // Fold in shell-level context + tools (local time, workspace, open windows/tabs, and the shell tools)
        // alongside the active page's.
        var shellCtx = new ShellAi.ShellAiContext(_workspace);
        tools.AddRange(shellCtx.BuildTools());

        // What the workspace can make — a picture, text read aloud, a clip — where it has a model for each now.
        if (_workspace.ShellServices is { } shell)
            tools.AddRange(new Generation.GenerationTools(ImageGenerator, HighQualityImageGenerator, TextToSpeech, VideoGenerator,
                                                          Generation.GeneratedFiles.For(shell)).Tools);

        // The help the app ships, on every page. A model that was not trained on Nexaflow otherwise has to
        // guess at what a document can hold or what a page can do, and a guess reads exactly like knowing.
        // No language pack means no help to offer — which is the case in a unit test of this loop, and the
        // same way BuildTools above yields nothing without a shell.
        if (LanguageManager.TryInstance is not null)
        {
            tools.AddRange(new Help.HelpClientTools(Help.HelpLibrary.Shared).Tools);
            tools.AddRange(new Help.MarkdownSyntaxTools(Help.HelpLibrary.Shared).Tools);
        }

        // The page first — it is what the request is about — then where it sits.
        return (pageContext + "\n\n" + shellCtx.BuildContext(), tools);
    }

    /// <summary>
    /// Context window (in tokens) for the model assigned to the Conversation ability,
    /// or null if no provider is configured or the provider doesn't expose model info.
    /// </summary>
    public async Task<int?> GetConversationContextWindowAsync(CancellationToken ct = default)
    {
        var provider = GetProvider(AiAbility.Conversation);
        if (provider is null) return null;
        try
        {
            var info = await provider.GetModelInfoAsync(ct);
            return info?.ContextWindowTokens;
        }
        catch { return null; }
    }

    // ── Nexaflow's own models ──────────────────────────────────────────────

    /// <summary>The workspace's models of Nexaflow's own: each column whose provider runs its model in the AI host, once
    /// per model.</summary>
    private IReadOnlyList<OwnedModelInUse> OwnedModelsInUse()
    {
        if (_abilityConfig is null) return [];
        var used = new List<OwnedModelInUse>();
        foreach (var column in _abilityConfig.Columns)
            if (_providers.TryGetValue(column.Id, out var provider) && provider.Harness == ModelHarness.Owned && provider is IHostModelCatalog catalog
                && used.All(u => u.ModelId != column.Model))
                used.Add(new OwnedModelInUse(column.Model, NameOf(provider, column.Model), column.Kind, provider, catalog));
        return used;

        static string NameOf(IAiProvider provider, string model)
            => provider is ILocalModelCatalog local && local.Catalog.FirstOrDefault(m => m.Id == model) is { } entry ? entry.DisplayName : model;
    }

    public Task<LocalAiSetup?> DescribeLocalAiAsync(CancellationToken ct = default)
        => ProviderManager.Instance.Service<HostModelSetup>() is { } setup
    ? LocalAiSetupReader.DescribeAsync(setup, OwnedModelsInUse(),
                                               ModelPlacement.Expected(ProviderManager.Instance.Service<ComputeHardwareReport>() ?? ComputeHardware.Probe()), ct)
            : Task.FromResult<LocalAiSetup?>(null);

    public Task<LocalAiCheck> CheckLocalAiAsync(IProgress<LocalAiCheckProgress> progress, CancellationToken ct = default)
        => ProviderManager.Instance.Service<HostModelSetup>() is { } setup
            ? new LocalAiChecker(setup, OwnedModelsInUse(), Path.Combine(ModelAssetStore.DefaultRoot(), "reports")).RunAsync(progress, ct)
            : throw new InvalidOperationException("Nexaflow's own models are not set up in this session.");

    // ── Persistence ───────────────────────────────────────────────────────

    private readonly WorkspaceRuntime _workspace;
    private readonly string _baseDir;

    /// <summary>Where calls that run on in the background are set going; null where there is no list of them to show.</summary>
    private readonly BackgroundJobs? _jobs;

    private List<ConversationRecord> _conversations = [];

    public ConversationRecord? ActiveConversation { get; }

    /// <param name="workspace">The owning Workspace — used to resolve query handlers via FeatureManager.</param>
    /// <param name="conversationsDir">Full path to the directory where this workspace's conversations are stored.</param>
    /// <param name="jobs">Where slow tool calls run on in the background; without it, every call is waited for.</param>
    public AIService(WorkspaceRuntime workspace, string conversationsDir, BackgroundJobs? jobs = null)
    {
        _workspace = workspace;
        _baseDir   = conversationsDir;
        _jobs      = jobs;
    }

    public IReadOnlyList<Page> RestoreContextPages(ConversationRecord conversation)
    {
        var pages = new List<Page>();
        foreach (var ctx in conversation.Context)
        {
            if (string.IsNullOrEmpty(ctx.PageKind)) continue;
            if (FeatureManager.Instance.CreateTab(ctx.PageKind, _workspace, ctx.PageParams) is { } page)
                pages.Add(page);
        }
        return pages;
    }

    public void SetConversationContext(ConversationRecord conversation, IEnumerable<Page> contextPages)
    {
        conversation.Context = contextPages
            .Where(p => !string.IsNullOrEmpty(p.PageKind))
            .Select(p => new ContextRef
            {
                PageKind        = p.PageKind!,
                PageParams      = p.PageParams,
                AssemblyVersion = FeatureManager.Instance.GetPageKindVersion(p.PageKind!),
            })
            .ToList();
    }

    public Task<IEnumerable<ConversationRecord>> LoadConversationsAsync()
    {
        _conversations = ConversationStore.Load(_baseDir);
        return Task.FromResult<IEnumerable<ConversationRecord>>(_conversations);
    }

    public Task SaveConversationAsync(ConversationRecord activeConversation)
    {
        try { ConversationStore.Save(_baseDir, activeConversation); }
        catch { /* never crash on persistence failures */ }
        return Task.CompletedTask;
    }

    public event Action<string>? ConversationArtifactSaved;

    public Task DeleteConversationAsync(string conversationId)
    {
        try
        {
            ConversationStore.Delete(_baseDir, conversationId);
            _conversations.RemoveAll(c => c.Id == conversationId);
        }
        catch { /* never crash on delete failures */ }
        return Task.CompletedTask;
    }

    public async Task SaveConversationArtifactAsync(string conversationId, string name, string json)
    {
        try
        {
            var dir = Path.Combine(_baseDir, conversationId);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, ArtifactFileName(name));
            await File.WriteAllTextAsync(file, json);
            ConversationArtifactSaved?.Invoke(conversationId);
        }
        catch { /* never crash on persistence failures */ }
    }

    public async Task<string?> LoadConversationArtifactAsync(string conversationId, string name)
    {
        try
        {
            var file = Path.Combine(_baseDir, conversationId, ArtifactFileName(name));
            return File.Exists(file) ? await File.ReadAllTextAsync(file) : null;
        }
        catch { return null; }
    }

    /// <summary>Maps an artifact name to a safe <c>{name}.json</c> file name.</summary>
    private static string ArtifactFileName(string name)
    {
        var safe = string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (safe.Length == 0) safe = "artifact";
        return safe + ".json";
    }

    // ── AI routing ────────────────────────────────────────────────────────

    public (IReadOnlyList<(IQueryHandler Handler, float Score)> Scored,
            IQueryHandler? ClearWinner,
            string EffectiveText,
            bool Prefixed)
        ScoreHandlers(string text, IPageViewModel? pageVm)
    {
        var allHandlers = FeatureManager.Instance.GetQueryHandlers(_workspace).ToList();

        // A leading Symbol character explicitly routes to the handler(s) that own it: narrow to them and
        // strip the character. Every narrowed handler is then scored as "prefixed" on the stripped text.
        // With no such prefix, every handler competes on the raw text (prefixed: false).
        var symbolMatches = allHandlers
            .Where(h => h.Symbol is { Length: 1 } s && text.StartsWith(s))
            .ToList();
        var prefixed      = symbolMatches.Count > 0;
        var considered    = prefixed ? symbolMatches : allHandlers;
        var effectiveText = prefixed ? text[1..].TrimStart() : text;

        var scored = considered
            .Select(h => (Handler: h, Score: h.CanProcess(effectiveText, prefixed, pageVm)))
            .Where(x => x.Score > 0f)
            .OrderByDescending(x => x.Score)
            .ToList();

        IQueryHandler? clearWinner = null;
        if (scored.Count > 0)
        {
            var top    = scored[0].Score;
            var second = scored.Count > 1 ? scored[1].Score : 0f;
            if (top >= 0.8f && (scored.Count == 1 || top - second > 0.2f))
                clearWinner = scored[0].Handler;
        }

        return (scored, clearWinner, effectiveText, prefixed);
    }

    public async Task<IQueryHandler?> DisambiguateToolSelection(
        IPageViewModel? page, string input,
        IReadOnlyList<(IQueryHandler Handler, float Score)> candidates)
    {
        var provider = GetProvider(AiAbility.Disambiguation);
        if (provider is null) return null;

        var context  = page?.GetContext() ?? "No specific context.";
        var toolList = string.Join("\n", candidates.Select((c, i) =>
            $"{i + 1}. [confidence {c.Score:P0}] {c.Handler.Description}"));

        var systemPrompt = "You are a routing assistant. Reply with only a single digit — the number of the best tool, or 0 if none apply.";
        var userPrompt   =
            $"Context: {context}\n" +
            $"User typed: \"{input}\"\n\n" +
            $"Tools:\n0. None of these apply\n{toolList}\n\n" +
            "Which tool number should handle this request?";

        var response = await provider.CompleteAsync(
            [new(LlmRole.System, systemPrompt), new(LlmRole.User, userPrompt)]);
        var raw      = response?.RawText?.Trim() ?? string.Empty;

        var digit = raw.FirstOrDefault(char.IsDigit);
        if (digit == default) return null;

        var idx = digit - '0';
        return (idx >= 1 && idx <= candidates.Count) ? candidates[idx - 1].Handler : null;
    }

    public async Task<int?> DisambiguateOptionAsync(
        string contextDescription,
        string question,
        IReadOnlyList<(string Label, string Detail)> options,
        CancellationToken ct = default)
    {
        if (options is null || options.Count == 0) return null;

        var provider = GetProvider(AiAbility.Disambiguation);
        if (provider is null) return null;

        var optionList = string.Join("\n", options.Select((o, i) =>
            $"{i + 1}. {o.Label} — {o.Detail}"));

        var systemPrompt =
            "You are a disambiguation assistant. Reply with only a single number — the index of the best option, or 0 if none apply.";
        var userPrompt =
            $"Context:\n{contextDescription}\n\n" +
            $"Question: {question}\n\n" +
            $"Options:\n0. None of these apply\n{optionList}\n\n" +
            "Reply with only the number of your chosen option.";

        var response = await provider.CompleteAsync(
            [new(LlmRole.System, systemPrompt), new(LlmRole.User, userPrompt)]);
        var raw = response?.RawText?.Trim() ?? string.Empty;

        // Allow multi-digit answers (e.g. "12") so we don't cap at 9 options.
        int i = 0;
        while (i < raw.Length && !char.IsDigit(raw[i])) i++;
        int start = i;
        while (i < raw.Length && char.IsDigit(raw[i])) i++;
        if (start == i) return null;
        if (!int.TryParse(raw.AsSpan(start, i - start), out var picked)) return null;

        if (picked <= 0 || picked > options.Count) return null;
        return picked - 1;
    }

    public async Task<string?> RunAnalysisAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (await RouteAsync(AiAbility.Analysis, systemPrompt + "\n\n" + userPrompt, [], ct) is not { Model: var provider }) return null;

        // Nobody is watching an analysis: on a GPU shared with a reply, it waits its turn behind each token.
        var response = await provider.CompleteAsync(
            [new(LlmRole.System, systemPrompt), new(LlmRole.User, userPrompt)], LlmUrgency.Background, ct);
        return response?.RawText;
    }

    public async Task<string?> RunProblemSolvingAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        // Falling back to Analysis rather than returning null keeps the Solver working out of the
        // box: Problem Solving is a new column in the ability grid, so on an existing workspace it
        // is unassigned until someone chooses to point it somewhere better.
        var request = systemPrompt + "\n\n" + userPrompt;
        if ((await RouteAsync(AiAbility.ProblemSolving, request, [], ct) ?? await RouteAsync(AiAbility.Analysis, request, [], ct)) is not { Model: var provider })
            return null;

        var response = await provider.CompleteAsync(
            [new(LlmRole.System, systemPrompt), new(LlmRole.User, userPrompt)], ct);
        return response?.RawText;
    }

    // ── Client-side agent loop ───────────────────────────────────────────────

    /// <summary>Generous hard ceiling on automated tool turns — a final backstop. Real loops are caught
    /// earlier by repeated-batch detection, and the user can always cancel.</summary>
    private const int MaxAgentSteps = 20;

    /// <summary>Stop once the model emits the same tool batch (tool + args) this many times — it's spinning.</summary>
    private const int MaxIdenticalBatches = 3;

    public Task<AiResponse?> RunAgentAsync(IPageViewModel? page, string input, bool includeContext, IAIResponseHandler approval,
                                          CancellationToken ct = default)
        => RunAgentAsync(page, input, includeContext, approval, AgentRunOptions.Typed, ct);

    /// <summary>
    /// Runs a request through the agent loop on the model its row routes it to (<see cref="RouteAsync"/>). The machine's model,
    /// stuck on a request — repeating itself, or out of steps — hands it over to the larger model, which carries it on from where
    /// it stands with what was done before it; the larger model may hand reading to the machine's (<see cref="ReadingTool"/>).
    /// </summary>
    public async Task<AiResponse?> RunAgentAsync(
        IPageViewModel? page, string input, bool includeContext,
        IAIResponseHandler approval, AgentRunOptions options, CancellationToken ct = default)
    {
        // What the user is looking at as they asked, read on the caller's thread before anything is awaited.
        var (context, pageTools) = GatherContext(page, includeContext);

        ModelRoute? route;
        try { route = await RouteAsync(AiAbility.Conversation, input, options.History, ct); }
        catch (OperationCanceledException) { return null; }
        if (route is null) return null;
        var ran = new List<LlmMessage>();
        try
        {
            var run = await RunStepsAsync(route.Model, page, context, includeContext, pageTools, input, null, approval, options,
                                          Delegates(route.Reader, approval), ran, ct);

            if (run.Stuck is { } stuck && route.HandOverTo is { } larger)
            {
                approval.ReportProgress($"Handing over to {larger.Name}…");
                var taken = new List<LlmMessage>();
                run = await RunStepsAsync(larger, page, context, includeContext, pageTools, input, HandOver(stuck.Why), approval,
                                          options with { History = [.. options.History, .. ran] }, Delegates(route.Model, approval), taken, ct);
                ran.AddRange(taken.Skip(1));   // the first is the hand-over, said to the model and not by the user
            }

            if (run.Stuck is { } unfinished)
            {
                ran.Add(new(LlmRole.Assistant, unfinished.Message));
                approval.ShowFinal(unfinished.Message);
                return AiResponse.AsMessage(unfinished.Message, run.Artifacts);
            }
            return run.Response;
        }
        finally { options.Ran?.Invoke(ran); }

        static string HandOver(string why) =>
            $"A smaller model began the request above, and what it did and what came of it are above too. It stopped because {why}. " +
            "Take the request over from where it stands: what it did has been done.";
    }

    /// <summary>The models a run on a larger model may hand work to: the machine's own, to read for it.</summary>
    private IReadOnlyList<IClientTool> Delegates(ILlmProvider? reader, IAIResponseHandler approval)
        => reader is null ? []
         : [new ReadingTool(reader, path => _workspace.ShellServices?.GetFileTextExtractor(path), approval.ReportProgress)];

    /// <summary>How a run of the loop on one model ended: its response, or nothing when it was stopped or refused; the files
    /// its tools touched; and, when it got stuck, what to tell the user and why, for a model to take it over.</summary>
    private sealed record StepsEnded(AiResponse? Response, IReadOnlyList<string> Artifacts, StepsStuck? Stuck = null);

    /// <summary>A run that stopped short of an answer: <paramref name="Message"/> for the user, <paramref name="Why"/> for a model
    /// that takes it over.</summary>
    private sealed record StepsStuck(string Message, string Why);

    /// <summary>
    /// The loop on one model: its steps until it answers, is stopped, or gets stuck. <paramref name="handOver"/> is what the
    /// model is told in place of <paramref name="input"/> when it takes a request over, the request itself being in the history.
    /// Every message the run adds to the conversation goes to <paramref name="added"/>, however it ends.
    /// </summary>
    private async Task<StepsEnded> RunStepsAsync(
        ILlmProvider provider, IPageViewModel? page, string context, bool includeContext, List<IClientTool> pageTools,
        string input, string? handOver, IAIResponseHandler approval, AgentRunOptions options, IReadOnlyList<IClientTool> delegates,
        List<LlmMessage> added, CancellationToken ct)
    {
        var ranker = ToolChooser.Picks(pageTools.Count) ? GetProvider(AiAbility.Disambiguation) : null;
        var artifacts = new List<string>();   // files the tools read/created, for conversation context

        // A page whose contents are high-risk sends them off this machine only when the user agrees: every model
        // that would read them and sends what it reads elsewhere is named, and a refusal cancels the request.
        var risk = includeContext ? page?.GetContextSecurityRisk() ?? ContextSecurityRisk.Low : ContextSecurityRisk.Low;
        if (risk == ContextSecurityRisk.High)
        {
            // A recording a tool captures reaches a model that cannot hear as a transcript, so the speech model
            // that would make it reads it too.
            var leaving = new IAiProvider?[] { provider, ranker, SeparateImageModel(provider), provider.SupportsAudio ? null : SpeechToText }
                .OfType<IAiProvider>()
                .Where(p => p.Residency == DataResidency.LeavesMachine)
                .Select(p => p.Name).Distinct().ToList();
            try
            {
                if (leaving.Count > 0 && !await approval.RequestContextReleaseAsync(leaving, ct))
                    return new(null, artifacts);
            }
            catch (OperationCanceledException) { return new(null, artifacts); }
        }

        // The tools offered are chosen again as the work moves on; any of them, offered or not, is run when called.
        var getCommands = BuildGetClientCommandsTool(pageTools);
        var chooser     = new ToolChooser(pageTools, getCommands, [.. options.Tools, .. delegates, .. _jobs?.Tools ?? []], ranker, context, input);
        var toolNames   = chooser.All.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The model is offered its tools in its own protocol where it has one; otherwise its instructions describe them, so
        // its instructions are written for each step's tools.
        var protocol = ToolProtocol.For(provider);
        var messages = new List<LlmMessage> { new(LlmRole.System, "") };
        messages.AddRange(options.History);
        messages.Add(new(LlmRole.User, handOver ?? input));

        var transcript = RunTranscript.Begin(input);   // off unless NEXAFLOW_AI_TRANSCRIPTS names a folder
        var batchCounts = new Dictionary<string, int>();   // tool-batch signature → times seen (loop guard)
        AgentStep? awaiting = null;   // a step said whose calls are not yet answered

        try
        {
            var planMode = false;
            for (var step = 0; step < MaxAgentSteps; step++)
            {
                ct.ThrowIfCancellationRequested();

                var exposed = await chooser.ChooseAsync(ct);
                messages[0] = new(LlmRole.System, BuildSystemPrompt(page, context, protocol.Instructions(exposed), options.ReplyStyle));

                var taken = await protocol.StepAsync(provider, messages, exposed, toolNames, approval, ct);
                transcript?.Step(messages, exposed, taken.Written);

                // A call the model meant but wrote so it cannot run is written again in a context of its own, and takes the
                // attempt's place: the conversation goes on as if it had been written so, rather than holding the attempt and its
                // correction. One that still cannot be read is told why, below.
                if (protocol.Misread(taken).Count > 0 && await protocol.RepairAsync(provider, taken, chooser.All, toolNames, ct) is { } repaired)
                {
                    transcript?.Repaired(repaired.Written);
                    taken = repaired;
                }
                var turn = taken.Turn;

                // 1. A plan was proposed and not yet approved — approve once, then run unattended.
                if (turn.Plan is not null && !planMode)
                {
                    messages.Add(protocol.Said(taken));
                    KeepNarration(approval, turn);
                    planMode = await approval.RequestPlanApprovalAsync(turn.Plan, ct);
                    var decided = new ToolOutcome(planMode
                        ? "The plan is approved: carry out its steps."
                        : "The plan was declined: stop, or suggest a different approach.");
                    messages.AddRange(protocol.Answer(taken, [decided, .. taken.Calls.Select(_ => NotRun("the plan comes first"))], null, null));
                    continue;
                }

                // 2. Pure prefill.
                if (turn.Prefill is not null && turn.ToolCalls.Count == 0)
                {
                    approval.EndReplyDraft(keep: false);
                    return new(AiResponse.AsPrefill(turn.Prefill), artifacts);
                }

                // 3. No tool calls.
                if (turn.ToolCalls.Count == 0)
                {
                    if (turn.ParseErrors.Count > 0)
                    {
                        // The model tried to act but emitted something malformed — let it self-correct.
                        approval.EndReplyDraft(keep: false);
                        messages.Add(protocol.Said(taken));
                        messages.Add(new(LlmRole.User,
                            "TOOL RESULTS\n" + string.Join('\n', turn.ParseErrors) +
                            "\nFix the block and try again, or just answer."));
                        continue;
                    }
                    // Before finalizing, deliver anything the user submitted while we generated this
                    // answer: feed the model its draft + the interjection and let it reconsider.
                    var lateInterjections = approval.TakeInterjections();
                    if (lateInterjections.Count > 0)
                    {
                        chooser.Asked(lateInterjections);
                        KeepNarration(approval, turn);
                        messages.Add(protocol.Said(taken));
                        messages.Add(new(LlmRole.User, FormatInterjections(lateInterjections)));
                        continue;
                    }

                    var final = turn.ExplanationMarkdown.Length > 0 ? turn.ExplanationMarkdown : taken.Written;
                    messages.Add(new(LlmRole.Assistant, final));
                    approval.ShowFinal(final);
                    return new(AiResponse.AsMessage(final, artifacts), artifacts);
                }

                // 4. A batch of tool calls.
                messages.Add(protocol.Said(taken));
                awaiting = taken;

                var calls = turn.ToolCalls
                    .Select(c => (Call: c, Tool: FindTool(chooser.All, c.Tool)))
                    .ToList();

                // Loop guard: if the model keeps emitting the SAME batch (tool + args), it's spinning
                // without progress — stop rather than burn the whole step budget. Tools whose identical
                // repeats are purposeful (e.g. "scroll the page") opt out; the step cap still bounds them.
                var guardedBatch = !calls.All(c => c.Tool is { ExemptFromRepeatGuard: true });
                if (guardedBatch)
                {
                    var signature = string.Join("|", turn.ToolCalls.Select(c => $"{c.Tool}:{c.Arguments.ToJsonString()}"));
                    batchCounts.TryGetValue(signature, out var seen);
                    batchCounts[signature] = seen + 1;
                    if (seen + 1 >= MaxIdenticalBatches)
                    {
                        KeepNarration(approval, turn);
                        messages.AddRange(protocol.Answer(taken, [.. taken.Calls.Select(_ => NotRun("it repeats a call already made"))], null, null));
                        awaiting = null;
                        return new(null, artifacts, new(
                            "I've stopped because I kept repeating the same action without making progress. Let me know how you'd " +
                            "like to proceed. _(stopped: repeated tool call)_",
                            "it kept making the same call without getting anywhere"));
                    }
                }

                var needApproval = !planMode &&
                    calls.Any(rc => rc.Tool?.SafetyOf(rc.Call.Arguments) == ToolSafety.RequiresApproval);

                // An approval request carries the step's prose as its explanation, so the draft gives way to it;
                // a batch that runs unasked leaves the prose standing as the account of what it is doing.
                if (needApproval) approval.EndReplyDraft(keep: false);
                else              KeepNarration(approval, turn);

                // Each call as it is put to the user: what it does, in the tool's own words where it gives them.
                IReadOnlyList<ToolCall> batch = [.. calls.Select(c => c.Call with { Announcement = c.Tool?.Announce(c.Call.Arguments) })];
                if (needApproval && !await approval.RequestToolBatchApprovalAsync(turn.ExplanationMarkdown, batch, ct))
                {
                    // What the user said while deciding is part of the answer.
                    var meanwhile = approval.TakeInterjections();
                    chooser.Asked(meanwhile);
                    var stopped   = NotRun("the user chose not to. Do not try it again — ask what they would prefer, or finish");
                    messages.AddRange(protocol.Answer(taken, [.. taken.Calls.Select(_ => stopped)],
                                                      meanwhile.Count > 0 ? FormatInterjections(meanwhile) : null, null));
                    awaiting = null;
                    continue;
                }

                approval.OnToolBatchStarting(batch);
                var results = await ExecuteBatchAsync(calls, approval, ct);

                // A tool asked to end the run silently (e.g. a command handed off to a background
                // watcher): feed the model nothing and show no final answer — just dismiss the UI.
                if (results.Any(x => x.Result.EndsRun))
                {
                    messages.AddRange(protocol.Answer(taken, [.. results.Select(r => new ToolOutcome(r.Result.ModelText, r.Result.IsError))], null, null));
                    awaiting = null;
                    approval.Abort();
                    return new(null, artifacts);
                }

                approval.OnToolBatchFinished([.. results.Select(r => new ToolRunResult(r.Tool, r.Result))]);
                foreach (var (_, r) in results)
                    if (r.Attachments is { } att)
                        foreach (var p in att)
                            if (!artifacts.Contains(p)) artifacts.Add(p);

                // What each call did moves the work on, so the next step's tools are chosen again; the tools get_client_commands has
                // just listed are all offered from the next step on.
                foreach (var ((call, _), (_, result)) in calls.Zip(results))
                    chooser.Did(call.Tool, call.Arguments, result.ModelText);
                if (results.Any(r => r.Tool == getCommands.Name))
                    chooser.Listed();

                var besides = new List<string>();

                // A tool may have captured an image for the model to see (e.g. the web view screenshot).
                // How it reaches the conversation model depends on the Image Recognition ability: same model
                // → attach the image directly; a different model → that model describes it and we pass text.
                var toolAttachments = new List<LlmAttachment>();
                var toolImages = results.SelectMany(x => x.Result.Images ?? []).ToList();
                if (toolImages.Count > 0)
                {
                    var (atts, note) = await ResolveToolImagesAsync(provider, toolImages, approval, ct);
                    toolAttachments.AddRange(atts ?? []);
                    if (note is not null) besides.Add(note);
                }

                // A tool may have produced a recording to hear (the audio player's listen_to_track): attached for a
                // model that hears, transcribed by the Speech In model for one that does not.
                var toolAudio = results.SelectMany(x => x.Result.Audio ?? []).ToList();
                if (toolAudio.Count > 0)
                {
                    var (atts, note) = await ResolveToolAudioAsync(provider, toolAudio, approval, ct);
                    toolAttachments.AddRange(atts ?? []);
                    if (note is not null) besides.Add(note);
                }

                // Fold in any messages the user interjected while the batch ran, so the model can steer.
                var interjections = approval.TakeInterjections();
                chooser.Asked(interjections);
                if (interjections.Count > 0) besides.Add(FormatInterjections(interjections));

                var outcomes = results.Select(r => new ToolOutcome(r.Result.ModelText, r.Result.IsError)).ToList();
                messages.AddRange(protocol.Answer(taken, outcomes, besides.Count > 0 ? string.Join("\n\n", besides) : null, toolAttachments));
                awaiting = null;
            }

            return new(null, artifacts, new(
                "I've reached the maximum number of automated steps, so I've stopped here. _(stopped: max steps reached)_",
                "it took as many steps as a request is given without finishing"));
        }
        catch (OperationCanceledException) { return new(null, artifacts); }
        finally
        {
            // A step stopped while its calls ran: what came of them is not known, and the conversation says so rather
            // than leaving them unanswered.
            if (awaiting is { } cut)
                messages.AddRange(protocol.Answer(cut, [.. cut.Calls.Select(_ => new ToolOutcome("Interrupted: the run was stopped before what came of it was known."))], null, null));
            added.AddRange(messages.Skip(1 + options.History.Count));
        }

        static ToolOutcome NotRun(string why) => new($"Not run: {why}.");
    }

    /// <summary>Ends a step's draft as narration: the step's prose stands in full, as the parser read it, or —
    /// when it wrote none — nothing does.</summary>
    private static void KeepNarration(IAIResponseHandler handler, ParsedAssistantTurn turn)
    {
        var keep = turn.ExplanationMarkdown.Length > 0;
        if (keep) handler.ShowReplyDraft(turn.ExplanationMarkdown);
        handler.EndReplyDraft(keep);
    }

    private static IClientTool? FindTool(IReadOnlyList<IClientTool> catalog, string name)
        => catalog.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<(string Tool, ToolResult Result)>> ExecuteBatchAsync(
        IReadOnlyList<(ToolCall Call, IClientTool? Tool)> calls,
        IAIResponseHandler approval, CancellationToken ct)
    {
        var results     = new (string Tool, ToolResult Result)[calls.Count];
        var parallelIdx = new List<int>();

        for (var i = 0; i < calls.Count; i++)
        {
            var (call, tool) = calls[i];
            if (tool is null)
            {
                results[i] = (call.Tool, ToolResult.Error(
                    $"Unknown tool '{call.Tool}'",
                    $"There is no client tool named '{call.Tool}'. Call get_client_commands to see what's available."));
                continue;
            }

            // Slow work runs on in the background, where it is listed for the user; the model hears that it has started.
            if (_jobs is { } jobs && tool.RunsInBackground(call.Arguments))
            {
                results[i] = (call.Tool, jobs.Start(tool, call.Arguments, approval).Result);
                continue;
            }
            if (tool.Parallelizable) { parallelIdx.Add(i); continue; }

            approval.ReportProgress($"Running {tool.Name}…");
            results[i] = (call.Tool, await InvokeSafelyAsync(tool, call, ct));
        }

        if (parallelIdx.Count > 0)
        {
            approval.ReportProgress(parallelIdx.Count == 1
                ? $"Running {calls[parallelIdx[0]].Tool!.Name}…"
                : $"Running {parallelIdx.Count} tools…");

            var tasks = parallelIdx.Select(i => InvokeSafelyAsync(calls[i].Tool!, calls[i].Call, ct)).ToArray();
            var done  = await Task.WhenAll(tasks);
            for (var p = 0; p < parallelIdx.Count; p++)
                results[parallelIdx[p]] = (calls[parallelIdx[p]].Call.Tool, done[p]);
        }

        return results;
    }

    private static async Task<ToolResult> InvokeSafelyAsync(IClientTool tool, ToolCall call, CancellationToken ct)
    {
        try { return await tool.InvokeAsync(call.Arguments, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolResult.Error($"{tool.Name} failed: {ex.Message}"); }
    }

    /// <summary>
    /// Frames user messages that arrived mid-response so the model can decide whether to change course.
    /// The interjections are also in the conversation, so nothing is lost if the model finishes first.
    /// </summary>
    private static string FormatInterjections(IReadOnlyList<string> interjections)
    {
        var sb = new StringBuilder(
            "USER INTERJECTION\nWhile you were working, the user sent the following. If it changes the " +
            "task, adjust now; otherwise finish the current step and address it next — it stays in the " +
            "conversation either way:\n");
        foreach (var msg in interjections)
            sb.Append("- ").Append(msg).Append('\n');
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Decides how a tool-captured image reaches the conversation model, honouring the grid's Image
    /// Recognition ability:
    /// <list type="bullet">
    /// <item>No image model set, or it's the SAME model as Conversation → attach the image directly when
    /// that model is multimodal; otherwise return a note that it can't be shown.</item>
    /// <item>A DIFFERENT image model → ask it to describe the image(s) and return that text for the
    /// conversation model, so a text-only conversation model still gets the gist. No image is attached.</item>
    /// </list>
    /// Returns the attachments to carry on the message (or null), plus an optional note to append to the text.
    /// </summary>
    private async Task<(IReadOnlyList<LlmAttachment>? Attachments, string? Note)> ResolveToolImagesAsync(
        ILlmProvider convProvider, IReadOnlyList<ContextImage> images,
        IAIResponseHandler approval, CancellationToken ct)
    {
        var atts = images
            .Select(i => new LlmAttachment(i.Label ?? "image", i.MimeType, i.Bytes))
            .ToList();

        var imageProvider = SeparateImageModel(convProvider);
        if (imageProvider is null)
        {
            return convProvider.SupportsImages
                ? (atts, null)
                : (null, "(An image was captured, but the current model can't view images, so it wasn't attached.)");
        }

        // A separate image-recognition model is configured — have it describe each image, then pass the
        // description as text so the (possibly text-only) conversation model can still use it.
        approval.ReportProgress("Analysing the captured image…");
        var descriptions = new List<string>();
        foreach (var image in atts)
        {
            var desc = await DescribeImageAsync(imageProvider, image, ct);
            if (!string.IsNullOrWhiteSpace(desc)) descriptions.Add(desc!.Trim());
        }

        return descriptions.Count > 0
            ? (null, "Description of the captured image (from the image-recognition model):\n\n"
                     + string.Join("\n\n", descriptions))
            : (null, "(An image was captured, but it couldn't be described.)");
    }

    /// <summary>
    /// How a recording a tool produced reaches the conversation model: as audio when it can hear, otherwise as what the
    /// Speech In model hears in it — or, with no speech model, a note that it could not be passed on.
    /// </summary>
    private async Task<(IReadOnlyList<LlmAttachment>? Attachments, string? Note)> ResolveToolAudioAsync(
        ILlmProvider convProvider, IReadOnlyList<ContextAudio> audio, IAIResponseHandler approval, CancellationToken ct)
    {
        if (convProvider.SupportsAudio)
            return ([.. audio.Select(a => new LlmAttachment(a.Label ?? "recording.wav", "audio/wav", a.Wav))], null);

        if (SpeechToText is not { } transcriber)
            return (null, "(A recording was captured, but the current model can't hear audio and no Speech In model is set up to transcribe it, so it wasn't passed on.)");

        approval.ReportProgress("Transcribing the recording…");
        var transcripts = new List<string>();
        foreach (var clip in audio)
        {
            string heard;
            try
            {
                heard = await SpeechTranscription.TranscribeAsync(transcriber, clip.Wav, ct);
                if (heard.Length == 0) heard = "(no speech was heard in it)";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                heard = $"(it couldn't be transcribed: {ex.Message})";
            }
            transcripts.Add(clip.Label is { } label ? $"{label}: {heard}" : heard);
        }
        return (null, "Transcript of the recording (the current model can't hear audio, so the Speech In model transcribed it; "
                    + "sounds other than speech are not in it):\n\n" + string.Join("\n\n", transcripts));
    }

    /// <summary>
    /// Asks the image-recognition model to describe one image. Returns null if that model can't process
    /// images or the call fails (cancellation propagates).
    /// </summary>
    private static async Task<string?> DescribeImageAsync(
        ILlmProvider imageProvider, LlmAttachment image, CancellationToken ct)
    {
        if (!imageProvider.SupportsImages) return null;

        const string system =
            "You are an image analyst. Describe the image thoroughly and factually so someone who can't see " +
            "it understands what's there: overall layout, any visible text (transcribe it), UI elements, " +
            "charts/figures, and anything notable. Be complete but concise; don't speculate beyond what's shown.";

        try
        {
            var resp = await imageProvider.CompleteAsync(
                [new(LlmRole.System, system),
                 new(LlmRole.User, "Describe this image.") { Attachments = [image] }],
                ct);
            return resp?.RawText;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static IClientTool BuildGetClientCommandsTool(IReadOnlyList<IClientTool> pageTools)
        => new DelegateClientTool(
            "get_client_commands",
            "List every client tool available here, with descriptions and parameters.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(ToolResult.Ok(
                $"{pageTools.Count} tool(s) available", Agent.ToolCatalog.Describe(pageTools))),
            parallelizable: true);

    /// <summary>
    /// What the model is told, in the order it matters: who it is and what its replies can draw; how to reply in this
    /// mode; what the user is looking at; and, for a model offered no tools of its own, how to act. Nothing it cannot use —
    /// approval is the app's business, not the
    /// model's, and what it does is shown from the calls themselves, so it is never asked to explain before acting.
    /// </summary>
    private string BuildSystemPrompt(IPageViewModel? page, string context, string? acting, string? replyStyle)
    {
        var persona = _workspace.Workspace.Persona;
        var aiName  = string.IsNullOrWhiteSpace(persona?.Name) ? "Aria" : persona!.Name!.Trim();

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(persona?.SystemPrompt))
            sb.Append(persona!.SystemPrompt!.Trim()).Append("\n\n");

        sb.Append($"You are {aiName}, the assistant built into Nexaflow, a desktop application for working with the files, ")
          .Append("documents and programs on the user's machine. Your replies are shown as Markdown, which here draws far ")
          .Append("more than text — tables, maths, diagrams, charts, codes and music among it: get_markdown_syntaxes lists ")
          .Append("what it can draw, and get_markdown_syntax shows how to write one.\n\n");

        // How to reply here — spoken, or written in a small panel — comes straight after who it is.
        if (replyStyle is { Length: > 0 }) sb.Append(replyStyle.Trim()).Append("\n\n");
        if (page is IChatEngagement eng)
            sb.Append("Your reply is shown in a compact panel beneath this page, about ").Append(eng.MaxResponseRows ?? 8)
              .Append(" lines tall: keep it short and to the point. Anything longer opens in a larger overlay, and anything ")
              .Append("involved can be carried on as a full conversation.\n\n");

        sb.Append("# What the user is looking at\n").Append(context.Trim()).Append("\n\n");

        // Page-specific guidance (what the model can usefully do on this page).
        var guidance = page?.GetAiSystemPromptGuidance();
        if (!string.IsNullOrWhiteSpace(guidance))
            sb.Append(guidance!.Trim()).Append("\n\n");

        // How to act — only for a model with no tool calling of its own; any other is offered its tools in it.
        if (acting is { Length: > 0 }) sb.Append(acting);
        return sb.ToString().TrimEnd();
    }
}
