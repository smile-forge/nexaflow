namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>The face a journey task shows for its score: 5–4 happy, 3 neutral, 2–1 sad.</summary>
public enum JourneyMood { Happy, Neutral, Sad }

/// <summary>One step of a user journey — a <c>Task name: score: actor, actor</c> line.</summary>
public sealed class JourneyTask
{
    public required string Name { get; init; }
    /// <summary>1 (worst) … 5 (best); a missing score parses as 3.</summary>
    public int Score { get; init; } = 3;
    public List<string> Actors { get; } = [];
    public JourneyMood Mood => JourneyDiagram.MoodOf(Score);
}

/// <summary>A named group of tasks (a journey <c>section</c>); tasks declared before any
/// <c>section</c> live in one section whose <see cref="Name"/> is empty.</summary>
public sealed class JourneySection
{
    public required string Name { get; init; }
    public List<JourneyTask> Tasks { get; } = [];
}

/// <summary>
/// Data model for a Mermaid <c>journey</c> (user journey) diagram — sections of scored tasks and
/// the actors who take part in each.  <see cref="Actors"/> is the legend: every distinct actor in
/// first-appearance order, which is also the order colours are assigned in.
/// </summary>
public sealed class JourneyDiagram
{
    public string Title { get; set; } = string.Empty;
    public List<JourneySection> Sections { get; } = [];
    public JourneyConfig Config { get; set; } = new();

    public IEnumerable<JourneyTask> Tasks => Sections.SelectMany(s => s.Tasks);
    public int TaskCount => Sections.Sum(s => s.Tasks.Count);

    /// <summary>Every distinct actor, in the order they first appear (ordinal comparison).</summary>
    public IReadOnlyList<string> Actors =>
        Tasks.SelectMany(t => t.Actors).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Mermaid's score→face rule: 5 and 4 smile, 3 is flat, 2 and 1 frown.</summary>
    public static JourneyMood MoodOf(int score) =>
        score >= 4 ? JourneyMood.Happy : score == 3 ? JourneyMood.Neutral : JourneyMood.Sad;
}
