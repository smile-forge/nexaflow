using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

using Nexaflow.Core.Models;
using Nexaflow.Core.Services;

using Nexaflow.Features.Common;

using Nexaflow.Providers.Common;
using Nexaflow.Localization;
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

/// <summary>The choice of who runs the workspace's AI: Nexaflow's own models, or a cloud provider — the two together are set
/// up later, per ability, in the workspace's AI settings.</summary>
public sealed partial class WizardHarnessContent : ObservableObject
{
    public WizardHarnessContent(string modelName, string hardwareText)
    {
        ModelName    = modelName;
        HardwareText = hardwareText;
    }

    public string ModelName    { get; }
    public string HardwareText { get; }

    /// <summary>What Nexaflow's own model does, by name.</summary>
    public string OwnedText => Str.Format("Shell.Wizard.Harness.OwnedDetailFormat", ModelName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwned), nameof(IsBridged))]
    private WorkspaceHarness _choice = WorkspaceHarness.Owned;

    public bool IsOwned   { get => Choice == WorkspaceHarness.Owned;   set { if (value) Choice = WorkspaceHarness.Owned; } }
    public bool IsBridged { get => Choice == WorkspaceHarness.Bridged; set { if (value) Choice = WorkspaceHarness.Bridged; } }
}

public sealed partial class WizardModelPickContent : ObservableObject
{
    public ObservableCollection<string> Models { get; } = [];
    [ObservableProperty] private string? _selectedModel;
    [ObservableProperty] private bool    _isLoading;
}

/// <summary>One kind of model the chosen provider offers beside language: its models, and the one chosen — none until the
/// user picks one.</summary>
public sealed partial class WizardModelKindChoice : ObservableObject
{
    /// <summary>The entry that leaves the kind's rows without a model of the provider's.</summary>
    public const string None = "None";

    public WizardModelKindChoice(ModelKind kind, string label)
    {
        Kind  = kind;
        Label = label;
    }

    public ModelKind Kind { get; }

    /// <summary>The rows it fills, as the ability grid names them.</summary>
    public string Label { get; }

    public string AutomationId => "WizardKind_" + Kind;

    public ObservableCollection<string> Models { get; } = [None];

    [ObservableProperty] private string _selectedModel = None;

    /// <summary>The model chosen; null for none.</summary>
    public string? Chosen => SelectedModel == None ? null : SelectedModel;
}

/// <summary>The provider's other kinds of model, each with its choice.</summary>
public sealed partial class WizardModelKindsContent : ObservableObject
{
    public ObservableCollection<WizardModelKindChoice> Kinds { get; } = [];
    [ObservableProperty] private bool _isLoading;
}

// ── Shared state for the first-workspace sub-flow ────────────────────────────────

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
        (!_requireAll || ConfigRequirements.IsComplete(_editor.EditingClone));

    public void OnEnter() { }

    public void Commit()
    {
        _editor.ApplyToReal();
        _commit(_editor);
    }
}

/// <summary>
/// Who runs the workspace's AI: Nexaflow's own models, or a cloud provider the user chooses. Asked only where the machine
/// can run Nexaflow's own model on a GPU; choosing it skips the provider steps and sets the grid here, and the Download models
/// step follows. The two together are set up later, per ability, in the workspace's AI settings.
/// </summary>
internal sealed class HarnessStep : ObservableObject, IWizardStep
{
    private readonly WorkspaceAiSetup _setup;
    private readonly WizardHarnessContent? _content;

    public HarnessStep(WorkspaceAiSetup setup)
    {
        _setup = setup;
        if (!setup.OffersOwned || setup.Owned is not { OnGpu: { } chat } offer) return;

        var gpu = offer.Placement.Gpus.First(g => g.Device == chat.Device);
        var context = chat.ContextTokens.ToString("N0");
        string machine = offer.SpeechInModelId is null && offer.SpeechOutModelId is null
            ? Str.Format("Shell.Wizard.Harness.MachineFormat", gpu.Name, context)
            : offer.VoiceOnGpu
                ? Str.Format("Shell.Wizard.Harness.MachineVoiceOnGpuFormat", gpu.Name, context)
                : Str.Format("Shell.Wizard.Harness.MachineVoiceOnCpuFormat", gpu.Name, context);
        if (offer.Larger.Count > 0) machine += " " + Str.Format("Shell.Wizard.Harness.LargerFormat", string.Join(", ", offer.Larger));

        _content = new WizardHarnessContent(offer.DisplayName, machine);
    }

