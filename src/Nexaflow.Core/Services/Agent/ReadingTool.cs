using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.IO.Common;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// Hands reading to the model on this machine: it reads the files it is given in full, however long, and does the task the
/// larger model sets from them, so a long document costs the larger model the answer rather than its every word. It reads in
/// parts the size of its context: what the task needs is noted from each part, the notes are merged while they outgrow the
/// context, and the task is done from them. It reads and nothing else: it is given no tools. It reads only what the user has
/// given the assistant — a file open in a tab, or one in a folder open in a tab.
/// </summary>
internal sealed class ReadingTool(ILlmProvider reader, Func<string, IFileTextExtractor?> extractors, Action<string> report, PathScope scope) : IClientTool
{
    /// <summary>The most of one file that is read; past it, what was read is used and the answer says so.</summary>
    internal const long MostBytes = 64L << 20;

    /// <summary>The most text read for one task, across its files, in characters.</summary>
    internal const int MostChars = 4_000_000;

    /// <summary>How much of the model's context a part fills: the rest is for its instructions, its thinking and what it writes.</summary>
    private const double Fill = 0.6;

    /// <summary>The context assumed of a model that does not say, in tokens.</summary>
    private const int AssumedContext = 8192;

    /// <summary>The smallest part read, in characters, whatever the model's context.</summary>
    private const int SmallestPart = 2000;

    /// <summary>What the reader writes for a part that holds nothing the task needs.</summary>
    private const string NothingHere = "Nothing relevant.";

    private const string Brief =
        "You read documents for another assistant and report what its task needs. Your report goes to that assistant, not to " +
        "a person: plain and complete, with facts, figures and wording as the document gives them and where they are (the file, " +
        "and the page or section where it shows). Never add what the documents do not say; when they do not say, say so.";

    public string Name => "ask_local_model";

