using System;

namespace Nexaflow.Features.Audio.Models;

/// <summary>
/// One timed lyric line from an <c>.lrc</c> file. <see cref="Time"/> is the moment the line becomes
/// active; <see cref="Time"/> = <see cref="TimeSpan.Zero"/> is used for unsynced lines (e.g. lyrics
/// pulled from an embedded tag) so they still render in order.
/// </summary>
public sealed record LyricLine(TimeSpan Time, string Text);