    public string Title      => Str.Get("Shell.Wizard.Harness.Title");
    public object Content    => _content!;
    public bool   IsComplete => true;
    public bool   Applies    => _content is not null;
    public void   OnEnter() { }
    public void   Commit()  => _setup.ChooseHarness(_content!.Choice);
}

/// <summary>Pick which provider to set up first.</summary>
internal sealed class ProviderPickStep : ObservableObject, IWizardStep
{
    private readonly WorkspaceAiSetup _setup;
    private readonly WizardProviderPickContent _content = new();

    public ProviderPickStep(WorkspaceAiSetup setup)
    {
        _setup = setup;
        foreach (var name in setup.Providers
                     .Select(c => c.FriendlyName)
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            _content.Providers.Add(name);

        _content.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(WizardProviderPickContent.SelectedProvider)) return;
            _setup.ChooseProvider(_setup.Providers.FirstOrDefault(c => c.FriendlyName == _content.SelectedProvider));
            OnPropertyChanged(nameof(IsComplete));
        };
    }

    public string Title      => Str.Get("Shell.Wizard.Provider.Title");
    public bool   Applies    => _setup.Harness != WorkspaceHarness.Owned;
    public object Content    => _content;
    public bool   IsComplete => _setup.Provider is not null;
    public void   OnEnter() { }
    public void   Commit() { }
}

/// <summary>Enter the chosen provider's config (API key etc.); saving it lets the next steps list the provider's models.</summary>
internal sealed partial class ProviderConfigStep(WorkspaceAiSetup setup, IConfigRulings rulings, ConfigPageModels pages) : ObservableObject, IWizardStep
{
    [ObservableProperty] private object? _content;
    private ConfigEditViewModel? _editor;

    public string Title   => Str.Get("Shell.Wizard.ProviderConfig.Title");
    public bool   Applies => setup.Harness != WorkspaceHarness.Owned;

    public bool IsComplete => _editor is not null && ConfigRequirements.IsComplete(_editor.EditingClone);

    public void OnEnter()
    {
        if (setup.Provider is not { } provider) { Content = null; _editor = null; return; }
        _editor = new ConfigEditViewModel(provider, provider.ConfigName, provider.FriendlyName, rulings, pages);
        _editor.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsComplete));
        Content = _editor;
        OnPropertyChanged(nameof(IsComplete));
    }

    public void Commit()
    {
        if (_editor is null) return;
        _editor.ApplyToReal();
        setup.SaveProviderConfig();
    }
}

/// <summary>Pick a language model, then put it in every reasoning row of the ability grid.</summary>
internal sealed class ModelPickStep : ObservableObject, IWizardStep
{
    private readonly WorkspaceAiSetup _setup;
    private readonly WizardModelPickContent _content = new();

    public ModelPickStep(WorkspaceAiSetup setup)
    {
        _setup = setup;
        _content.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WizardModelPickContent.SelectedModel))
                OnPropertyChanged(nameof(IsComplete));
        };
    }

    public string Title      => Str.Get("Shell.Wizard.Model.Title");
    public bool   Applies    => _setup.Harness != WorkspaceHarness.Owned;
    public object Content    => _content;
    public bool   IsComplete => !string.IsNullOrEmpty(_content.SelectedModel);

    public void OnEnter()
    {
        _content.Models.Clear();
        _content.SelectedModel = null;
        _ = LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        _content.IsLoading = true;
        try
        {
            foreach (var model in await _setup.ListModelsAsync(ModelKind.Language)) _content.Models.Add(model);
            if (_content.Models.Count > 0) _content.SelectedModel = _content.Models[0];
        }
        finally { _content.IsLoading = false; }
    }

    public void Commit()
    {
        if (!string.IsNullOrEmpty(_content.SelectedModel)) _setup.UseLanguageModel(_content.SelectedModel);
    }
}

