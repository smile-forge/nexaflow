using Nexaflow.Features.Images.Editing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// The photos a viewer has open — each an <see cref="ImageEditSession"/> with its history and decode — the most
/// recently shown kept, so stepping back to one is instant and its undo history is still there. Beyond
/// <see cref="Capacity"/> the least recently shown is closed, which writes its pending edits.
/// </summary>
public sealed class ImageSessionCache(ImageEditingServices editing, int capacity = ImageSessionCache.DefaultCapacity) : IDisposable
{
    public const int DefaultCapacity = 4;

    private readonly List<ImageEditSession> _sessions = [];   // most recently shown last

    /// <summary>Sessions kept open; at least two, so opening one never closes the one shown before it.</summary>
    public int Capacity { get; } = capacity >= 2 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    /// <summary>The session for <paramref name="path"/>, kept from before or opened now, and now the most recent.</summary>
    public ImageEditSession Open(string path)
    {
        if (Find(path) is { } existing)
        {
            _sessions.Remove(existing);
            _sessions.Add(existing);
            return existing;
        }

        var session = new ImageEditSession(path, editing);
        _sessions.Add(session);
        while (_sessions.Count > Capacity)
        {
            var oldest = _sessions[0];
            _sessions.RemoveAt(0);
            oldest.Dispose();
        }
        return session;
    }

    /// <summary>Takes <paramref name="path"/>'s session out of the cache, for the caller to close; null if none is open.</summary>
    public ImageEditSession? Remove(string path)
    {
        var session = Find(path);
        if (session is not null) _sessions.Remove(session);
        return session;
    }

    /// <summary>The session open for <paramref name="path"/>, left where it stands in the cache; null if none is open.</summary>
    public ImageEditSession? Peek(string path) => Find(path);


    private ImageEditSession? Find(string path) =>
        _sessions.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        foreach (var session in _sessions) session.Dispose();
        _sessions.Clear();
    }
}
