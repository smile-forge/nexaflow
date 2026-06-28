using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Audio.Models;

namespace Nexaflow.Features.Audio.ViewModels;

/// <summary>One lyric line in the panel; <see cref="IsActive"/> highlights the line at the playhead.</summary>
public partial class LyricLineViewModel : ObservableObject
{
    public LyricLineViewModel(TimeSpan time, string text)
    {
        Time = time;
        Text = text;
    }

    public TimeSpan Time { get; }
    public string Text { get; }

    [ObservableProperty] private bool _isActive;
}

/// <summary>
/// Holds the current track's lyrics. Synced lyrics (from a sibling <c>.lrc</c>) track the playhead and
/// highlight the active line; unsynced lyrics (from an embedded tag) render statically.
/// </summary>
public partial class LyricsViewModel : ObservableObject
{
    public ObservableCollection<LyricLineViewModel> Lines { get; } = [];

    [ObservableProperty] private bool _hasLyrics;

    /// <summary>True when the lyrics carry timestamps and follow the playhead.</summary>
    [ObservableProperty] private bool _isSynced;

    /// <summary>Index of the active line, or -1. Watched by the view to auto-scroll.</summary>
    [ObservableProperty] private int _activeIndex = -1;

    public void Load(IReadOnlyList<LyricLine> lines, bool synced)
    {
        Lines.Clear();
        foreach (var l in lines)
            Lines.Add(new LyricLineViewModel(l.Time, l.Text));

        IsSynced = synced && Lines.Count > 0;
        HasLyrics = Lines.Count > 0;
        SetActive(-1);
    }

    public void Clear()
    {
        Lines.Clear();
        HasLyrics = false;
        IsSynced = false;
        SetActive(-1);
    }

    /// <summary>Highlights the latest line whose timestamp has passed <paramref name="position"/>.</summary>
    public void UpdatePosition(TimeSpan position)
    {
        if (!IsSynced || Lines.Count == 0) return;

        int idx = -1;
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Time <= position) idx = i;
            else break;
        }
        if (idx != ActiveIndex) SetActive(idx);
    }

    private void SetActive(int idx)
    {
        if (ActiveIndex >= 0 && ActiveIndex < Lines.Count) Lines[ActiveIndex].IsActive = false;
        ActiveIndex = idx;
        if (idx >= 0 && idx < Lines.Count) Lines[idx].IsActive = true;
    }
}