/// <summary>
/// The chosen provider's other kinds of model — speech in and out, pictures, video — as the provider lists them, one chosen
/// for each or none, and each put in the rows of the ability grid that take its kind. Applies where the provider offers
/// any beside language.
/// </summary>
internal sealed class ModelKindsStep(WorkspaceAiSetup setup) : ObservableObject, IWizardStep
{
    private readonly WizardModelKindsContent _content = new();

    public string Title      => Str.Get("Shell.Wizard.Kinds.Title");
    public bool   Applies    => setup.Harness != WorkspaceHarness.Owned && setup.KindsOffered().Count > 0;
    public object Content    => _content;
    public bool   IsComplete => !_content.IsLoading;

    public void OnEnter()
    {
        _content.Kinds.Clear();
        _ = LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        _content.IsLoading = true;
        OnPropertyChanged(nameof(IsComplete));
        try
        {
            foreach (var (kind, rows) in setup.KindsOffered())
            {
                var choice = new WizardModelKindChoice(kind, string.Join(" · ", rows.Select(AiAbilityNames.Friendly)));
                foreach (var model in await setup.ListModelsAsync(kind)) choice.Models.Add(model);
                if (choice.Models.Count > 1) _content.Kinds.Add(choice);   // a kind whose models cannot be listed is not offered
            }
        }
        finally
        {
            _content.IsLoading = false;
            OnPropertyChanged(nameof(IsComplete));
        }
    }

    public void Commit()
        => setup.UseModels([.. _content.Kinds.Where(k => k.Chosen is not null).Select(k => (k.Kind, k.Chosen!))]);
}

