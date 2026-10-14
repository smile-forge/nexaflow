namespace Nexaflow.AiHost.Jinja;

/// <summary>
/// A model's chat template, parsed once and rendered for each conversation. The engine is Jinja as transformers runs
/// chat templates — <c>trim_blocks</c> and <c>lstrip_blocks</c>, <c>loopcontrols</c>, its immutable sandbox, and its
/// globals and <c>tojson</c> — so a model is prompted exactly as it was trained, by its own template, with no prompt
/// code written for it here.
/// </summary>
internal sealed class JinjaTemplate
{
    private readonly IReadOnlyList<JinjaNode> _nodes;

    private JinjaTemplate(IReadOnlyList<JinjaNode> nodes) => _nodes = nodes;

    /// <exception cref="JinjaException">The template uses what the engine does not read.</exception>
    public static JinjaTemplate Parse(string source) => new(JinjaParser.Parse(source));

    /// <summary>The template rendered with <paramref name="variables"/> — JSON nodes, dictionaries, lists, strings,
    /// numbers and booleans, as Python would hand them over.</summary>
    /// <exception cref="JinjaException">The template failed, or raised an exception of its own.</exception>
    public string Render(IReadOnlyDictionary<string, object?> variables)
    {
        var scope = new JinjaScope(null);
        foreach (var (name, value) in variables) scope.Set(name, JinjaValues.From(value));
        return new JinjaRenderer().Render(_nodes, scope);
    }
}
