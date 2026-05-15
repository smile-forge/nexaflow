using Nexaflow.Features.Scratchpad.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Features.Scratchpad.Services;

public sealed class PostItStore
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    private readonly string _postitsPath;
    private readonly string _recyclePath;

    public PostItStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smile", "Nexaflow", "Scratchpad");

        _postitsPath = Path.Combine(root, "postits");
        _recyclePath = Path.Combine(root, "recyclebin");

        Directory.CreateDirectory(_postitsPath);
        Directory.CreateDirectory(_recyclePath);
    }

    public IReadOnlyList<PostItNote> LoadAll()       => LoadFrom(_postitsPath);
    public IReadOnlyList<PostItNote> LoadRecycleBin() => LoadFrom(_recyclePath);

    public void Save(PostItNote note)
        => File.WriteAllText(PostitPath(note.Id), JsonSerializer.Serialize(note, _opts));

    public void Delete(PostItNote note)
    {
        var path = PostitPath(note.Id);
        if (File.Exists(path)) File.Delete(path);
    }

    public void MoveToRecycleBin(PostItNote note)
    {
        var src = PostitPath(note.Id);
        var dst = RecyclePath(note.Id);
        if (File.Exists(src))
            File.Move(src, dst, overwrite: true);
    }

    public void RestoreFromRecycleBin(PostItNote note)
    {
        var src = RecyclePath(note.Id);
        var dst = PostitPath(note.Id);
        if (File.Exists(src))
            File.Move(src, dst, overwrite: true);
    }

    /// <summary>Deletes recycled notes older than <paramref name="retentionDays"/>.
    /// Pass null to skip purge (infinite retention), 0 to delete everything.</summary>
    public void PurgeRecycleBin(int? retentionDays)
    {
        if (retentionDays is null) return;

        var cutoff = DateTimeOffset.Now.AddDays(-retentionDays.Value);
        foreach (var file in Directory.GetFiles(_recyclePath, "*.json"))
        {
            try
            {
                var note = Deserialize(file);
                if (note is null) continue;

                var timestamp = note.ExpiresAt ?? note.CreatedAt;
                if (retentionDays == 0 || timestamp < cutoff)
                    File.Delete(file);
            }
            catch { /* skip corrupt files */ }
        }
    }

    /// <summary>Deletes all files in the recycle bin regardless of age.</summary>
    public void EmptyRecycleBin()
    {
        foreach (var file in Directory.GetFiles(_recyclePath, "*.json"))
        {
            try { File.Delete(file); } catch { }
        }
    }

    private IReadOnlyList<PostItNote> LoadFrom(string folder)
    {
        var notes = new List<PostItNote>();
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                var note = Deserialize(file);
                if (note is not null) notes.Add(note);
            }
            catch { /* skip corrupt files */ }
        }
        return notes;
    }

    private static PostItNote? Deserialize(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PostItNote>(json, _opts);
    }

    private string PostitPath(Guid id) => Path.Combine(_postitsPath, $"{id}.json");
    private string RecyclePath(Guid id) => Path.Combine(_recyclePath, $"{id}.json");
}
