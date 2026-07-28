namespace Nexaflow.Search;

/// <summary>How a <see cref="SearchCondition"/>'s value is compared to a property.</summary>
public enum SearchComparison
{
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
    StartsWith,
    EndsWith,
    Contains,
    NotContains,
    Wildcards,

    /// <summary>Whole-word match — what a full-text index does, and what no substring test can imitate.</summary>
    WordEqual,

    /// <summary>Whole-word prefix match.</summary>
    WordStartsWith,

    /// <summary>Recognised as a comparison, but not one this model can express. Must never be guessed at:
    /// an unknown operator evaluated as some nearby one silently answers a different question.</summary>
    Unsupported,
}

/// <summary>What a <see cref="SearchCondition"/> node is.</summary>
public enum SearchConditionKind { And, Or, Not, Leaf }

/// <summary>
/// A parsed property constraint, as a tree rather than a string.
/// <para>
/// This is the whole point of the type: a query is parsed <em>once</em> and then projected — into SQL for
/// an index, and into a predicate for a folder walk. A translated SQL string can only ever serve the
/// first, which is how a constraint the index never saw ends up silently matching every file on a walk.
/// </para>
/// <para>
/// Deliberately inert: no COM, no SQL, no filesystem. It carries what was asked, and the two projections
/// live with the backends that can answer it.
/// </para>
/// </summary>
/// <param name="Kind">Branch or leaf.</param>
/// <param name="Children">Operands of an And/Or/Not branch.</param>
/// <param name="Property">The property a leaf constrains, in the backend's own naming (<c>System.Size</c>).</param>
/// <param name="Comparison">How <paramref name="Value"/> relates to the property.</param>
/// <param name="Value">The value compared against — a string, long, DateTime or bool.</param>
public sealed record SearchCondition(
    SearchConditionKind Kind,
    IReadOnlyList<SearchCondition> Children,
    string? Property = null,
    SearchComparison Comparison = SearchComparison.Equal,
    object? Value = null)
{
    public static SearchCondition Leaf(string property, SearchComparison comparison, object? value) =>
        new(SearchConditionKind.Leaf, [], property, comparison, value);

    public static SearchCondition And(IReadOnlyList<SearchCondition> children) =>
        new(SearchConditionKind.And, children);

    public static SearchCondition Or(IReadOnlyList<SearchCondition> children) =>
        new(SearchConditionKind.Or, children);

    public static SearchCondition Not(SearchCondition child) =>
        new(SearchConditionKind.Not, [child]);

    /// <summary>Every property this condition mentions — what a backend needs to decide whether it can
    /// answer the whole thing or only part of it.</summary>
    public IEnumerable<string> Properties =>
        Kind == SearchConditionKind.Leaf
            ? Property is null ? [] : [Property]
            : Children.SelectMany(c => c.Properties);

    /// <summary>True when any leaf uses a comparison this model couldn't express — the condition can still
    /// be sent to a backend that parsed it, but nothing else may claim to evaluate it.</summary>
    public bool HasUnsupportedComparison =>
        Kind == SearchConditionKind.Leaf
            ? Comparison == SearchComparison.Unsupported
            : Children.Any(c => c.HasUnsupportedComparison);
}
