using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;

namespace Nexaflow.Core.ViewModels;

// ── Step contract ──────────────────────────────────────────────────────────────

/// <summary>One page of the setup wizard. <see cref="Content"/> is rendered by a type-keyed
/// DataTemplate in SetupWizardWindow.</summary>
public interface IWizardStep : INotifyPropertyChanged
{
    string Title      { get; }
    object Content    { get; }
    bool   IsComplete { get; }          // gates the Next button
    void   OnEnter();                   // called when the step becomes current (lazy load)
    void   Commit();                    // persist this step on advancing past it

    /// <summary>Whether the step is asked at all, given what earlier steps chose — the provider steps are not when
    /// Nexaflow runs the workspace's AI itself. Next and Back pass over a step that does not apply.</summary>
    bool Applies => true;
}

// ── Content view models (rendered by DataTemplates) ──────────────────────────────

public sealed class WizardMarkdownContent(string markdown)
{
    public string Markdown { get; } = markdown;
}

public sealed partial class WizardProviderPickContent : ObservableObject
{
    public ObservableCollection<string> Providers { get; } = [];
    [ObservableProperty] private string? _selectedProvider;
}

/// <summary>The choice of who runs the workspace's AI, with what Nexaflow's own model costs to fetch.</summary>
public sealed partial class WizardHarnessContent : ObservableObject
{
    public WizardHarnessContent(string modelName, string downloadText, string hardwareText)
    {
        ModelName    = modelName;
        DownloadText = downloadText;
        HardwareText = hardwareText;
    }

    public string ModelName    { get; }
    public string DownloadText { get; }
    public string HardwareText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwned), nameof(IsBridged), nameof(IsMixed))]
    private WorkspaceHarness _choice = WorkspaceHarness.Mixed;

    public bool IsOwned   { get => Choice == WorkspaceHarness.Owned;   set { if (value) Choice = WorkspaceHarness.Owned; } }
    public bool IsBridged { get => Choice == WorkspaceHarness.Bridged; set { if (value) Choice = WorkspaceHarness.Bridged; } }
    public bool IsMixed   { get => Choice == WorkspaceHarness.Mixed;   set { if (value) Choice = WorkspaceHarness.Mixed; } }
}

public sealed partial class WizardModelPickContent : ObservableObject
{
    public ObservableCollection<string> Models { get; } = [];
    [ObservableProperty] private string? _selectedModel;
    [ObservableProperty] private bool    _isLoading;
}

// ── Shared state for the first-workspace sub-flow ────────────────────────────────

internal sealed class WorkspaceSetupContext
{
    public required Workspace Workspace;
    public IReadOnlyList<IProviderConfig> AvailableProviders = [];
    public IProviderConfig? ChosenProvider;
    public ProviderSet?     ProviderSet;   // acquired after the provider config is saved (for model listing)

    /// <summary>Who runs the workspace's AI; bridged unless the machine can run Nexaflow's own model and the user chose it.</summary>
    public WorkspaceHarness Harness = WorkspaceHarness.Bridged;

    /// <summary>Nexaflow's own model, when the machine can run one.</summary>
    public OwnedModelOffer? Owned;
}

