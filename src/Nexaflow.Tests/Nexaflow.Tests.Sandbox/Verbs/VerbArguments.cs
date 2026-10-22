using System.Globalization;

namespace Nexaflow.Tests.Sandbox.Verbs;

/// <summary>
/// A verb's arguments, parsed strictly against its <see cref="Verb"/>: an option it does not declare, a value option with
/// nothing after it, or the wrong number of positionals is refused with the verb's synopsis. A lone <c>--</c> makes
/// everything after it positional, for text that starts with dashes.
/// </summary>
internal sealed class VerbArguments
{
    private readonly Dictionary<string, List<string>> _values;
    private readonly HashSet<string> _flags;

    private VerbArguments(IReadOnlyList<string> positionals, Dictionary<string, List<string>> values, HashSet<string> flags)
    {
        Positionals = positionals;
        _values = values;
        _flags = flags;
    }

    public IReadOnlyList<string> Positionals { get; }

    public string? Positional(int index) => index < Positionals.Count ? Positionals[index] : null;

    /// <summary>The option's value — the last, when it was given more than once — or null.</summary>
    public string? Value(string name) => _values.TryGetValue(name, out var given) ? given[^1] : null;

    public IReadOnlyList<string> Values(string name) => _values.TryGetValue(name, out var given) ? given : [];

    public bool Flag(string name) => _flags.Contains(name);

    public int? IntValue(string name)
    {
        if (Value(name) is not { } text) return null;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new SandboxException($"--{name} takes a positive whole number, not '{text}'.");
    }

    public static VerbArguments Parse(Verb verb, IReadOnlyList<string> args)
    {
        var positionals = new List<string>();
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                positionals.AddRange(args.Skip(i + 1));
                break;
            }
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(arg);
                continue;
            }

            var name = arg[2..];
            if (verb.ValueOptions.Contains(name))
            {
                if (i + 1 >= args.Count) throw Refused(verb, $"--{name} needs a value");
                if (!values.TryGetValue(name, out var given)) values[name] = given = [];
                given.Add(args[++i]);
            }
            else if (verb.Flags.Contains(name))
                flags.Add(name);
            else
                throw Refused(verb, $"unknown option {arg}");
        }

        if (positionals.Count < verb.MinPositionals) throw Refused(verb, "missing an argument");
        if (positionals.Count > verb.MaxPositionals) throw Refused(verb, $"too many arguments ('{positionals[verb.MaxPositionals]}')");
        return new VerbArguments(positionals, values, flags);
    }

    private static SandboxException Refused(Verb verb, string why) =>
        new($"{verb.Name}: {why}.{Environment.NewLine}  usage: {verb.Synopsis}");
}
