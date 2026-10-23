using System.IO;
using System.Text.Json;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// The days of the last week each local model was used on, on this machine — what the preload policy reads. Kept in the
/// AI folder beside the models, which is this machine's alone: what is worth loading ahead depends on what runs here, and
/// the models are loaded once for every workspace. A model is named by its provider and its id ("Nexaflow/gemma-4-e4b-it").
/// The file changes at most once a day per model — on its first use that day — so each change is written straight away,
/// off the caller's thread.
/// </summary>
public sealed class ModelUseHistory
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SortedSet<DateOnly>> _days = new(StringComparer.Ordinal);
    private Task _saving = Task.CompletedTask;

    public ModelUseHistory(string path, TimeProvider? time = null)
    {
        _path = path;
        _time = time ?? TimeProvider.System;
        Read();
    }

    /// <summary>Completes once every change recorded so far is on disk.</summary>
    internal Task Saved
    {
        get { lock (_gate) return _saving; }
    }

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    private DateOnly FirstCounted => Today.AddDays(1 - ModelPreloadConfig.UseWindowDays);

    /// <summary>Counts today as a day <paramref name="model"/> was used on.</summary>
    public void Record(string model)
    {
        lock (_gate)
        {
            if (!_days.TryGetValue(model, out var days)) _days[model] = days = [];
            if (!days.Add(Today)) return;
            Trim();
            var snapshot = _days.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);
            _saving = _saving.ContinueWith(_ => Write(snapshot), TaskScheduler.Default);
        }
    }

    /// <summary>On how many of the last seven days, today among them, <paramref name="model"/> was used.</summary>
    public int DaysUsed(string model)
    {
        lock (_gate)
            return _days.TryGetValue(model, out var days) ? days.Count(d => d >= FirstCounted) : 0;
    }

    /// <summary>Forgets the days that no longer count, and the models with none left.</summary>
    private void Trim()
    {
        var first = FirstCounted;
        foreach (var (model, days) in _days.ToList())
        {
            days.RemoveWhere(d => d < first);
            if (days.Count == 0) _days.Remove(model);
        }
    }

    private void Read()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var read = JsonSerializer.Deserialize<Dictionary<string, DateOnly[]>>(File.ReadAllText(_path), Json);
            if (read is null) return;
            foreach (var (model, days) in read) _days[model] = [.. days];
            Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable history counts as none: every model then loads with its first use until uses are counted again.
            _days.Clear();
        }
    }

    /// <summary>Writes the days beside the file and moves them over it, so a crash mid-write leaves the last whole one.</summary>
    private void Write(Dictionary<string, DateOnly[]> days)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var written = _path + ".tmp";
            File.WriteAllText(written, JsonSerializer.Serialize(days, Json));
            File.Move(written, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not recorded this time; the next day's first use writes the whole history again.
        }
    }
}
