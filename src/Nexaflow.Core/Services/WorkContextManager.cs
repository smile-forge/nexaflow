using Nexaflow.Core.Models;
using System.Collections.ObjectModel;
using System.IO;

namespace Nexaflow.Core.Services;

public sealed class WorkContextManager
{
    public static WorkContextManager Instance { get; } = new();

    /// <summary>
    /// Saved workspace configurations — what the dropdown and taskbar JumpList show.
    /// Populated by <see cref="Initialize"/>; never contains runtime-only data.
    /// </summary>
    public ObservableCollection<WorkContextConfig> Configs { get; } = [];

    /// <summary>
    /// Live runtime contexts. One is created per window (lazily, on first use); removed when
    /// its last window closes. Not exposed as a public property — callers use
    /// <see cref="CreateFromConfig"/> / <see cref="GetOrCreate"/>.
    /// </summary>
    private readonly List<WorkContext> _active = [];

    /// <summary>
    /// Fired after <see cref="Initialize"/> completes, or when configs are added/removed,
    /// so listeners (JumpList, ShellViewModel) can refresh their derived state.
    /// </summary>
    public event EventHandler? ContextsRefreshed;

    /// <summary>The first live context, or null when no windows are open (e.g. daemon mode).</summary>
    public WorkContext? FirstActive => _active.FirstOrDefault();

    /// <summary>True when at least one window is open across all active contexts.</summary>
    internal bool AnyWindowsOpen
        => _active.Any(c => c.ShellServices is ShellServices s && s.HasWindows);

    private WorkContextsConfig? _savedConfig;

    private WorkContextManager() { }

