using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.Models;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The matching + selection state behind the AI input's "/" quick-open. Kept separate from
/// <see cref="ShellViewModel"/> so the ranking, dedup and keyboard-nav logic can be tested without standing
/// up the whole shell. The shell supplies the candidate set (pages + ribbon items) and drives invocation;
/// this class decides what shows, in what order, and which row is highlighted.
/// </summary>
public sealed partial class SlashCommandPalette : ObservableObject
{
    /// <summary>Cap so a bare "/" doesn't render the entire catalog.</summary>
    public const int MaxItems = 8;

    /// <summary>The matching rows for the current query; empty when the palette is closed.</summary>
    public ObservableCollection<SlashCommandItem> Items { get; } = [];

    /// <summary>Highlighted row (keyboard nav); -1 when none.</summary>
    [ObservableProperty] private int _selectedIndex = -1;

    /// <summary>Open exactly when there are rows to show.</summary>
    public bool IsOpen => Items.Count > 0;

    public SlashCommandItem? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    /// <summary>
    /// Rebuilds the visible rows: keeps the <paramref name="candidates"/> whose <c>Label</c> matches
    /// <paramref name="query"/>, ranked best-match first, deduped by label (first candidate wins — the
    /// caller orders canonical pages ahead of ribbon items), capped to <see cref="MaxItems"/>.
    /// </summary>
    public void Update(string query, IReadOnlyList<SlashCommandItem> candidates)
    {
        Items.Clear();

        var q    = query.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var ranked = candidates
            .Select(c => (Rank: Rank(c.Label, q), Item: c))
            .Where(x => x.Rank is not null)
            .OrderBy(x => x.Rank!.Value)
            .ThenBy(x => x.Item.Label, StringComparer.OrdinalIgnoreCase);

        foreach (var (_, item) in ranked)
        {
            if (!seen.Add(item.Label)) continue;
            Items.Add(item);
            if (Items.Count >= MaxItems) break;
        }

        SelectedIndex = Items.Count > 0 ? 0 : -1;
        RefreshHighlights();
        OnPropertyChanged(nameof(IsOpen));
    }

    public void MoveDown() { if (Items.Count > 0) SelectedIndex = (SelectedIndex + 1) % Items.Count; }
    public void MoveUp()   { if (Items.Count > 0) SelectedIndex = (SelectedIndex - 1 + Items.Count) % Items.Count; }

    public void Close()
    {
        if (Items.Count == 0 && SelectedIndex == -1) return;
        Items.Clear();
        SelectedIndex = -1;
        OnPropertyChanged(nameof(IsOpen));
    }

    partial void OnSelectedIndexChanged(int value) => RefreshHighlights();

    // Focus stays in the text box, so the list can't show selection itself — mirror it onto the rows.
    private void RefreshHighlights()
    {
        for (int i = 0; i < Items.Count; i++)
            Items[i].IsHighlighted = i == SelectedIndex;
    }

    /// <summary>Match rank: 0 = exact, 1 = prefix, 2 = word-start, 3 = substring, null = no match.
    /// An empty query matches everything (rank 3) so a bare "/" lists the top catalog entries.</summary>
    internal static int? Rank(string label, string query)
    {
        if (query.Length == 0) return 3;
        if (label.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (label.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        var idx = label.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return idx > 0 && label[idx - 1] == ' ' ? 2 : 3;
    }
}
