namespace Nexaflow.Markdown.Pipeline;

/// <summary>
/// Somewhere in a document a link can name: a definition saying where a <c>[text][name]</c> link goes, what an abbreviation
/// stands for, a heading's name — as a stage found it (<see cref="LinkTargets"/>).
/// </summary>
/// <param name="Kind">What it is: the kind of the piece that says it.</param>
/// <param name="Name">What a link names it by, written as the stage that found it compares names.</param>
/// <param name="Meaning">What it says: where a link goes, what an abbreviation stands for — for a heading, its name again.</param>
/// <param name="At">Where it is written in the document.</param>
public sealed record LinkTarget(string Kind, string Name, string Meaning, int At);

/// <summary>
/// The places a document's links can name, as far as it has been read: one list for one document, kept by whatever shows it
/// and handed to its stages.
///
/// <para>
/// What a link means can be written anywhere in the document — a definition at the bottom, a heading three pages down — so
/// no block's reading can know it, and the parser, which reads one block as it would read any other, is told nothing of it. A
/// stage working over the document finds what it defines and says so here (<see cref="Report"/>), and a stage after it asks
/// (<see cref="Named"/>), in the same run.
/// </para>
/// <para>
/// Where only the top of a long document is worked over, what the rest of it defines is found by working over all of it
/// elsewhere, and said separately (<see cref="Further"/>): it stands for everything past what the stages last reached.
/// </para>
/// </summary>
public sealed class LinkTargets
{
    private Known _reported = Known.None;
    private Known _further = Known.None;

    /// <summary>Whether a stage has said what the document holds — never, for content in a language none of whose stages look.</summary>
    public bool Reported { get; private set; }

    /// <summary>How far into the document the stages last reported had read.</summary>
    public int Reach => _reported.Reach;

    /// <summary>What the stages last reported found, in the order it is written.</summary>
    public IReadOnlyList<LinkTarget> Targets => _reported.Targets;

    /// <summary>
    /// What a stage found in the document from its start to <paramref name="reach"/> — everything there, in the order it is
    /// written — standing in place of whatever was said of that stretch before.
    /// </summary>
    public void Report(int reach, IReadOnlyList<LinkTarget> targets)
    {
        _reported = new Known(reach, targets);
        Reported = true;
    }

    /// <summary>
    /// What working over the document as far as <paramref name="reach"/> found, for the part of it past what the stages here
    /// last reached; and whether that changes what any name past there means.
    /// </summary>
    public bool Further(int reach, IReadOnlyList<LinkTarget> targets)
    {
        var before = Beyond(_further);
        _further = new Known(reach, targets);

        return !before.SequenceEqual(Beyond(_further));
    }

    /// <summary>
    /// Moves what was found further on along by <paramref name="by"/> characters, from <paramref name="from"/> on: what was
    /// written before it has changed, and it reads as it did.
    /// </summary>
    public void Moved(int from, int by)
    {
        if (by == 0 || _further.Targets.Count == 0) return;

        _further = new Known(_further.Reach >= from ? _further.Reach + by : _further.Reach,
                             [.. _further.Targets.Select(target => target.At >= from ? target with { At = target.At + by } : target)]);
    }

    /// <summary>Lets go of everything: what is shown now is another document.</summary>
    public void Forget()
    {
        (_reported, _further) = (Known.None, Known.None);
        Reported = false;
    }

    /// <summary>The first <paramref name="kind"/> written in the document named <paramref name="name"/>, or null where none is.</summary>
    public LinkTarget? Named(string kind, string name)
    {
        if (_reported.Named.TryGetValue((kind, name), out var here)) return here[0];

        if (_further.Named.TryGetValue((kind, name), out var further))
            foreach (var target in further)
                if (target.At >= _reported.Reach) return target;

        return null;
    }

    /// <summary>Every <paramref name="kind"/> written in the document, in the order it is written.</summary>
    public IEnumerable<LinkTarget> Every(string kind)
    {
        foreach (var target in _reported.Targets)
            if (target.Kind == kind) yield return target;

        foreach (var target in _further.Targets)
            if (target.Kind == kind && target.At >= _reported.Reach) yield return target;
    }

    /// <summary>What <paramref name="known"/> says past what the stages last reached — where it stands left out, since that only moves.</summary>
    private IEnumerable<(string Kind, string Name, string Meaning)> Beyond(Known known) =>
        known.Targets.Where(target => target.At >= _reported.Reach).Select(target => (target.Kind, target.Name, target.Meaning));

    /// <summary>What was found as far as <paramref name="Reach"/>, and each by what it is called, in the order written.</summary>
    private sealed class Known(int reach, IReadOnlyList<LinkTarget> targets)
    {
        public static readonly Known None = new(0, []);

        public int Reach { get; } = reach;

        public IReadOnlyList<LinkTarget> Targets { get; } = targets;

        public Dictionary<(string Kind, string Name), List<LinkTarget>> Named { get; } = ByName(targets);

        private static Dictionary<(string Kind, string Name), List<LinkTarget>> ByName(IReadOnlyList<LinkTarget> targets)
        {
            var named = new Dictionary<(string Kind, string Name), List<LinkTarget>>();

            foreach (var target in targets)
            {
                if (!named.TryGetValue((target.Kind, target.Name), out var alike)) named[(target.Kind, target.Name)] = alike = [];
                alike.Add(target);
            }

            return named;
        }
    }
}