    /// <summary>
    /// Loads workspace configurations from <paramref name="config"/>. Does NOT create any
    /// <see cref="WorkContext"/> runtime objects — those are created lazily when windows open.
    /// Must be called AFTER <see cref="ProviderManager"/> has loaded its assemblies.
    /// </summary>
    public void Initialize(WorkContextsConfig config)
    {
        _savedConfig = config;
        Configs.Clear();

        var configs = config.Contexts is { Count: > 0 } saved ? saved : [new WorkContextConfig()];
        foreach (var cfg in configs)
            Configs.Add(cfg);

        ContextsRefreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a new <see cref="WorkContext"/> from <paramref name="config"/> with fully-bootstrapped
    /// services. Each call returns a fresh instance — use this when opening a new window.
    /// </summary>
    public WorkContext CreateFromConfig(WorkContextConfig config)
    {
        var ctx = new WorkContext(config);
        BootstrapServices(ctx);
        _active.Add(ctx);
        return ctx;
    }

    /// <summary>
    /// Returns the first live <see cref="WorkContext"/> already active for <paramref name="config"/>,
    /// or creates one if none exists. Use this for context-switching (the user wants to join an
    /// existing session for that config, not start a fresh one).
    /// </summary>
    public WorkContext GetOrCreate(WorkContextConfig config)
    {
        var existing = _active.FirstOrDefault(c => ReferenceEquals(c.Config, config));
        return existing ?? CreateFromConfig(config);
    }

    /// <summary>
    /// Called by <see cref="ShellServices.UnregisterWindow"/> when a window closes.
    /// Removes <paramref name="ctx"/> from the active list once it has no more open windows.
    /// </summary>
    public void NotifyWindowClosed(WorkContext ctx)
    {
        if (ctx.ShellServices is ShellServices svc && svc.HasWindows) return;
        _active.Remove(ctx);
    }

    /// <summary>
    /// Rebuilds <paramref name="ctx"/>'s provider set from the currently-loaded provider assemblies
    /// and re-registers the providers into its live <see cref="AIService"/>.
    /// Called when the AI options panel opens.
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
    /// Adds a new <see cref="WorkContextConfig"/> with a random icon/colour to <see cref="Configs"/>.
    /// </summary>
    public WorkContextConfig AddConfig(string name)
    {
        var (icon, color) = WorkContextStyle.Random();
        var config = new WorkContextConfig { Name = name, Icon = icon, Color = color };
        Configs.Add(config);
        ContextsRefreshed?.Invoke(this, EventArgs.Empty);
        return config;
    }

    /// <summary>
    /// Creates a new <see cref="WorkContextConfig"/> whose AI ability config and provider configs
    /// are copied from <paramref name="sourceConfig"/>'s folder (or the live runtime state if a
    /// context is currently active for it), with a randomised icon/colour.
    /// </summary>
    public WorkContextConfig CloneConfig(WorkContextConfig sourceConfig, string name)
    {
        var (icon, color) = WorkContextStyle.Random();
        var newConfig = new WorkContextConfig { Name = name, Icon = icon, Color = color };
        var newDir    = ContextDir(name);

        var liveCtx = _active.FirstOrDefault(c => ReferenceEquals(c.Config, sourceConfig));
        if (liveCtx != null)
        {
            // Flush in-memory state to the new folder so BootstrapServices reads it on first open.
            ConfigManager.Instance.SaveTo(newDir, liveCtx.AiConfig, liveCtx.AiConfig.ConfigName);
            foreach (var cfg in liveCtx.Providers?.Configs ?? [])
                ConfigManager.Instance.SaveTo(newDir, cfg, cfg.ConfigName);
        }
        else
        {
            // No live context — copy persisted files directly.
            var srcDir = ContextDir(sourceConfig.Name);
            if (Directory.Exists(srcDir))
            {
                Directory.CreateDirectory(newDir);
                foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
                {
                    var rel  = Path.GetRelativePath(srcDir, file);
                    var dest = Path.Combine(newDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, overwrite: true);
                }
            }
        }

        Configs.Add(newConfig);
        ContextsRefreshed?.Invoke(this, EventArgs.Empty);
        return newConfig;
    }

    /// <summary>
    /// Removes <paramref name="config"/> from <see cref="Configs"/> and disposes any live
    /// <see cref="WorkContext"/> associated with it. At least one config always remains.
    /// Returns <c>true</c> if removed.
    /// </summary>
    public bool RemoveConfig(WorkContextConfig config)
    {
        if (Configs.Count <= 1) return false;

        // Evict the runtime context if present (windows using it must have already been moved away).
        var live = _active.FirstOrDefault(c => ReferenceEquals(c.Config, config));
        if (live != null) _active.Remove(live);

        var removed = Configs.Remove(config);
        if (removed) ContextsRefreshed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <summary>Per-work-context data folder: <c>%AppData%\…\Contexts\&lt;name&gt;</c>.</summary>
    public static string ContextDir(string name)
        => Path.Combine(ConfigManager.Instance.BaseDir, "Contexts", name);

    /// <summary>
    /// Persists the <see cref="WorkContextConfig"/> list (names/colours/icons only).
    /// Each context's AI config is saved separately via <see cref="SaveContextAiConfig"/>.
    /// </summary>
    public void SaveConfig()
    {
        if (_savedConfig is null) return;
        _savedConfig.Contexts = [.. Configs];
        ConfigManager.Instance.Save(_savedConfig, _savedConfig.ConfigName);
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

        ConfigManager.Instance.LoadFrom(contextDir, ctx.AiConfig, ctx.AiConfig.ConfigName);

        ctx.Providers          = ProviderManager.Instance.CreateProviderSet(contextDir);
        ctx.AiConfig.Providers = ctx.Providers;

        var service = new AIService(ctx, Path.Combine(contextDir, "Conversations"));
        foreach (var (name, provider) in ctx.Providers.Providers)
            service.Register(name, provider);

        service.LoadAbilityConfig(ctx.AiConfig);
        ctx.AiService = service;

        ctx.ShellServices  ??= new ShellServices(ctx, ProviderManager.Instance.ActivityManager);
        ctx.RibbonService    = new RibbonLayoutService(contextDir);
    }
}
