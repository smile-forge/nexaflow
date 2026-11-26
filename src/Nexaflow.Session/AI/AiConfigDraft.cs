using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;
using Nexaflow.Session.Services.LocalAi;

namespace Nexaflow.Session.AI;

/// <summary>
/// An edit of a workspace's ability grid, judged against the providers it can use and the models on this machine: the model chosen
/// for each job of the Local column, the providers' columns, and for each ability the chain of columns that serve it, in order of
/// preference. A chain only ever names a column that exists and whose model serves its ability. Nothing reaches the config until
/// <see cref="Apply"/>, except a Local choice, which takes effect at once.
/// </summary>
public sealed class AiConfigDraft
{
    private readonly AiConfig _config;

    /// <summary>The model chosen for each job of the Local column.</summary>
    private readonly Dictionary<LocalSlot, ProviderModelPair> _local = [];

    private readonly List<ProviderModelPair> _columns = [];
    private readonly Dictionary<AiAbility, List<string>> _chains = [];

    /// <param name="config">The config edited.</param>
    /// <param name="providers">The provider instances the edit is judged by and lists models from — the editor's own, as the saved
    /// config carries none; without them, no provider column can be added and none is judged beyond its kind.</param>
    /// <param name="shelf">The models on this machine; without it, the Local column has nothing to use.</param>
    public AiConfigDraft(AiConfig config, ProviderSet? providers = null, LocalModelShelf? shelf = null)
    {
        _config   = config;
        Providers = providers;
        Shelf     = shelf;
        if (providers is not null) LocalColumns.Gather(config, providers.IsLocal);

        foreach (var slot in Enum.GetValues<LocalSlot>())
            if (config.Columns.FirstOrDefault(c => c.Id == LocalColumns.IdOf(slot)) is { } chosen) _local[slot] = Copy(chosen);
        foreach (var column in config.Columns.Where(c => !LocalColumns.IsLocal(c.Id)))
            _columns.Add(Copy(column));

        // Each saved chain keeps only the columns that still exist and serve its ability, and its job's Local model where one is chosen.
        foreach (var ability in Enum.GetValues<AiAbility>())
        {
            var usable = _columns.Where(c => c.Kind == AiAbilityNames.Kind(ability) && Serves(ability, c.ProviderName, c.Kind, c.Model))
                                 .Select(c => c.Id).ToHashSet();
            if (_local.ContainsKey(LocalColumns.SlotOf(ability))) usable.Add(LocalColumns.IdOf(LocalColumns.SlotOf(ability)));
            var saved = config.Assignments.GetValueOrDefault(ability.ToString()) ?? AbilityChain.Empty;
            _chains[ability] = [.. saved.ColumnIds.Where(usable.Contains)];
        }
    }

    /// <summary>Raised when anything the edit holds changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The providers the edit is judged by and lists models from.</summary>
    public ProviderSet? Providers { get; private set; }

    /// <summary>The models on this machine.</summary>
    public LocalModelShelf? Shelf { get; private set; }

    /// <summary>The providers' columns, in the order they were added; the Local column's models are <see cref="LocalModelFor"/>.</summary>
    public IReadOnlyList<ProviderModelPair> Columns => _columns;

    /// <summary>The model chosen for <paramref name="slot"/>, or null when none is.</summary>
    public ProviderModelPair? LocalModelFor(LocalSlot slot) => _local.GetValueOrDefault(slot);

    /// <summary>The columns that serve <paramref name="ability"/>, in order of preference — a Local column by its job's id.</summary>
    public IReadOnlyList<string> ChainOf(AiAbility ability) => _chains[ability];

    /// <summary>Whether a model of <paramref name="providerName"/> serves <paramref name="ability"/> beyond being of its kind — judged
    /// by the provider's capability, so a column is judged before it is bound to its model.</summary>
    public bool Serves(AiAbility ability, string providerName, ModelKind kind, string model)
        => AiAbilityNames.Serves(ability, Providers?.Find(providerName, kind), model);

    /// <summary>Whether what <paramref name="column"/>'s model is sent stays on this machine — read from its provider as the
    /// workspace configures it: an Ollama server's address, an OpenAI-compatible base URL.</summary>
    public bool StaysOnMachine(ProviderModelPair column)
        => Providers?.Find(column.ProviderName, column.Kind) is { Residency: DataResidency.StaysOnMachine };

    /// <summary>A column not in <paramref name="ability"/>'s chain joins it at the end; one already in it leaves, and those after it
    /// move up.</summary>
    public void Toggle(AiAbility ability, string columnId)
    {
        var chain = _chains[ability];
        if (!chain.Remove(columnId)) chain.Add(columnId);
        Raise();
    }