    public string Description =>
        "Hands reading to the model on this computer, which costs nothing: it reads the files in full, however long, and does " +
        "the task from them — a summary, the parts that matter, facts or figures pulled out, an answer about them. Use it for " +
        "long files whose gist or particulars you need rather than every word. Its answer can miss nuance: read a file yourself " +
        "where every word matters.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("task", "What to find out or do from the files, complete in itself: the model has not seen this conversation."),
        new("files", "Full paths of the files to read — files open in a tab, or in a folder open in one: text, PDF, Word, Excel, PowerPoint or RTF.", Type: "array", ItemType: "string"),
    ];

    public ToolSafety Safety => ToolSafety.SafeOperation;

    /// <summary>One at a time: the machine's model reads one thing at a time however many are asked of it.</summary>
    public bool Parallelizable => false;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var task = ToolArgs.Str(arguments, "task", "question", "prompt");
        if (string.IsNullOrWhiteSpace(task)) return ToolResult.Error("no task", "Give a 'task': what to find out from the files.");

        List<string> paths = arguments["files"] is JsonArray listed
            ? [.. listed.Select(f => f?.ToString()).OfType<string>().Where(f => f.Length > 0)]
            : ToolArgs.Str(arguments, "files", "file", "path") is { Length: > 0 } one ? [one] : [];
        if (paths.Count == 0) return ToolResult.Error("no files", "Give 'files': the full paths of the files to read.");

        var read = new List<(string Path, string Name, string Text)>();
        var unread = new List<string>();
        var cut = false;
        var left = MostChars;
        foreach (var asked in paths)
        {
            var name = Path.GetFileName(asked);
            if (scope.Resolve(asked) is not { } path) { unread.Add(RunScope.Outside(asked)); continue; }
            if (left <= 0) { unread.Add($"{name}: the files before it were as much as is read at once"); cut = true; continue; }
            report($"Reading {name}…");
            var (text, problem) = await ReadAsync(path, ct);
            if (text is null) { unread.Add($"{name}: {problem}"); continue; }
            if (text.Length > left) { text = text[..left]; cut = true; }
            left -= text.Length;
            read.Add((path, name, text));
        }
        if (read.Count == 0) return ToolResult.Error("nothing read", "None of the files could be read: " + string.Join("; ", unread) + ".");

        var context = await ContextAsync(ct);
        var part = Math.Max(SmallestPart, (int)(context * Fill) * RequestTriage.CharsPerToken);

        string answer;
        try { answer = await AnswerAsync(task, read, part, ct); }
        catch (LlmProviderException ex) { return ToolResult.Error("could not read them", ex.Message); }

        var said = new StringBuilder(answer);
        if (cut) said.Append("\n\n(Only the start of the longest files was read: they hold more than is read at once.)");
        if (unread.Count > 0) said.Append("\n\nNot read: ").Append(string.Join("; ", unread)).Append('.');
        return ToolResult.Ok($"read {read.Count} file(s)", said.ToString()) with { Attachments = [.. read.Select(r => r.Path)] };
    }

    /// <summary>The task done from the files: from all of them at once where they fit, from notes taken part by part where not.</summary>
    private async Task<string> AnswerAsync(string task, IReadOnlyList<(string Path, string Name, string Text)> read, int part, CancellationToken ct)
    {
        if (read.Sum(r => r.Name.Length + r.Text.Length + 8) <= part)
        {
            var whole = new StringBuilder("Task: ").Append(task).Append("\n\n");
            foreach (var (_, name, text) in read) whole.Append("# ").Append(name).Append("\n\n").Append(text).Append("\n\n");
            whole.Append("Do the task from these documents.");
            report("Reading…");
            return await AskAsync(whole.ToString(), ct);
        }

        var notes = new List<string>();
        foreach (var (_, name, text) in read)
        {
            var parts = Split(text, part);
            for (var i = 0; i < parts.Count; i++)
            {
                report($"Reading {name} — part {i + 1} of {parts.Count}…");
                var note = await AskAsync($"Task: {task}\n\nThis is part {i + 1} of {parts.Count} of {name}:\n\n{parts[i]}\n\n" +
                                          $"Note everything in this part that the task needs, briefly. If nothing in it bears on the task, reply: {NothingHere}", ct);
                if (note.Length > 0 && !note.StartsWith(NothingHere.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
                    notes.Add($"From {name}, part {i + 1} of {parts.Count}:\n{note}");
            }
        }
        if (notes.Count == 0) return "Nothing in the files bears on the task.";

        while (notes.Sum(n => n.Length + 2) > part && notes.Count > 1)
        {
            report("Bringing the notes together…");
            var merged = new List<string>();
            foreach (var group in Groups(notes, part))
                merged.Add(group.Count == 1 ? group[0]
                    : await AskAsync($"Task: {task}\n\nNotes taken while reading, part by part:\n\n{string.Join("\n\n", group)}\n\n" +
                                     "Merge them into one set of notes, keeping everything the task needs and where it is.", ct));
            if (merged.Count >= notes.Count) { notes = merged; break; }
            notes = merged;
        }
        var all = string.Join("\n\n", notes);
        if (all.Length > part) all = all[..part];

        report("Writing up what it read…");
        return await AskAsync($"Task: {task}\n\nNotes taken while reading {string.Join(", ", read.Select(r => r.Name))}, part by part:\n\n{all}\n\n" +
                              "Do the task from these notes.", ct);
    }

    private async Task<string> AskAsync(string asked, CancellationToken ct)
        => (await reader.CompleteAsync([new(LlmRole.System, Brief), new(LlmRole.User, asked)], LlmUrgency.Interactive, ct))?.RawText?.Trim() ?? "";

    private async Task<int> ContextAsync(CancellationToken ct)
    {
        try { return (await reader.GetModelInfoAsync(ct))?.ContextWindowTokens is > 0 and var known ? known : AssumedContext; }
        catch (OperationCanceledException) { throw; }
        catch { return AssumedContext; }
    }

    /// <summary>The file's text: through the extractor that claims its format, as text otherwise; null and why where neither reads it.</summary>
    private async Task<(string? Text, string? Problem)> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return (null, "no such file");
        try
        {
            if (extractors(path) is { } extractor)
                return await extractor.ExtractAsync(path, MostBytes, ct) is { Length: > 0 } extracted ? (extracted, null) : (null, "no text could be read from it");
            return await Task.Run(() => ReadText(path), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (null, ex.Message); }
    }

    private static (string? Text, string? Problem) ReadText(string path)
    {
        using var stream = File.OpenRead(path);
        var probe = EncodingDetector.Detect(stream);
        if (probe.Encoding.CodePage is 65001 or 28591 && HoldsNul(stream)) return (null, "it is not text, and no reader for its format is installed");

        stream.Position = probe.BomByteCount;
        using var text = new StreamReader(stream, probe.Encoding, detectEncodingFromByteOrderMarks: false);
        var buffer = new char[(int)Math.Min(stream.Length, MostChars)];
        var count = text.ReadBlock(buffer, 0, buffer.Length);
        return (new string(buffer, 0, count), null);
    }

    /// <summary>Whether the head of a file the probe took for 8-bit or UTF-8 text holds a NUL, as no text does.</summary>
    private static bool HoldsNul(Stream stream)
    {
        stream.Position = 0;
        var head = new byte[(int)Math.Min(stream.Length, 8192)];
        var got = stream.Read(head, 0, head.Length);
        return Array.IndexOf(head, (byte)0, 0, got) >= 0;
    }

    /// <summary><paramref name="text"/> in parts of at most <paramref name="size"/> characters, each ended at a line break
    /// where one falls in its last fifth.</summary>
    internal static IReadOnlyList<string> Split(string text, int size)
    {
        var parts = new List<string>();
        var at = 0;
        while (at < text.Length)
        {
            var end = Math.Min(text.Length, at + size);
            if (end < text.Length)
            {
                var floor = at + size * 4 / 5;
                var line = text.LastIndexOf('\n', end - 1, end - floor);
                if (line >= floor) end = line + 1;
            }
            parts.Add(text[at..end]);
            at = end;
        }
        return parts;
    }

    /// <summary>The notes in runs whose text fits in <paramref name="size"/> characters, each run at least one note.</summary>
    private static IEnumerable<List<string>> Groups(IReadOnlyList<string> notes, int size)
    {
        var group = new List<string>();
        var length = 0;
        foreach (var note in notes)
        {
            if (group.Count > 0 && length + note.Length + 2 > size) { yield return group; group = []; length = 0; }
            group.Add(note);
            length += note.Length + 2;
        }
        if (group.Count > 0) yield return group;
    }
}
