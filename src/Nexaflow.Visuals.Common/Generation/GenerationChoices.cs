using System.Collections.ObjectModel;
using System.Globalization;

namespace Nexaflow.Visuals.Common.Generation;

/// <summary>One choice in a list of what a picture or clip model offers: what it says, and what it asks for — null for the model's own.</summary>
public sealed record GenerationChoice(string Label, string? Value);

/// <summary>The lists a person sets a picture or a clip up with, filled from what a model offers, and the seed they type.</summary>
public static class GenerationChoices
{
    /// <summary>
    /// Fills <paramref name="list"/> with <paramref name="offered"/> — after a choice of the model's own labelled
    /// <paramref name="ownLabel"/>, where there is one and anything is offered — and gives back the choice to keep:
    /// <paramref name="chosen"/>'s value where it is still offered, else the first.
    /// </summary>
    public static GenerationChoice? Refill(ObservableCollection<GenerationChoice> list, IEnumerable<GenerationChoice>? offered,
                                           GenerationChoice? chosen, string? ownLabel = null)
    {
        list.Clear();
        var choices = offered?.ToList() ?? [];
        if (ownLabel is not null && choices.Count > 0) list.Add(new(ownLabel, null));
        foreach (var choice in choices) list.Add(choice);
        return list.FirstOrDefault(c => c.Value == chosen?.Value) ?? list.FirstOrDefault();
    }

    /// <summary>The shape proportions written width:height make — "Square", "Landscape" or "Portrait" — or null for what is not proportions.</summary>
    public static string? ShapeOf(string aspect) =>
        aspect.Split(':') is [var w, var h]
        && int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out var width) && width > 0
        && int.TryParse(h, NumberStyles.None, CultureInfo.InvariantCulture, out var height) && height > 0
            ? width == height ? "Square" : width > height ? "Landscape" : "Portrait"
            : null;

    /// <summary>A seed as typed: none for an empty box, else a whole number of 0 or more; false for anything else.</summary>
    public static bool SeedValue(string typed, out int? seed)
    {
        seed = null;
        if (string.IsNullOrWhiteSpace(typed)) return true;
        if (!int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return false;
        seed = value;
        return true;
    }
}
