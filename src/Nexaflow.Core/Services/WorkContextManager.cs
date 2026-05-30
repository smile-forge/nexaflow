using Nexaflow.Core.Models;
using System.Collections.ObjectModel;
using System.IO;

namespace Nexaflow.Core.Services;

public sealed class WorkContextManager
{
    public static WorkContextManager Instance { get; } = new();

    public ObservableCollection<WorkContext> Contexts { get; } = [];

    /// <summary>
    /// Fired after <see cref="Initialize"/> completes — lets ShellViewModels refresh their
    /// <c>CurrentWorkContext</c> reference when the context list is rebuilt from the Options panel.
    /// </summary>
    public event EventHandler? ContextsRefreshed;

    private WorkContextsConfig? _config;

    private WorkContextManager() { }

    /// <summary>
    /// Rebuilds the Contexts collection from <paramref name="config"/>, creating a new
    /// <see cref="AIService"/> per context and registering all currently loaded providers.
    /// Existing <see cref="ShellServices"/> instances are preserved (they hold live window/tab state).
    /// Must be called AFTER <see cref="ProviderManager"/> has loaded its assemblies.
    /// </summary>
    public void Initialize(WorkContextsConfig config)
    {
        _config = config;
        Contexts.Clear();

        var contexts = config.Contexts is { Count: > 0 } saved ? saved : [new WorkContext()];
        foreach (var ctx in contexts)
        {
            BootstrapServices(ctx);
            Contexts.Add(ctx);
        }

        ContextsRefreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a new <see cref="WorkContext"/> with a fresh <see cref="AIService"/> and
    /// <see cref="ShellServices"/>, builds its per-context provider set, and adds it
    /// to <see cref="Contexts"/>.
    /// </summary>
    public WorkContext Create(string name)
    {
        var ctx = new WorkContext { Name = name };
        BootstrapServices(ctx);
        Contexts.Add(ctx);
        return ctx;
    }

    /// <summary>
    /// Creates a new context whose AI ability config and provider configs (API keys etc.) are
    /// copied from <paramref name="source"/>, with a randomised icon/colour so it is visually
    /// distinct. The copied configs are written to the new context's own folder, then read back
    /// when its services are bootstrapped.
    /// </summary>
    public WorkContext Clone(WorkContext source, string name)
    {
        var (icon, color) = WorkContextStyle.Random();
        var ctx = new WorkContext { Name = name, Icon = icon, Color = color };
        var dir = ContextDir(name);

        // Persist the source's current configs into the clone's folder so BootstrapServices reads them.
        ConfigManager.Instance.SaveTo(dir, source.AiConfig, source.AiConfig.ConfigName);
        foreach (var cfg in source.Providers?.Configs ?? [])
            ConfigManager.Instance.SaveTo(dir, cfg, cfg.ConfigName);

        BootstrapServices(ctx);
        Contexts.Add(ctx);
        return ctx;
    }

    /// <summary>
    /// Rebuilds <paramref name="ctx"/>'s provider set from the currently-loaded provider assemblies
    /// (reloading each provider config from the context's folder) and re-registers the providers
    /// into its live <see cref="AIService"/>. Called when the AI options panel opens so newly
    /// discovered providers and on-disk config changes take effect.
    /// </summary>
    public void RefreshProviders(WorkContext ctx)
    {
        ctx.Providers          = ProviderManager.Instance.CreateProviderSet(ContextDir(ctx.Name));
        ctx.AiConfig.Providers = ctx.Providers;

        if (ctx.AiService is { } svc)
            foreach (var (name, provider) in ctx.Providers.Providers)
                svc.Register(name, provider);
    }

    /// <summary>
    /// Removes <paramref name="ctx"/> from the list. At least one context always remains.
    /// Returns <c>true</c> if removed.
    /// </summary>
    public bool Remove(WorkContext ctx)
    {
        if (Contexts.Count <= 1) return false;
        return Contexts.Remove(ctx);
    }

    /// <summary>Per-work-context data folder: <c>%AppData%\…\Contexts\&lt;name&gt;</c>.</summary>
    public static string ContextDir(string name)
        => Path.Combine(ConfigManager.Instance.BaseDir, "Contexts", name);

    /// <summary>
    /// Persists the WorkContexts list (metadata only — names/colours/icons). Each context's AI
    /// config lives in its own folder and is saved by <see cref="SaveContextAiConfig"/>.
    /// </summary>
    public void SaveConfig()
    {
        if (_config is null) return;
        _config.Contexts = [.. Contexts];
        ConfigManager.Instance.Save(_config, _config.ConfigName);
    }

    /// <summary>
    /// Persists one context's AI ability config to its own folder
    /// (<c>Contexts/&lt;name&gt;/ai-abilities/</c>). Called by <see cref="ViewModels.ManageAiViewModel"/>.
    /// </summary>
    public void SaveContextAiConfig(WorkContext ctx)
        => ConfigManager.Instance.SaveTo(ContextDir(ctx.Name), ctx.AiConfig, ctx.AiConfig.ConfigName);

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void BootstrapServices(WorkContext ctx)
    {
        var contextDir = ContextDir(ctx.Name);

        // Load this context's AI ability config from its own folder (default if absent).
        ConfigManager.Instance.LoadFrom(contextDir, ctx.AiConfig, ctx.AiConfig.ConfigName);

        // Per-context provider instances, each with this context's own config (API keys etc.).
        ctx.Providers          = ProviderManager.Instance.CreateProviderSet(contextDir);
        ctx.AiConfig.Providers = ctx.Providers;

        // AIService — always recreate so provider registrations stay current
        var service = new AIService(ctx, Path.Combine(contextDir, "Conversations"));

        foreach (var (name, provider) in ctx.Providers.Providers)
            service.Register(name, provider);

        service.LoadAbilityConfig(ctx.AiConfig);
        ctx.AiService = service;

        // ShellServices — preserve existing instance; it holds live window/tab state
        ctx.ShellServices ??= new ShellServices(ctx, ProviderManager.Instance.ActivityManager);

        // RibbonLayoutService — always recreate (path is deterministic from context name)
        ctx.RibbonService = new RibbonLayoutService(contextDir);
    }
}