/// <summary>The model Nexaflow runs itself that a new workspace is offered: what its column is, and what it costs to fetch.</summary>
/// <param name="Placement">Where its models would run on this machine, as its GPUs are expected before the host has run.</param>
/// <param name="VoiceOnGpu">Every voice model has room on a GPU beside it.</param>
/// <param name="Larger">The other language models of Nexaflow's own this machine fits, by name.</param>
internal sealed record OwnedModelOffer(string ProviderName, string ModelId, string DisplayName, string AssemblyFileName, long DownloadBytes,
                                       string? SpeechInModelId, string? SpeechOutModelId, Placement Placement, bool VoiceOnGpu,
                                       IReadOnlyList<string> Larger)
{
/// <summary>The columns a workspace that uses Nexaflow's own models starts with.</summary>
    public OwnedColumns Columns() => new(
        Column(ModelId, ModelKind.Language),
        SpeechInModelId is null ? null : Column(SpeechInModelId, ModelKind.SpeechToText),
        SpeechOutModelId is null ? null : Column(SpeechOutModelId, ModelKind.TextToSpeech));

    private ProviderModelPair Column(string model, ModelKind kind)
        => new() { ProviderName = ProviderName, Model = model, Kind = kind, AssemblyFileName = AssemblyFileName };

/// <summary>Its language model as the setup would place it on a GPU; null where none has room for it.</summary>
    public ModelSpec? OnGpu => Placement.Placed.FirstOrDefault(s => s.Id.StartsWith(ModelId + "/", StringComparison.Ordinal) && s.Device is not null);

/// <summary>The owned language provider's first model, with its speech models, their files and — where the machine
/// suits it — the CUDA pack to fetch, and where they would run here; null when no provider is Nexaflow's own harness.</summary>
    public static OwnedModelOffer? Find(ProviderSet providers, ComputeHardwareReport hardware)
    {
        var provider = providers.Providers.FirstOrDefault(p => p.Harness == ModelHarness.Owned && p.Serves(ModelKind.Language));
        if (provider is not ILocalModelCatalog { Catalog: [var model, ..] } catalog) return null;

        IAiProvider? Serving(ModelKind kind)
            => providers.Providers.FirstOrDefault(p => p.Harness == ModelHarness.Owned && p.Name == provider.Name && p.Serves(kind));
        CatalogModel? First(ModelKind kind) => Serving(kind) is ILocalModelCatalog { Catalog: [var first, ..] } ? first : null;
        var speechIn  = First(ModelKind.SpeechToText);
        var speechOut = First(ModelKind.TextToSpeech);
        long bytes = new[] { model, speechIn, speechOut }.OfType<CatalogModel>().Sum(m => m.Files.Sum(f => f.Size))
                   + (CudaRuntimePack.Applies(hardware) ? CudaRuntimePack.Size : 0);

        static IReadOnlyList<PlacementNeed> NeedsOf(IAiProvider? served, CatalogModel? of)
            => served is IHostModelCatalog host && of is not null ? host.NeedsOf(of.Id) : [];
        var voice = NeedsOf(Serving(ModelKind.SpeechToText), speechIn).Concat(NeedsOf(Serving(ModelKind.TextToSpeech), speechOut)).ToList();
        var placement = ModelPlacement.Plan(ModelPlacement.Expected(hardware), [.. NeedsOf(provider, model), .. voice]);

        return new OwnedModelOffer(provider.Name, model.Id, model.DisplayName, providers.GetAssemblyFileName(provider.Name), bytes,
                                   speechIn?.Id, speechOut?.Id, placement,
                                   VoiceOnGpu: voice.All(n => n.CpuOnly || placement.Of(n.Id)?.Device is not null),
                                   Larger: [.. catalog.Catalog.Skip(1).Select(m => m.DisplayName)]);
    }
}

// ── Steps ────────────────────────────────────────────────────────────────────

/// <summary>A read-only markdown page (What's New). Always complete.</summary>
internal sealed class MarkdownStep(string title, string markdown) : ObservableObject, IWizardStep
{
    public string Title      => title;
    public object Content    { get; } = new WizardMarkdownContent(markdown);
    public bool   IsComplete => true;
    public void   OnEnter() { }
    public void   Commit()  { }
}

/// <summary>Edits one config via the shared <see cref="ConfigEditViewModel"/>. Completes when the
/// edits are valid (and, when <paramref name="requireAll"/>, all required fields are filled).</summary>
internal sealed class ConfigEditStep : ObservableObject, IWizardStep
{
    private readonly ConfigEditViewModel _editor;
    private readonly Action<ConfigEditViewModel> _commit;
    private readonly bool _requireAll;

    public ConfigEditStep(string title, ConfigEditViewModel editor,
                          Action<ConfigEditViewModel> commit, bool requireAll = false)
    {
        Title    = title;
        _editor  = editor;
        _commit  = commit;
        _requireAll = requireAll;
        _editor.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsComplete));
    }

    public string Title   { get; }
    public object Content => _editor;

    public bool IsComplete => _editor.IsValid &&
        (!_requireAll || ConfigEditViewModel.AreRequiredPropertiesSatisfied(_editor.EditingClone));

    public void OnEnter() { }

    public void Commit()
    {
        _editor.ApplyToReal();
        _commit(_editor);
    }
}

