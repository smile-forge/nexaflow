namespace Nexaflow.Features.Common;

/// <summary>
/// A feature's own setup for the shell session, run once when its assembly activates: after its global configs are
/// registered, before anything of it is built for a workspace. What a feature keeps for the whole session — a registry read
/// from the machine, a store under the app's data folder — it sets up here, so Core never reaches into a feature to do it.
/// Activation can come on a background thread (the post-paint warm-up): a setup must not touch the UI.
/// </summary>
public interface IFeatureStartup
{
    /// <summary>Sets the feature up for the session.</summary>
    void Start(FeatureStartupContext session);
}

/// <summary>What a feature's setup is given of the session.</summary>
/// <param name="Configs">Every global feature config registered so far, the feature's own among them.</param>
/// <param name="DataDirectory">The app's data folder: the config root, where a feature keeps a store of its own.</param>
/// <param name="SaveConfig">Saves one of the feature's global configs, when its setup changed it.</param>
public sealed record FeatureStartupContext(
    IReadOnlyDictionary<Type, IFeatureConfig> Configs,
    string DataDirectory,
    Action<IFeatureConfig> SaveConfig)
{
    /// <summary>The registered global config of type <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">No such config is registered — its assembly has not activated.</exception>
    public T Config<T>() where T : class, IFeatureConfig
        => Configs.TryGetValue(typeof(T), out var config) && config is T typed
            ? typed
            : throw new InvalidOperationException($"The global config {typeof(T).Name} is not registered.");
}
