namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// What one verb accepts: how many positional arguments, which options carry a value, and which are bare
/// switches. Declared once per verb and used for <b>both</b> parsing and the error text, so the two can't drift.
/// </summary>
/// <param name="Verb">The verb's name, for error messages.</param>
/// <param name="Positionals">How many positional arguments the verb itself takes (excluding <c>&lt;root&gt;</c>).</param>
/// <param name="ValueFlags">Options of the form <c>--name &lt;value&gt;</c> (or <c>--name=&lt;value&gt;</c>).</param>
/// <param name="Switches">Bare on/off options such as <c>--json</c>.</param>
/// <param name="Usage">The one-line usage shown when the arguments don't fit.</param>
/// <param name="TakesRoot">Whether a trailing <c>&lt;root&gt;</c> positional is allowed. False inside a
/// <c>batch</c> script, where every instruction shares the run's single root.</param>
internal sealed record VerbSpec(
    string Verb,
    int Positionals,
    string[] ValueFlags,
    string[] Switches,
    string Usage,
    bool TakesRoot = true,
    int MinPositionals = -1)
{
    /// <summary>How many of <see cref="Positionals"/> must actually be supplied. Defaults to all of them —
    /// an optional positional is the exception, for a verb whose request is complete without it
    /// (<c>tree</c> with no node id means the whole tree).</summary>
    public int Required => MinPositionals < 0 ? Positionals : MinPositionals;

    /// <summary>The same spec as it applies inside a batch script — no trailing <c>&lt;root&gt;</c>.</summary>
    public VerbSpec InBatch => this with { TakesRoot = false };
}

/// <summary>
/// One verb's parsed command line. Replaces the "filter out anything starting with '-', then look each flag
/// up by name" idiom, which silently dropped an option the verb didn't know about — so
/// <c>set-concern x tests done --note "why"</c> discarded the note and reported success. Here an unrecognised
/// option, a missing option value, or a surplus positional is a hard error, and because the <c>Apply*</c>
/// cores are shared with <c>batch</c>, a typo aborts the whole (transactional) script before anything is written.
/// </summary>
/// <remarks>
/// Parsing is strictly left to right and consumes each value token as it goes. That also fixes a subtler bug
/// in the old <c>FollowsFlag</c> helper, which located a token with <c>Array.IndexOf</c> — the <em>first</em>
/// occurrence — so a flag value that happened to equal an earlier positional was mistaken for one.
/// </remarks>
internal sealed class VerbArgs
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _switches = new(StringComparer.Ordinal);

    private VerbArgs(IReadOnlyList<string> positionals, string? root)
    {
        Positionals = positionals;
        Root = root;
    }

    /// <summary>The verb's own positional arguments, exactly <see cref="VerbSpec.Positionals"/> of them.</summary>
    public IReadOnlyList<string> Positionals { get; }

    /// <summary>The trailing <c>&lt;root&gt;</c> argument, or null when it was omitted.</summary>
    public string? Root { get; }

    public string this[int index] => Positionals[index];

    /// <summary>The value given for <paramref name="flag"/>, or null if it wasn't supplied. When a flag is
    /// repeated the last wins — see <see cref="All"/> for the repeatable ones.</summary>
    public string? Value(string flag) => _values.TryGetValue(flag, out var v) ? v[^1] : null;

    /// <summary>Every value given for a repeatable flag (e.g. <c>--test-dll</c>), in order.</summary>
    public IReadOnlyList<string> All(string flag) => _values.TryGetValue(flag, out var v) ? v : [];

    public bool Has(string flag) => _switches.Contains(flag);

    /// <summary>
    /// Parses <paramref name="args"/> against <paramref name="spec"/>. Returns false with a caller-ready
    /// <paramref name="error"/> when an option is unknown, an option's value is missing, or the positional
    /// count is wrong.
    /// </summary>
    public static bool TryParse(VerbSpec spec, string[] args, out VerbArgs parsed, out string error)
    {
        parsed = null!;
        var positionals = new List<string>();
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var switches = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (token.Length < 2 || token[0] != '-') { positionals.Add(token); continue; }

            // --name=value is accepted alongside --name value; the split is on the FIRST '=' so a value
            // may itself contain one.
            var eq = token.IndexOf('=');
            var name = eq > 0 ? token[..eq] : token;
            var inlineValue = eq > 0 ? token[(eq + 1)..] : null;

            if (spec.Switches.Contains(name, StringComparer.Ordinal))
            {
                if (inlineValue is not null) { error = Fail(spec, $"'{name}' is a switch and takes no value"); return false; }
                switches.Add(name);
                continue;
            }

            if (!spec.ValueFlags.Contains(name, StringComparer.Ordinal))
            {
                error = Fail(spec, $"unknown option '{name}'");
                return false;
            }

            // A value is taken positionally, so it may itself look like an option (--desc "--verbatim").
            if (inlineValue is null)
            {
                if (i + 1 >= args.Length) { error = Fail(spec, $"'{name}' needs a value"); return false; }
                inlineValue = args[++i];
            }
            (values.TryGetValue(name, out var list) ? list : values[name] = []).Add(inlineValue);
        }

        var allowed = spec.Positionals + (spec.TakesRoot ? 1 : 0);
        if (positionals.Count < spec.Required)
        {
            error = Fail(spec, positionals.Count == 0 ? "missing arguments" : "not enough arguments");
            return false;
        }

        // With an optional positional, a lone directory is the <root>, not the id: `nfi tree D:\SomeRepo`
        // means "that repo's tree", and reading it as a node id would search THIS repo and report the path
        // as a missing node. Only an existing directory moves, so a real id — which never names one — is
        // untouched, and the surplus/unknown-argument strictness elsewhere is unchanged.
        if (spec.TakesRoot && positionals.Count > 0 && positionals.Count <= spec.Positionals
            && positionals.Count - 1 >= spec.Required            // moving it must not empty a REQUIRED slot
            && Directory.Exists(positionals[^1]))
        {
            var root = positionals[^1];
            positionals[^1] = string.Empty;                       // the optional slot goes unsupplied
            positionals.Add(root);
        }
        if (positionals.Count > allowed)
        {
            var surplus = string.Join(", ", positionals.Skip(allowed).Select(p => $"'{p}'"));
            error = Fail(spec, $"unexpected argument(s) {surplus}"
                             + (spec.TakesRoot ? "" : " (a batch instruction takes no <root> — the run has one)"));
            return false;
        }

        parsed = new VerbArgs([.. positionals.Take(spec.Positionals)],
                              positionals.Count > spec.Positionals ? positionals[spec.Positionals] : null);
        foreach (var (k, v) in values) parsed._values[k] = v;
        foreach (var s in switches) parsed._switches.Add(s);
        error = string.Empty;
        return true;
    }

    private static string Fail(VerbSpec spec, string problem) => $"{spec.Verb}: {problem}\n  usage: {spec.Usage}";
}