/// <summary>
/// Who runs the workspace's AI: Nexaflow itself, a provider the user chooses, or both. Asked only where the machine
/// can run Nexaflow's own model; choosing Nexaflow alone skips the provider steps and sets the grid here.
/// </summary>
internal sealed class HarnessStep : ObservableObject, IWizardStep
{
    private readonly WorkspaceSetupContext _ctx;
    private readonly WizardHarnessContent? _content;

    public HarnessStep(WorkspaceSetupContext ctx)
    {
        _ctx = ctx;
        // Offered where Nexaflow's own model has a GPU to run on: on the CPU alone it answers too slowly to be the choice.
        if (ctx.Owned is not { OnGpu: { } chat } offer) return;

        var gpu = offer.Placement.Gpus.First(g => g.Device == chat.Device);
        string machine = $"This machine runs it on its {gpu.Name}, with a context of {chat.ContextTokens:N0} tokens"
                       + (offer.SpeechInModelId is null && offer.SpeechOutModelId is null ? "."
                          : offer.VoiceOnGpu ? ", and the voice models beside it."
                          : "; the voice models that do not fit beside it run on the CPU, more slowly.");
        if (offer.Larger.Count > 0) machine += $" Larger models it fits can be chosen in AI settings: {string.Join(", ", offer.Larger)}.";

        _content = new WizardHarnessContent(offer.DisplayName,
            $"{offer.DisplayName} and what it needs to run on this machine: {offer.DownloadBytes / 1e9:0.0} GB, downloaded in the background once you finish. Until it is here, Nexaflow's own model is not available.",
            machine);
    }

    public string Title      => "Who runs the AI";
    public object Content    => _content!;
    public bool   IsComplete => true;
    public bool   Applies    => _content is not null;
    public void   OnEnter() { }

    public void Commit()
    {
        _ctx.Harness = _content!.Choice;
        if (_ctx.Harness != WorkspaceHarness.Owned) return;

        WorkspaceAiDefaults.Assign(_ctx.Workspace.AiConfig, bridged: null, _ctx.Owned!.Columns(),
                                   LegacyVoiceSettings.WhisperModel(ConfigManager.Instance.BaseDir));
        WorkspaceManager.Instance.SaveWorkspaceAiConfig(_ctx.Workspace);
        WorkspaceManager.Instance.SaveWorkspaces();
    }
}

/// <summary>Pick which provider to set up first.</summary>
internal sealed class ProviderPickStep : ObservableObject, IWizardStep
{
    private readonly WorkspaceSetupContext _ctx;
    private readonly WizardProviderPickContent _content = new();

    public ProviderPickStep(WorkspaceSetupContext ctx)
    {
        _ctx = ctx;
        foreach (var name in ctx.AvailableProviders
                     .Select(c => c.FriendlyName)
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            _content.Providers.Add(name);

        _content.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(WizardProviderPickContent.SelectedProvider)) return;
            _ctx.ChosenProvider = _ctx.AvailableProviders
                .FirstOrDefault(c => c.FriendlyName == _content.SelectedProvider);
            OnPropertyChanged(nameof(IsComplete));
        };
    }

    public string Title      => "Choose a provider";
    public bool   Applies    => _ctx.Harness != WorkspaceHarness.Owned;
    public object Content     => _content;
    public bool   IsComplete  => _ctx.ChosenProvider is not null;
    public void   OnEnter() { }
    public void   Commit() { }
}

/// <summary>Enter the chosen provider's config (API key etc.), then acquire a provider set so the
/// next step can list models.</summary>
internal sealed partial class ProviderConfigStep : ObservableObject, IWizardStep
{
    private readonly WorkspaceSetupContext _ctx;
    [ObservableProperty] private object? _content;
    private ConfigEditViewModel? _editor;

    public ProviderConfigStep(WorkspaceSetupContext ctx) => _ctx = ctx;

    public string Title => "Provider settings";
    public bool Applies => _ctx.Harness != WorkspaceHarness.Owned;

    public bool IsComplete =>
        _editor is not null && ConfigEditViewModel.AreRequiredPropertiesSatisfied(_editor.EditingClone);

