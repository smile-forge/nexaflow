using Nexaflow.AiHost.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core;

/// <summary>Which local models are loaded ahead of their first request.</summary>
public enum ModelPreloadMode
{
    /// <summary>Those used on enough of the last seven days; the rest load with their first request.</summary>
    [ConfigDisplayName("Shell.Config.ModelPreload.Mode.Adaptive")]
    Adaptive,
    /// <summary>None: every model loads with its first request.</summary>
    [ConfigDisplayName("Shell.Config.ModelPreload.Mode.OnDemand")]
    OnDemand,
    /// <summary>Every model a workspace's ability grid assigns.</summary>
    [ConfigDisplayName("Shell.Config.ModelPreload.Mode.Always")]
    Always,
}

/// <summary>
/// How the local models a provider keeps loaded — Nexaflow's own, Whisper, Ollama's — spend this machine's memory: which
/// are loaded ahead once the first window is up, and how long one that was not stays loaded after its last use. Global
/// (one instance, shared by all workspaces): the models are loaded once for the whole app. Registered in App.xaml.cs;
/// surfaced as a "Local models" section in Options.
/// </summary>
public sealed class ModelPreloadConfig : IFeatureConfig
{
    /// <summary>The days a model's uses are counted over.</summary>
    public const int UseWindowDays = 7;

    public string ConfigName   => "model-preload";
    public string FriendlyName => Str.Get("Shell.Config.ModelPreload");

    [ConfigDisplayName("Shell.Config.ModelPreload.Mode")]
    public ModelPreloadMode Mode { get; set; } = ModelPreloadMode.Adaptive;

    /// <summary>Under <see cref="ModelPreloadMode.Adaptive"/>, on how many of the last seven days a model must have been
    /// used to be loaded ahead.</summary>
    [ConfigDisplayName("Shell.Config.ModelPreload.DaysToPreload")]
    public int DaysToPreload { get; set; } = 3;

    /// <summary>How long a model that was not loaded ahead stays loaded after its last use; 0 keeps it until the app
    /// closes.</summary>
    [ConfigDisplayName("Shell.Config.ModelPreload.IdleMinutes")]
    public int IdleMinutes { get; set; } = 15;

    /// <summary>The share of this machine's memory, in percent, models running on its CPU may hold — those no GPU here has
    /// room for. Idle ones are let go to stay within it; a language model's conversation is shortened to fit it. Read when
    /// the AI host starts.</summary>
    [ConfigDisplayName("Shell.Config.ModelPreload.MemoryForModelsPercent")]
    public int MemoryForModelsPercent { get; set; } = AiHostProtocol.DefaultMemoryShare;

    /// <summary>Whether a model used on <paramref name="daysUsed"/> of the last seven days is loaded ahead.</summary>
    public bool Preloads(int daysUsed) => Mode switch
    {
        ModelPreloadMode.Always   => true,
        ModelPreloadMode.OnDemand => false,
        _                         => daysUsed >= Math.Clamp(DaysToPreload, 1, UseWindowDays),
    };

    /// <summary>How long a model not loaded ahead may stand idle before it is let go; null when it never is.</summary>
    public TimeSpan? IdleRelease => IdleMinutes > 0 ? TimeSpan.FromMinutes(IdleMinutes) : null;
}
