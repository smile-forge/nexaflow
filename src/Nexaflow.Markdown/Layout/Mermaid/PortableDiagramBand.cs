
namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// Where a band goes: over each run of things next to each other that belong to the same group — a timeline's periods
/// under their section, a journey's tasks under theirs.
///
/// <para>
/// A group written twice with something else between is two runs, and so two bands, because a band says where its group's
/// things are rather than that the group exists.
/// </para>
/// </summary>
internal static class PortableDiagramBand
{
    /// <summary>
    /// Each run of <paramref name="things"/> sharing a group, as the group, where the run starts and ends, and the box
    /// round everything in it — <paramref name="boxes"/> holding where each thing is drawn, one for one.
    /// </summary>
    public static IEnumerable<(TKey Key, int From, int To, LayoutRect Over)> Runs<T, TKey>(
        IReadOnlyList<T> things, Func<T, TKey> key, IReadOnlyList<LayoutRect> boxes)
    {
        var comparer = EqualityComparer<TKey>.Default;

        for (var at = 0; at < things.Count;)
        {
            var group = key(things[at]);
            var last = at;
            while (last + 1 < things.Count && comparer.Equals(key(things[last + 1]), group)) last++;

            yield return (group, at, last, LayoutRect.Union(boxes[at], boxes[last]));
            at = last + 1;
        }
    }
}