    public void OnEnter()
    {
        if (_ctx.ChosenProvider is null) { Content = null; _editor = null; return; }
        _editor = new ConfigEditViewModel(_ctx.ChosenProvider, _ctx.ChosenProvider.ConfigName, _ctx.ChosenProvider.FriendlyName);
        _editor.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsComplete));
        Content = _editor;
        OnPropertyChanged(nameof(IsComplete));
    }

    public void Commit()
    {
        if (_editor is null || _ctx.ChosenProvider is null) return;
        _editor.ApplyToReal();
        ConfigManager.Instance.SaveTo(_ctx.Workspace.Dir, _ctx.ChosenProvider, _ctx.ChosenProvider.ConfigName);

        // (Re)acquire a provider set from the now-saved configs so the model step can enumerate models.
        if (_ctx.ProviderSet is not null) ProviderManager.Instance.ReleaseProviderSet(_ctx.ProviderSet);
        _ctx.ProviderSet = ProviderManager.Instance.AcquireProviderSet(_ctx.Workspace.ProviderConfigs, []);
    }
}

/// <summary>Pick a model, then assign it to every AI ability.</summary>
internal sealed class ModelPickStep : ObservableObject, IWizardStep
{
    private readonly WorkspaceSetupContext _ctx;
    private readonly WizardModelPickContent _content = new();
    private string? _providerName;   // resolved ILlmProvider.Name (the key columns resolve against)

    public ModelPickStep(WorkspaceSetupContext ctx)
    {
        _ctx = ctx;
        _content.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WizardModelPickContent.SelectedModel))
                OnPropertyChanged(nameof(IsComplete));
        };
    }

    public string Title      => "Choose a model";
    public bool   Applies    => _ctx.Harness != WorkspaceHarness.Owned;
    public object Content     => _content;
    public bool   IsComplete  => !string.IsNullOrEmpty(_content.SelectedModel);

    public void OnEnter()
    {
        _content.Models.Clear();
        _content.SelectedModel = null;
        _ = LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        var name = _ctx.ChosenProvider?.FriendlyName;
        if (name is null || _ctx.ProviderSet?.Find(name, ModelKind.Language) is not { } provider)
            return;

        // The provider's own Name is exactly the key the ability grid / AcquireProviderSet resolve
        // columns against — capture it so the saved column matches (FriendlyName usually equals it).
        _providerName = provider.Name;

        _content.IsLoading = true;
        try
        {
            var models = await provider.GetAvailableModelsAsync();
            foreach (var m in models) _content.Models.Add(m);
            if (_content.Models.Count > 0) _content.SelectedModel = _content.Models[0];
        }
        catch { }
        finally { _content.IsLoading = false; }
    }

    public void Commit()
    {
        if (_ctx.ChosenProvider is null || string.IsNullOrEmpty(_content.SelectedModel)) return;

        var name = _providerName ?? _ctx.ChosenProvider.FriendlyName;
        var pair = new ProviderModelPair
        {
            ProviderName     = name,
            Model            = _content.SelectedModel!,
            AssemblyFileName = _ctx.ProviderSet?.GetAssemblyFileName(name) ?? string.Empty,
        };

        // The chosen language model serves every reasoning role — alongside Nexaflow's own when the workspace mixes
        // both; abilities of other kinds need models of their own.
        WorkspaceAiDefaults.Assign(_ctx.Workspace.AiConfig, pair, _ctx.Harness == WorkspaceHarness.Mixed ? _ctx.Owned?.Columns() : null,
                                   LegacyVoiceSettings.WhisperModel(ConfigManager.Instance.BaseDir));

        WorkspaceManager.Instance.SaveWorkspaceAiConfig(_ctx.Workspace);
        WorkspaceManager.Instance.SaveWorkspaces();
    }
}

