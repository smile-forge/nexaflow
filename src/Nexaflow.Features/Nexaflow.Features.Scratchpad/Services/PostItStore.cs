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
    private readonly string _attachmentsPath;

    public PostItStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Smile", "Nexaflow", "Scratchpad"))
    {
    }

    public PostItStore(string root)
    {
        _postitsPath     = Path.Combine(root, "postits");
        _recyclePath     = Path.Combine(root, "recyclebin");
        _attachmentsPath = Path.Combine(root, "attachments");

        Directory.CreateDirectory(_postitsPath);
        Directory.CreateDirectory(_recyclePath);
        Directory.CreateDirectory(_attachmentsPath);
    }

    /// <summary>
    /// The per-note folder that holds a post-it's backing files (dropped images, URL preview
    /// images). Keyed on the note Id; its existence is the attachment state. The folder follows
    /// the note's lifetime — it survives recycle/restore and is removed only on permanent deletion
    /// (<see cref="Delete"/> / <see cref="PurgeRecycleBin"/> / <see cref="EmptyRecycleBin"/>).
    /// </summary>
    public string AttachmentDir(Guid id) => Path.Combine(_attachmentsPath, id.ToString());

    /// <summary>Creates (if needed) and returns the note's attachment folder.</summary>
    public string EnsureAttachmentDir(Guid id)
    {
        var dir = AttachmentDir(id);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void DeleteAttachmentDir(Guid id)
    {
        var dir = AttachmentDir(id);
        if (Directory.Exists(dir))
            try { Directory.Delete(dir, recursive: true); } catch { }
    }

    public IReadOnlyList<PostItNote> LoadAll()       => LoadFrom(_postitsPath);
    public IReadOnlyList<PostItNote> LoadRecycleBin() => LoadFrom(_recyclePath);

    public void Save(PostItNote note)
        => File.WriteAllText(PostitPath(note.Id), JsonSerializer.Serialize(note, _opts));

    public void Delete(PostItNote note)
    {
        var live = PostitPath(note.Id);
        if (File.Exists(live)) File.Delete(live);
        var bin = RecyclePath(note.Id);
        if (File.Exists(bin)) File.Delete(bin);
        DeleteAttachmentDir(note.Id);
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
                {
                    File.Delete(file);
                    DeleteAttachmentDir(note.Id);
                }
            }
            catch { /* skip corrupt files */ }
        }
    }

    /// <summary>Deletes all files in the recycle bin regardless of age.</summary>
    public void EmptyRecycleBin()
    {
        foreach (var file in Directory.GetFiles(_recyclePath, "*.json"))
        {
            try
            {
                File.Delete(file);
                if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id))
                    DeleteAttachmentDir(id);
            }
            catch { }
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
