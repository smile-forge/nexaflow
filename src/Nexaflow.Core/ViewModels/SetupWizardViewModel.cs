using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
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

public sealed partial class WizardModelPickContent : ObservableObject
{
    public ObservableCollection<string> Models { get; } = [];
    [ObservableProperty] private string? _selectedModel;
    [ObservableProperty] private bool    _isLoading;
}

// ── Shared state for the first-workspace sub-flow ────────────────────────────────

internal sealed class WorkspaceSetupContext
{
    public required Profile Profile;
    public IReadOnlyList<IProviderConfig> AvailableProviders = [];
    public IProviderConfig? ChosenProvider;
    public ProviderSet?     ProviderSet;   // acquired after the provider config is saved (for model listing)
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
        ConfigManager.Instance.SaveTo(_ctx.Profile.Dir, _ctx.ChosenProvider, _ctx.ChosenProvider.ConfigName);

        // (Re)acquire a provider set from the now-saved configs so the model step can enumerate models.
        if (_ctx.ProviderSet is not null) ProviderManager.Instance.ReleaseProviderSet(_ctx.ProviderSet);
        _ctx.ProviderSet = ProviderManager.Instance.AcquireProviderSet(_ctx.Profile.ProviderConfigs, []);
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
        if (name is null || _ctx.ProviderSet is null
            || !_ctx.ProviderSet.Providers.TryGetValue(name, out var provider))
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

        _ctx.Profile.AiConfig.Columns = [pair];
        _ctx.Profile.AiConfig.Assignments.Clear();
        foreach (AiAbility ability in Enum.GetValues<AiAbility>())
            _ctx.Profile.AiConfig.Assignments[ability.ToString()] = pair.Id;

        WorkspaceManager.Instance.SaveProfileAiConfig(_ctx.Profile);
        WorkspaceManager.Instance.SaveProfiles();
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
    public bool          IsLastStep     => CurrentIndex == _steps.Count - 1;
    public string        NextButtonText => IsLastStep ? "Finish" : "Next";
    public int           StepNumber     => CurrentIndex + 1;
    public int           StepCount      => _steps.Count;
    public string        StepIndicator  => $"Step {StepNumber} of {StepCount}";
    public bool          ShowBack       => CurrentIndex > 0;

    private SetupWizardViewModel(IReadOnlyList<IWizardStep> steps, WorkspaceSetupContext? ctx)
    {
        _steps = steps;
        _ctx   = ctx;
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
        if (IsLastStep) { Complete(); return; }
        CurrentIndex++;
    }

    private bool CanGoBack() => CurrentIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentIndex--;

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
    /// Builds the wizard for <paramref name="startupProfile"/>, or returns null when nothing needs
    /// configuring (App then launches the window directly). Steps: optional What's-New (after an
    /// update) → each unconfigured GLOBAL feature config → first-workspace setup (if the profile has
    /// no usable AI assignment).
    /// </summary>
    public static SetupWizardViewModel? Build(Profile startupProfile)
    {
        var steps = new List<IWizardStep>();

        // 1. What's New — only after a version bump (not on a brand-new install).
        if (TryLoadWhatsNew(out var whatsNew))
            steps.Add(new MarkdownStep("What's New", whatsNew));

        // 2. Unconfigured GLOBAL feature configs (version-defaulted) that opt into the wizard via
        //    [MandatorySetup], excluding the Workspaces list. Everything else is set up in Options.
        var defaulted = ConfigManager.Instance.GetDefaultedConfigs()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var cfg in ConfigManager.Instance.GetAll().OfType<IFeatureConfig>())
        {
            if (cfg is WorkspacesConfig || !defaulted.Contains(cfg.ConfigName) || !IsMandatory(cfg)) continue;
            var editor = new ConfigEditViewModel(cfg, cfg.ConfigName, cfg.FriendlyName);
            steps.Add(new ConfigEditStep(cfg.FriendlyName, editor,
                e => ConfigManager.Instance.Save(e.RealConfig, e.ConfigName)));
        }

        // 3. First-workspace setup, only if the startup profile has no usable AI assignment.
        startupProfile.EnsureSharedServicesLoaded();
        WorkspaceSetupContext? ctx = null;
        if (!IsWorkspaceConfigured(startupProfile))
            ctx = AppendWorkspaceSetupSteps(steps, startupProfile);

        return steps.Count == 0 ? null : new SetupWizardViewModel(steps, ctx);
    }

    /// <summary>
    /// Builds a wizard containing ONLY the workspace-setup steps (provider → key → model, plus any
    /// [MandatorySetup] persona / per-workspace configs) for <paramref name="profile"/>. Used by the
    /// Workspaces tab's "Add Workspace" to configure a freshly-created workspace.
    /// </summary>
    public static SetupWizardViewModel BuildWorkspaceSetup(Profile profile)
    {
        profile.EnsureSharedServicesLoaded();
        var steps = new List<IWizardStep>();
        var ctx   = AppendWorkspaceSetupSteps(steps, profile);
        return new SetupWizardViewModel(steps, ctx);
    }

    /// <summary>Appends provider-pick → provider-config → model-pick (+ mandatory persona/scoped
    /// configs) for <paramref name="profile"/>; returns the shared setup context.</summary>
    private static WorkspaceSetupContext AppendWorkspaceSetupSteps(List<IWizardStep> steps, Profile profile)
    {
        ProviderManager.Instance.DiscoverAll();
        profile.ReloadProviderConfigs();

        var ctx = new WorkspaceSetupContext
        {
            Profile            = profile,
            AvailableProviders = profile.ProviderConfigs,
        };

        // Essential AI bootstrap — always runs.
        steps.Add(new ProviderPickStep(ctx));
        steps.Add(new ProviderConfigStep(ctx));
        steps.Add(new ModelPickStep(ctx));

        // Persona + per-workspace feature configs only when they opt in via [MandatorySetup].
        var persona = profile.Persona;
        if (IsMandatory(persona))
            steps.Add(new ConfigEditStep(persona.FriendlyName,
                new ConfigEditViewModel(persona, persona.ConfigName, persona.FriendlyName),
                _ => WorkspaceManager.Instance.SaveProfilePersona(profile)));

        foreach (var wc in profile.WorkspaceConfigs)
            if (IsMandatory(wc))
                steps.Add(new ConfigEditStep(wc.FriendlyName,
                    new ConfigEditViewModel(wc, wc.ConfigName, wc.FriendlyName),
                    e => WorkspaceManager.Instance.SaveProfileWorkspaceConfig(profile, (IFeatureConfig)e.RealConfig)));

        return ctx;
    }

    /// <summary>True when the profile has at least one ability assigned to a provider whose required
    /// config (e.g. API key) is satisfied.</summary>
    public static bool IsWorkspaceConfigured(Profile p)
    {
        var ai = p.AiConfig;
        if (ai.Assignments.Values.All(string.IsNullOrEmpty)) return false;

        var assignedColumnIds = ai.Assignments.Values.Where(v => !string.IsNullOrEmpty(v)).ToHashSet();
        var byName = p.ProviderConfigs.ToDictionary(c => c.FriendlyName);
        return ai.Columns
            .Where(c => assignedColumnIds.Contains(c.Id))
            .Any(c => byName.TryGetValue(c.ProviderName, out var cfg)
                      && ConfigEditViewModel.AreRequiredPropertiesSatisfied(cfg));
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
