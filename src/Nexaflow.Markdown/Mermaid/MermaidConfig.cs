using System.Globalization;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// The front matter's <c>config:</c> read as what it is: keys with values, and sections of more of the same.
///
/// <para>
/// Mermaid's front matter is YAML, and what every diagram takes from it is the same shape — a handful of nested
/// <c>key: value</c> lines. This reads exactly that shape and nothing else: no anchors, no flow mappings, no
/// multi-line scalars. A line it cannot read is skipped rather than fought with, because the block is config and a
/// diagram with none of it still draws.
/// </para>
/// <para>
/// Which is also why nothing here says what a key <em>means</em>. <see cref="Pie.PieConfig"/> and its like read
/// their own options out of this, with their own defaults and their own limits.
/// </para>
/// </summary>
public sealed class MermaidConfig
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MermaidConfig> _sections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nothing at all — what a block with no front matter has.</summary>
    public static MermaidConfig None { get; } = new();

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static MermaidConfig Read(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return None;

        var root = new MermaidConfig();
        var open = new List<(int Indent, MermaidConfig Config)> { (-1, root) };

        foreach (var raw in yaml.Split('\n'))
        {
            var line = raw.TrimEnd();
            var text = line.TrimStart();
            if (text.Length == 0 || text[0] is '#' or '-') continue;

            var colon = text.IndexOf(':');
            if (colon <= 0) continue;

            var indent = line.Length - text.Length;
            while (open.Count > 1 && open[^1].Indent >= indent) open.RemoveAt(open.Count - 1);

            var key = text[..colon].Trim();
            var value = text[(colon + 1)..].Trim();

            if (value.Length == 0)
            {
                var section = new MermaidConfig();
                open[^1].Config._sections[key] = section;
                open.Add((indent, section));
                continue;
            }

            open[^1].Config._values[key] = Bare(value);
        }

        return root;
    }

    /// <summary>The section of this name, or nothing.</summary>
    public MermaidConfig? Section(string name) => _sections.GetValueOrDefault(name);

    /// <summary>What a key says, or null where it says nothing.</summary>
    public string? Value(string key) => _values.GetValueOrDefault(key);

    /// <summary>Every key set here, with what it says.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>
    /// What a key says as a number, or null. A size is written as a number of pixels either way —
    /// <c>pieOuterStrokeWidth: "5px"</c> and <c>: 5</c> mean the same thing — so the unit comes off first.
    /// </summary>
    public double? Number(string key) => MermaidNumber.Pixels(Value(key));

    /// <summary>What a key says as true or false, or null.</summary>
    public bool? Flag(string key) =>
        Value(key) is { Length: > 0 } text && bool.TryParse(text, out var flag) ? flag : null;

    /// <summary>A value without the quotes somebody wrote round it.</summary>
    private static string Bare(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary>What <c>config:</c> says under a diagram's own section — <c>config: pie:</c> — or nothing.</summary>
    public MermaidConfig Diagram(string name) => Section("config")?.Section(name) ?? None;

    /// <summary>What <c>config: themeVariables:</c> says, or nothing.</summary>
    public MermaidConfig Theme => Section("config")?.Section("themeVariables") ?? None;

    /// <summary>What <c>config: themeVariables:</c> says under a diagram's own section — <c>themeVariables: radar:</c> — or nothing.</summary>
    public MermaidConfig DiagramTheme(string name) => Theme.Section(name) ?? None;

    /// <summary>
    /// The colours written for <paramref name="count"/> keys numbered from <paramref name="first"/> — <c>pie1</c>…<c>pie12</c>,
    /// <c>cScale0</c>…<c>cScale11</c> — by their number. What is not written is the theme's.
    /// </summary>
    public IReadOnlyDictionary<int, string> Swatches(string prefix, int count, int first = 1)
    {
        var swatches = new Dictionary<int, string>();
        for (var number = first; number < first + count; number++)
            if (Value($"{prefix}{number}") is { Length: > 0 } colour)
                swatches[number] = colour;

        return swatches;
    }

    /// <summary>What a key says as a size: a number greater than nought, or null where none is written or what is would draw nothing.</summary>
    public double? Size(string key) => Number(key) is { } size and > 0 ? size : null;
}