// ── Wizard view model ────────────────────────────────────────────────────────

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly IReadOnlyList<IWizardStep> _steps;
    /// <summary>The workspace being set up, which holds a provider set while a provider is; null where no workspace is.</summary>
    private readonly WorkspaceAiSetup? _setup;

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
    public string        NextButtonText => IsLastStep ? Str.Get("Shell.Wizard.Finish") : Str.Get("Shell.Wizard.Next");
    public int           StepNumber     => _steps.Take(CurrentIndex + 1).Count(s => s.Applies);
    public int           StepCount      => _steps.Count(s => s.Applies);
    public string        StepIndicator  => Str.Format("Shell.Wizard.StepFormat", StepNumber, StepCount);
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

    internal SetupWizardViewModel(IReadOnlyList<IWizardStep> steps, WorkspaceAiSetup? setup)
    {
        _steps = steps;
        _setup = setup;
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

    /// <summary>Lets go of what the workspace's setup holds: the provider set it lists models from.</summary>
    public void ReleaseResources() => _setup?.Dispose();

    // ── Build ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The first-run / post-update wizard, or null when there is nothing to ask: a step for each item of the
    /// <see cref="SetupAgenda"/> — what was lost, what is new, the app-wide configs to fill, the startup workspace's AI, and the models
    /// to download.
    /// </summary>
    /// <param name="startupWorkspace">The workspace the first window opens on, whose AI setup is asked for when it has none.</param>
    /// <param name="pages">The page a step's config is edited on where it has one of its own.</param>
    /// <param name="config">The session's configuration: each step's config saved back through it, and what rules on a setting shown
    /// as it rules.</param>
    /// <param name="runs">This run against the one before: a first run or an update.</param>
    /// <param name="features">The session's feature system, whose workspace-scoped configs the workspace's setup asks for.</param>
    /// <param name="providers">The session's provider pool, which the providers offered and the models on this machine are read
    /// from.</param>
    /// <param name="savedWorkspaces">The saved workspaces: each step writes back, and after an update each one's Local column is
    /// offered its models.</param>
    public static SetupWizardViewModel? Build(Workspace startupWorkspace, ConfigPageModels pages, ConfigManager config, AppRuns runs,
                                              FeatureManager features, ProviderManager providers, WorkspaceManager savedWorkspaces)
    {
        var agenda = SetupAgenda.Of(startupWorkspace, config, runs, providers, savedWorkspaces);
        var steps  = new List<IWizardStep>();

        // Lead with what was lost and what was recovered, so the steps that follow read as re-verification.
        if (agenda.Unreadable.Count > 0)
            steps.Add(new MarkdownStep(Str.Get("Shell.Wizard.Recovered.Title"), RecoveryNotes(agenda.Unreadable)));

        if (agenda.Welcomes && TryLoadWhatsNew(out var whatsNew))
            steps.Add(new MarkdownStep(Str.Get("Shell.Wizard.WhatsNew.Title"), whatsNew));

        // Each prefilled with what the config holds now: the editor binds the live config.
        foreach (var cfg in agenda.MandatoryConfigs)
            steps.Add(new ConfigEditStep(cfg.FriendlyName, new ConfigEditViewModel(cfg, cfg.ConfigName, cfg.FriendlyName, config, pages),
                                         e => config.Save(e.RealConfig, e.ConfigName)));

        var setup = agenda.SetsUpWorkspace
            ? AppendWorkspaceSetupSteps(steps, startupWorkspace, config, pages, features, providers, savedWorkspaces)
            : null;

        if (agenda.AwaitedDownloads.Count > 0)
            steps.Add(new ModelDownloadStep(() => true, agenda.AwaitedDownloads, providers));

        return steps.Count == 0 ? null : new SetupWizardViewModel(steps, setup);
    }

    /// <summary>What the first step of a recovery run says: which settings were lost, that they are on
    /// defaults, that the damaged files were kept, and that the rest of the wizard confirms what replaced
    /// them. Workspace data folders survive a lost list, so the conversations and notes are still there.</summary>
    private static string RecoveryNotes(IReadOnlyList<string> unreadable)
        => Str.Format("Shell.Wizard.Recovered.NotesFormat", string.Join(", ", unreadable.Select(n => $"`{n}`")));

    /// <summary>
    /// A wizard of ONLY the workspace-setup steps for <paramref name="workspace"/> — who runs its AI, a provider's key and models, and
    /// its configs that say <c>[MandatorySetup]</c>. Used by the Workspaces tab's "Add Workspace" to configure a freshly-created
    /// workspace; <paramref name="pages"/> make a step's own page where its config has one, <paramref name="rulings"/> rule on the
    /// settings the app does not decide alone, <paramref name="features"/> knows its per-workspace configs, <paramref name="providers"/>
    /// the providers offered, and <paramref name="savedWorkspaces"/> is written back as each step commits.
    /// </summary>
    public static SetupWizardViewModel BuildWorkspaceSetup(Workspace workspace, ConfigPageModels pages, IConfigRulings rulings, FeatureManager features, ProviderManager providers, WorkspaceManager savedWorkspaces)
    {
        var steps = new List<IWizardStep>();
        var setup = AppendWorkspaceSetupSteps(steps, workspace, rulings, pages, features, providers, savedWorkspaces);
        return new SetupWizardViewModel(steps, setup);
    }

    /// <summary>Appends the steps that set up <paramref name="workspace"/>'s AI — who runs it, the models to download, a provider and
    /// its models — and its configs that say <c>[MandatorySetup]</c>, each config's own page made by <paramref name="pages"/> where it
    /// has one and each setting shown as <paramref name="rulings"/> rule on it; returns the setup they drive.</summary>
    private static WorkspaceAiSetup AppendWorkspaceSetupSteps(List<IWizardStep> steps, Workspace workspace, IConfigRulings rulings, ConfigPageModels pages, FeatureManager features, ProviderManager providers, WorkspaceManager savedWorkspaces)
    {
        var setup = new WorkspaceAiSetup(workspace, providers, savedWorkspaces, features);

        steps.Add(new HarnessStep(setup));
        steps.Add(new ModelDownloadStep(() => setup.Harness == WorkspaceHarness.Owned, setup.Owned?.Downloads ?? [], providers));
        steps.Add(new ProviderPickStep(setup));
        steps.Add(new ProviderConfigStep(setup, rulings, pages));
        steps.Add(new ModelPickStep(setup));
        steps.Add(new ModelKindsStep(setup));

        foreach (var (config, configName, friendlyName) in setup.MandatoryConfigs())
            steps.Add(new ConfigEditStep(friendlyName, new ConfigEditViewModel(config, configName, friendlyName, rulings, pages),
                                         e => setup.Save(e.RealConfig)));

        return setup;
    }

    /// <summary>What is new, from the app's own resources: the welcome on a first run, the changes after an update.</summary>
    private static bool TryLoadWhatsNew(out string markdown)
    {
        markdown = string.Empty;
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
}