// ── Wizard view model ────────────────────────────────────────────────────────

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly IReadOnlyList<IWizardStep> _steps;
    private readonly WorkspaceSetupContext? _ctx;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(StepNumber))]
    [NotifyPropertyChangedFor(nameof(StepIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowBack))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private int _currentIndex;

    /// <summary>Raised when the user finishes the wizard or skips it — App then launches the window.</summary>
    public event EventHandler? Finished;

    public IWizardStep   CurrentStep    => _steps[CurrentIndex];
    public bool          IsLastStep     => NextApplying(CurrentIndex) is null;
    public string        NextButtonText => IsLastStep ? "Finish" : "Next";
    public int           StepNumber     => _steps.Take(CurrentIndex + 1).Count(s => s.Applies);
    public int           StepCount      => _steps.Count(s => s.Applies);
    public string        StepIndicator  => $"Step {StepNumber} of {StepCount}";
    public bool          ShowBack       => PreviousApplying(CurrentIndex) is not null;

    private int? NextApplying(int from)
    {
        for (int i = from + 1; i < _steps.Count; i++) if (_steps[i].Applies) return i;
        return null;
    }

    private int? PreviousApplying(int from)
    {
        for (int i = from - 1; i >= 0; i--) if (_steps[i].Applies) return i;
        return null;
    }

    internal SetupWizardViewModel(IReadOnlyList<IWizardStep> steps, WorkspaceSetupContext? ctx)
    {
        _steps = steps;
        _ctx   = ctx;
        _currentIndex = NextApplying(-1) ?? 0;
        CurrentStep.PropertyChanged += OnStepPropertyChanged;
        CurrentStep.OnEnter();
    }

    private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IWizardStep.IsComplete))
            NextCommand.NotifyCanExecuteChanged();
        if (e.PropertyName == nameof(IWizardStep.Content))
            OnPropertyChanged(nameof(CurrentStep));   // ProviderConfigStep builds Content on enter
    }

    partial void OnCurrentIndexChanging(int oldValue, int newValue)
    {
        if (oldValue >= 0 && oldValue < _steps.Count)
            _steps[oldValue].PropertyChanged -= OnStepPropertyChanged;
    }

    partial void OnCurrentIndexChanged(int value)
    {
        CurrentStep.PropertyChanged += OnStepPropertyChanged;
        CurrentStep.OnEnter();
        NextCommand.NotifyCanExecuteChanged();
    }

    private bool CanGoNext() => CurrentStep.IsComplete;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        CurrentStep.Commit();
        if (NextApplying(CurrentIndex) is not { } next) { Complete(); return; }
        CurrentIndex = next;
    }

    private bool CanGoBack() => PreviousApplying(CurrentIndex) is not null;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentIndex = PreviousApplying(CurrentIndex)!.Value;

    /// <summary>Skip the rest of the wizard and launch with whatever defaults exist (no dead-end).</summary>
    [RelayCommand]
    private void Skip() => Complete();

    private void Complete()
    {
        ReleaseResources();
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Releases the temporary provider set acquired during the workspace sub-flow.</summary>
    public void ReleaseResources()
    {
        if (_ctx?.ProviderSet is not null)
        {
            ProviderManager.Instance.ReleaseProviderSet(_ctx.ProviderSet);
            _ctx.ProviderSet = null;
        }
    }

    // ── Build ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The first-run / post-update wizard, or null when there is nothing to ask. Steps are added only where
    /// information is genuinely missing — see each numbered block — with one exception: a config that could
    /// not be read at all takes the wizard through the workspace's AI setup again, because what that file
    /// held is gone and the user is being asked to confirm what replaced it.
    /// </summary>
    public static SetupWizardViewModel? Build(Workspace startupWorkspace)
    {
        var steps = new List<IWizardStep>();

        // A config whose file was present but unreadable: its settings are back at their defaults. Lead with
        // what happened and what was recovered, so the steps that follow read as re-verification.
        var unreadable = ConfigManager.Instance.GetUnreadableConfigs();
        if (unreadable.Count > 0)
            steps.Add(new MarkdownStep("Recovered", RecoveryNotes(unreadable)));

        // 1. What's New — only after a version bump (not on a brand-new install).
        if (TryLoadWhatsNew(out var whatsNew))
            steps.Add(new MarkdownStep("What's New", whatsNew));

        // 2. GLOBAL feature configs that opt into the wizard via [MandatorySetup], excluding the
        //    Workspaces list. Show one when it is brand-new (defaulted — first run / new feature, which
        //    an unreadable config is also listed as) OR it was migrated from an older version but is now
        //    missing genuinely-new required info; a migrated config whose required fields all carried over
        //    is left alone (no re-ask). The step is prefilled with the migrated values (the editor binds
        //    the live config).
        var defaulted = ConfigManager.Instance.GetDefaultedConfigs()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var migrated = ConfigManager.Instance.GetMigratedConfigs()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var cfg in ConfigManager.Instance.GetAll().OfType<IFeatureConfig>())
        {
            if (cfg is WorkspacesConfig || !IsMandatory(cfg)) continue;
            bool isNew        = defaulted.Contains(cfg.ConfigName);
            bool needsNewInfo = migrated.Contains(cfg.ConfigName)
                                && !ConfigEditViewModel.AreRequiredPropertiesSatisfied(cfg);
            if (!isNew && !needsNewInfo) continue;
            var editor = new ConfigEditViewModel(cfg, cfg.ConfigName, cfg.FriendlyName);
            steps.Add(new ConfigEditStep(cfg.FriendlyName, editor,
                e => ConfigManager.Instance.Save(e.RealConfig, e.ConfigName)));
        }

        // 3. First-workspace setup, if the startup workspace has no usable AI assignment — or if anything
        //    was unreadable, in which case the assignment that IS there is exactly what needs confirming.
        startupWorkspace.EnsureSharedServicesLoaded();
        WorkspaceSetupContext? ctx = null;
        if (unreadable.Count > 0 || !IsWorkspaceConfigured(startupWorkspace))
            ctx = AppendWorkspaceSetupSteps(steps, startupWorkspace);

        return steps.Count == 0 ? null : new SetupWizardViewModel(steps, ctx);
    }

    /// <summary>What the first step of a recovery run says: which settings were lost, that they are on
    /// defaults, that the damaged files were kept, and that the rest of the wizard confirms what replaced
    /// them. Workspace data folders survive a lost list, so the conversations and notes are still there.</summary>
    private static string RecoveryNotes(IReadOnlyList<string> unreadable)
        => $"""
            ## Some settings could not be read

            Nexaflow could not read {string.Join(", ", unreadable.Select(n => $"`{n}`"))} this time, so
            those settings are back at their defaults. Each damaged file has been kept beside its folder
            under `%APPDATA%\Smile\nexaflow`, and the details are in the crash log, so nothing has been
            thrown away.

            Your workspaces' own data — conversations, notes, ribbon layouts and provider settings — lives
            in its own folder per workspace and was not affected. A workspace whose entry was lost has been
            rebuilt from that folder, so it is back with its contents intact; its symbol and colour were
            stored in the file that was lost, so those have been picked afresh.

            The remaining steps let you confirm what replaced the settings that were lost. You can skip
            them and set everything up later in Options.
            """;

    /// <summary>
    /// Builds a wizard containing ONLY the workspace-setup steps (provider → key → model, plus any
    /// [MandatorySetup] persona / per-workspace configs) for <paramref name="workspace"/>. Used by the
    /// Workspaces tab's "Add Workspace" to configure a freshly-created workspace.
    /// </summary>
    public static SetupWizardViewModel BuildWorkspaceSetup(Workspace workspace)
    {
        workspace.EnsureSharedServicesLoaded();
        var steps = new List<IWizardStep>();
        var ctx   = AppendWorkspaceSetupSteps(steps, workspace);
        return new SetupWizardViewModel(steps, ctx);
    }

    /// <summary>Appends provider-pick → provider-config → model-pick (+ mandatory persona/scoped
    /// configs) for <paramref name="workspace"/>; returns the shared setup context.</summary>
    private static WorkspaceSetupContext AppendWorkspaceSetupSteps(List<IWizardStep> steps, Workspace workspace)
    {
        ProviderManager.Instance.DiscoverAll();
        workspace.ReloadProviderConfigs();
        // Materialize every workspace-scoped config so a [MandatorySetup] one (e.g. Projects) gets a wizard
        // step. WorkspaceConfigs otherwise returns only configs already lazily materialized, so on first run
        // the scoped Projects config is absent and its step silently never shows.
        workspace.MaterializeAllScopedConfigs();

        var ctx = new WorkspaceSetupContext
        {
            Workspace            = workspace,
            AvailableProviders = workspace.ProviderConfigs,
        };

        // Who runs the AI — asked only where this machine can run Nexaflow's own model. A provider set is acquired
        // just long enough to find it among the providers by its harness.
        var hardware = ProviderManager.Instance.Service<ComputeHardwareReport>() ?? ComputeHardware.Probe();
        var offers = ProviderManager.Instance.AcquireProviderSet(workspace.ProviderConfigs, []);
        try { ctx.Owned = OwnedModelOffer.Find(offers, hardware); }
        finally { ProviderManager.Instance.ReleaseProviderSet(offers); }
        steps.Add(new HarnessStep(ctx));

        // Essential AI bootstrap — unless Nexaflow runs the workspace's AI itself.
        steps.Add(new ProviderPickStep(ctx));
        steps.Add(new ProviderConfigStep(ctx));
        steps.Add(new ModelPickStep(ctx));

        // Persona + per-workspace feature configs only when they opt in via [MandatorySetup].
        var persona = workspace.Persona;
        if (IsMandatory(persona))
            steps.Add(new ConfigEditStep(persona.FriendlyName,
                new ConfigEditViewModel(persona, persona.ConfigName, persona.FriendlyName),
                _ => WorkspaceManager.Instance.SaveWorkspacePersona(workspace)));

        foreach (var wc in workspace.WorkspaceConfigs)
            if (IsMandatory(wc))
                steps.Add(new ConfigEditStep(wc.FriendlyName,
                    new ConfigEditViewModel(wc, wc.ConfigName, wc.FriendlyName),
                    e => WorkspaceManager.Instance.SaveWorkspaceScopedConfig(workspace, (IFeatureConfig)e.RealConfig)));

        return ctx;
    }

    /// <summary>True when the workspace has a language ability assigned to a provider that is ready: one whose required
    /// config (e.g. API key) is satisfied, or one that takes no config at all — Nexaflow's own model.</summary>
    public static bool IsWorkspaceConfigured(Workspace p)
    {
        var ai = p.AiConfig;
        var assignedColumnIds = ai.AssignedColumnIds();
        if (assignedColumnIds.Count == 0) return false;
        var byName = p.ProviderConfigs.ToDictionary(c => c.FriendlyName);
        // Configured means a language model is ready to answer; a speech model alone does not make one.
        return ai.Columns
            .Where(c => c.Kind == ModelKind.Language && assignedColumnIds.Contains(c.Id))
            .Any(c => byName.TryGetValue(c.ProviderName, out var cfg)
                      ? ConfigEditViewModel.AreRequiredPropertiesSatisfied(cfg)
                      : ProviderManager.Instance.TakesNoConfig(c.AssemblyFileName));
    }

    private static bool TryLoadWhatsNew(out string markdown)
    {
        markdown = string.Empty;

        // Show on a fresh install (welcome) and after a version bump (changelog); skip on an
        // unchanged relaunch.
        var shell    = ConfigManager.Instance.GetAll().OfType<ShellConfig>().FirstOrDefault();
        var current  = CurrentVersion();
        var firstRun = ConfigManager.Instance.IsFirstRun;
        var updated  = shell?.LastRunVersion is { } last && last != current;
        if (!firstRun && !updated) return false;

        try
        {
            var info = Application.GetResourceStream(new Uri("Assets/WhatsNew.md", UriKind.Relative));
            if (info is null) return false;
            using var reader = new StreamReader(info.Stream);
            markdown = reader.ReadToEnd();
        }
        catch { return false; }

        return !string.IsNullOrWhiteSpace(markdown);
    }

    /// <summary>True when a config class opts into the wizard via [MandatorySetup] (feature or provider side).</summary>
    private static bool IsMandatory(object config)
        => config.GetType().GetCustomAttribute<Nexaflow.Features.Common.MandatorySetupAttribute>() is not null
        || config.GetType().GetCustomAttribute<Nexaflow.Providers.Common.MandatorySetupAttribute>() is not null;

    /// <summary>The Core assembly version (matches the version ConfigManager stamps on config files).</summary>
    public static string CurrentVersion()
        => (typeof(ShellConfig).Assembly.GetName().Version ?? new Version(0, 0, 0, 0)).ToString();
}
