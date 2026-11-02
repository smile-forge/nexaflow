namespace Nexaflow.Markdown.Pipeline;

/// <summary>
/// Somewhere in a document a link can name: a definition saying where a <c>[text][name]</c> link goes, what an abbreviation
/// stands for — as a stage found it (<see cref="LinkTargets"/>).
/// </summary>
/// <param name="Kind">What it is: the kind of the piece that says it.</param>
/// <param name="Name">What a link names it by, written as the stage that found it compares names.</param>
/// <param name="Meaning">What it says: where a link goes, what an abbreviation stands for.</param>
/// <param name="At">Where it is written in the document — or in the unit, for what a unit defines.</param>
/// <param name="Title">What a link it defines says about itself when rested on, where its definition gives it a title.</param>
public sealed record LinkTarget(string Kind, string Name, string Meaning, int At, string? Title = null);

/// <summary>
/// The places a document's links can name: one list for one document, kept by whatever shows it and handed to its stages.
///
/// <para>
/// What a link means can be written anywhere in the document — a definition at the bottom — so no block's reading can know it, and the
/// parser, which reads one block as it would read any other, is told nothing of it. A stage working over the document finds what it
/// defines and says so here (<see cref="Report"/>), and a stage after it asks (<see cref="Named"/>), in the same run.
/// </para>
/// <para>
/// A document read a unit at a time is worked over a unit at a time, each unit's stages with a list of their own over the document's:
/// what the unit defines is said there, gathered from every unit into the document's by whatever keeps them, and every question the
/// unit's stages ask is answered from the document's and remembered, so whoever keeps what the unit was worked over into knows whether
/// it still stands (<see cref="Answered"/>).
/// </para>
/// </summary>
public sealed class LinkTargets
{
    private Known _reported = Known.None;

    /// <summary>For a unit's: the document's, which it answers from.</summary>
    private readonly LinkTargets? _document;

    /// <summary>For a unit's: every question its stages asked, with what the document answered.</summary>
    private readonly List<(string Kind, string? Name, object Answer)> _asked = [];

    /// <summary>A document's: what its stages find is said here, and what they ask is answered from it.</summary>
    public LinkTargets()
    {
    }

    /// <summary>
    /// One unit's, of a document read a unit at a time: what the unit's stages find is said here, as the unit's own
    /// (<see cref="Defined"/>), and what they ask is answered from <paramref name="document"/>'s — every question remembered with
    /// its answer, so whoever keeps what the unit was worked over into knows whether it still stands (<see cref="Answered"/>).
    /// </summary>
    public LinkTargets(LinkTargets document) => _document = document;

    /// <summary>Whether a stage has said what the document holds — never, for content in a language none of whose stages look.</summary>
    public bool Reported { get; private set; }

    /// <summary>How far into the document the stages last reported had read.</summary>
    public int Reach => _reported.Reach;

    /// <summary>What the stages last reported found, in the order it is written.</summary>
    public IReadOnlyList<LinkTarget> Targets => _reported.Targets;

    /// <summary>For a unit's: what the unit defines, in the order written, where in the unit each stands.</summary>
    public IReadOnlyList<LinkTarget> Defined => _reported.Targets;

    /// <summary>
    /// What a stage found in the document from its start to <paramref name="reach"/> — everything there, in the order it is
    /// written — standing in place of whatever was said before. For a unit's, what the unit defines.
    /// </summary>
    public void Report(int reach, IReadOnlyList<LinkTarget> targets)
    {
        _reported = new Known(reach, targets);
        Reported = true;
    }

    /// <summary>Lets go of everything: what is shown now is another document.</summary>
    public void Forget()
    {
        _reported = Known.None;
        Reported = false;
    }

    /// <summary>The first <paramref name="kind"/> written in the document named <paramref name="name"/>, or null where none is.</summary>
    public LinkTarget? Named(string kind, string name)
    {
        if (_document is not null)
        {
            var answer = _document.Named(kind, name);
            _asked.Add((kind, name, Meant(answer)));
            return answer;
        }

        return _reported.Named.TryGetValue((kind, name), out var here) ? here[0] : null;
    }

    /// <summary>Every <paramref name="kind"/> written in the document, in the order it is written.</summary>
    public IEnumerable<LinkTarget> Every(string kind)
    {
        if (_document is not null)
        {
            var answer = _document.Every(kind).ToList();
            _asked.Add((kind, null, Meant(answer)));
            return answer;
        }

        return _reported.Targets.Where(target => target.Kind == kind);
    }

    /// <summary>
    /// For a unit's: whether the document still answers every question the unit's stages asked as it did — what each target means,
    /// not where it stands, since a definition moved along by writing before it means what it did.
    /// </summary>
    public bool Answered()
    {
        foreach (var (kind, name, answer) in _asked)
            if (!Equals(answer, name is null ? Meant(_document!.Every(kind).ToList()) : Meant(_document!.Named(kind, name))))
                return false;

        return true;
    }

    /// <summary>What an answer says, compared by value: what each target means and is called, not where it stands.</summary>
    private static object Meant(LinkTarget? target) => target is null ? string.Empty : (target.Kind, target.Name, target.Meaning, target.Title ?? string.Empty);

    private static object Meant(List<LinkTarget> targets) =>
        string.Join('\n', targets.Select(target => $"{target.Kind}\t{target.Name}\t{target.Meaning}\t{target.Title}"));

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