    /// <summary>Every provider and kind of model a column can be added for: a provider whose required config (an API key) is not
    /// complete offers nothing yet, and Nexaflow's own models are the Local column's, never a column of their own.</summary>
    public IReadOnlyList<(string ProviderName, ModelKind Kind)> Offers()
    {
        if (Providers is not { } set) return [];
        var configs = set.Configs.ToDictionary(c => c.FriendlyName);
        var offers  = new List<(string, ModelKind)>();
        foreach (var provider in set.Providers)
        {
            if (provider.Harness == ModelHarness.Owned) continue;
            if (configs.TryGetValue(provider.Name, out var config) && !ConfigRequirements.IsComplete(config)) continue;
            foreach (var kind in Enum.GetValues<ModelKind>())
                if (provider.Serves(kind)) offers.Add((provider.Name, kind));
        }
        return offers;
    }

    /// <summary>Adds a column for <paramref name="model"/> of <paramref name="providerName"/>; no chain uses it yet.</summary>
    public ProviderModelPair AddColumn(string providerName, ModelKind kind, string model)
    {
        var column = new ProviderModelPair
        {
            Id = Guid.NewGuid().ToString("N"), ProviderName = providerName, Model = model, Kind = kind,
            AssemblyFileName = Providers?.GetAssemblyFileName(providerName) ?? string.Empty,
        };
        _columns.Add(column);
        Raise();
        return column;
    }

    /// <summary>Removes a provider's column, and takes it out of every chain.</summary>
    public void RemoveColumn(string columnId)
    {
        _columns.RemoveAll(c => c.Id == columnId);
        foreach (var chain in _chains.Values) chain.Remove(columnId);
        Raise();
    }

    /// <summary>
    /// Makes <paramref name="model"/> <paramref name="slot"/>'s model and writes that into the config at once — a choice that takes
    /// effect straight away, while every other edit waits for <see cref="Apply"/>. A job none of whose rows uses the Local column
    /// yet starts using it in every one, at the end of its chain: the Local column is on wherever it can be.
    /// </summary>
    public void ChooseLocal(LocalSlot slot, ProviderModelPair model)
    {
        var id   = LocalColumns.IdOf(slot);
        var jobs = _chains.Where(c => LocalColumns.SlotOf(c.Key) == slot).Select(c => c.Value).ToList();
        var used = jobs.Any(chain => chain.Contains(id));

        _local[slot] = Copy(model, id);
        LocalColumns.Use(_config, slot, model);
        if (!used)
            foreach (var chain in jobs) chain.Add(id);
        Raise();
    }

    /// <summary>The models the Local column is used with that are not downloaded yet, each once.</summary>
    public IReadOnlyList<LocalModelOption> MissingLocal()
    {
        if (Shelf is not { } shelf) return [];
        var missing = new List<LocalModelOption>();
        foreach (var (slot, chosen) in _local)
        {
            var id = LocalColumns.IdOf(slot);
            if (!_chains.Any(c => LocalColumns.SlotOf(c.Key) == slot && c.Value.Contains(id))) continue;
            if (shelf.OptionsFor(slot).FirstOrDefault(o => o.IsIn(chosen)) is { Runs: true } option && !shelf.IsReady(option)
                && !missing.Any(m => m.IsIn(option.Column())))
                missing.Add(option);
        }
        return missing;
    }

    /// <summary>Judges and lists from <paramref name="providers"/> from now on, and reads the models on this machine from
    /// <paramref name="shelf"/>: the editor acquired its set again.</summary>
    public void UseProviders(ProviderSet providers, LocalModelShelf? shelf)
    {
        Providers = providers;
        Shelf     = shelf;
        Raise();
    }

    /// <summary>Whether the edit holds anything the config does not.</summary>
    public bool HasChanges
        => !AllColumns().Select(c => (c.Id, c.Model)).OrderBy(c => c.Id)
                        .SequenceEqual(_config.Columns.Select(c => (c.Id, c.Model)).OrderBy(c => c.Id))
           || _chains.Any(c => !new AbilityChain(c.Value).SameAs(_config.Assignments.GetValueOrDefault(c.Key.ToString()) ?? AbilityChain.Empty));

    /// <summary>Writes the edit into the config; it then holds nothing the config does not.</summary>
    public void Apply()
    {
        _config.Columns = [.. AllColumns()];
        _config.Assignments.Clear();
        foreach (var (ability, chain) in _chains)
            _config.Assignments[ability.ToString()] = new AbilityChain(chain);
        Raise();
    }

    /// <summary>Every column, as the config saves them: the Local column's models, then the providers'.</summary>
    private IEnumerable<ProviderModelPair> AllColumns() => _local.Values.Select(c => Copy(c)).Concat(_columns.Select(c => Copy(c)));

    private static ProviderModelPair Copy(ProviderModelPair column, string? id = null) => new()
    {
        Id = id ?? column.Id, ProviderName = column.ProviderName, Model = column.Model, Kind = column.Kind,
        AssemblyFileName = column.AssemblyFileName,
    };

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
