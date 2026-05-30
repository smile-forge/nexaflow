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
    /// <see cref="ShellServices"/>, registers all currently loaded providers, and adds it
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

        // AIService — always recreate so provider registrations stay current
        var service = new AIService(ctx, Path.Combine(contextDir, "Conversations"));

        foreach (var (name, provider) in ProviderManager.Instance.LoadedProviders)
            service.Register(name, provider);

        service.LoadAbilityConfig(ctx.AiConfig);
        ctx.AiService = service;

        // ShellServices — preserve existing instance; it holds live window/tab state
        ctx.ShellServices ??= new ShellServices(ctx, ProviderManager.Instance.ActivityManager);

        // RibbonLayoutService — always recreate (path is deterministic from context name)
        ctx.RibbonService = new RibbonLayoutService(contextDir);
    }
}
