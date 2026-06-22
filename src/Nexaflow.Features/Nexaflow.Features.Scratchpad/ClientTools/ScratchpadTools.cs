using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Scratchpad.ViewModels;

namespace Nexaflow.Features.Scratchpad.ClientTools;

/// <summary>
/// Lists the notes on the board: id, colour, shape, pin/expiry state, position, and a one-line content
/// preview. Optionally filtered by colour and/or shape so the agent can find e.g. "the green notes".
/// </summary>
public sealed class ScratchpadListNotesTool(ScratchpadViewModel vm) : IClientTool
{
    public string Name => "scratchpad_list_notes";
    public string Description =>
        "List the sticky notes on the scratchpad board: each note's id, colour " +
        "(Yellow/Blue/Green/Pink/Orange/Purple/White), shape (Rounded/DiagonalsRounded/SpeechBubble), " +
        "pinned/time-left, position+size, and a one-line content preview. Optionally filter by colour and/or shape.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("color", "Only list notes of this colour (e.g. Green). Omit for all colours.", Required: false),
        new("shape", "Only list notes of this shape. Omit for all shapes.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var color = ToolArgs.Str(arguments, "color");
        var shape = ToolArgs.Str(arguments, "shape");

        var all   = vm.SnapshotNotes();
        var notes = ScratchpadToolHelpers.Filter(all, color, shape);
        if (notes.Count == 0)
            return Task.FromResult(ToolResult.Ok("no matching notes",
                ScratchpadToolHelpers.EmptyMessage(all.Count, color, shape)));

        var sb = new StringBuilder($"{notes.Count} note(s):\n");
        foreach (var n in notes)
            sb.Append($"\n[{n.Id}] {n.Color} {n.Shape}, {n.TimeLeft}, at ({n.X},{n.Y}) {n.Width}×{n.Height}\n")
              .Append("    ").Append(ScratchpadToolHelpers.Preview(n.Content));
        return Task.FromResult(ToolResult.Ok($"{notes.Count} note(s)", sb.ToString().TrimEnd()));
    }
}

/// <summary>
/// Returns the full markdown content of notes, filtered by id (from <c>scratchpad_list_notes</c>) and/or
/// colour/shape. With no filter, returns every note. This is how the agent reads "the green notes".
/// </summary>
public sealed class ScratchpadReadNotesTool(ScratchpadViewModel vm) : IClientTool
{
    public string Name => "scratchpad_read_notes";
    public string Description =>
        "Read the full markdown content of scratchpad notes. Filter by id (from scratchpad_list_notes), " +
        "colour, and/or shape; with no filter, returns every note. Use this to read e.g. the green notes.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("id",    "Only read the note with this id (from scratchpad_list_notes).", Required: false),
        new("color", "Only read notes of this colour (e.g. Green).", Required: false),
        new("shape", "Only read notes of this shape.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var id    = ToolArgs.Str(arguments, "id");
        var color = ToolArgs.Str(arguments, "color");
        var shape = ToolArgs.Str(arguments, "shape");

        var all   = vm.SnapshotNotes();
        var notes = ScratchpadToolHelpers.Filter(all, color, shape);
        if (id is not null)
            notes = notes.Where(n => n.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();

        if (notes.Count == 0)
            return Task.FromResult(ToolResult.Ok("no matching notes",
                ScratchpadToolHelpers.EmptyMessage(all.Count, color, shape, id)));

        var sb = new StringBuilder();
        foreach (var n in notes)
            sb.Append($"── [{n.Id}] {n.Color} {n.Shape} ({n.TimeLeft}) ──\n")
              .Append(string.IsNullOrWhiteSpace(n.Content) ? "(empty)" : n.Content.Trim())
              .Append("\n\n");
        return Task.FromResult(ToolResult.Ok($"read {notes.Count} note(s)", sb.ToString().TrimEnd()));
    }
}

/// <summary>
/// Adds a new sticky note to the board with the given content. New notes get a fixed appearance (white,
/// diagonally-rounded) so AI-authored notes are recognisable and the tool stays single-argument. Classified
/// <see cref="ToolSafety.ReadOnly"/> so it auto-runs without an approval prompt — creating a note has no
/// material impact (it lives only on the user's own corkboard and expires like any other note).
/// </summary>
public sealed class ScratchpadAddNoteTool(ScratchpadViewModel vm) : IClientTool
{
    public string Name => "scratchpad_add_note";
    public string Description =>
        "Add a new sticky note to the scratchpad with the given markdown content (new notes are white and " +
        "diagonally-rounded). Returns the new note's id.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("content", "The note's text (markdown is supported)."),
    ];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => false;   // each call mutates the board (placement cascade, z-order)

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var content = ToolArgs.Str(arguments, "content");
        if (string.IsNullOrWhiteSpace(content))
            return Task.FromResult(ToolResult.Error("A note needs some content."));

        var note = vm.AddNoteFromAi(content);
        return Task.FromResult(ToolResult.Ok($"added note [{note.Id}]", $"Created note [{note.Id}]."));
    }
}

internal static class ScratchpadToolHelpers
{
    public static IReadOnlyList<NoteSnapshot> Filter(IReadOnlyList<NoteSnapshot> notes, string? color, string? shape)
        => notes.Where(n =>
               (color is null || string.Equals(n.Color, color, StringComparison.OrdinalIgnoreCase)) &&
               (shape is null || string.Equals(n.Shape, shape, StringComparison.OrdinalIgnoreCase)))
           .ToList();

    /// <summary>First non-empty line of the content, trimmed to one line for an overview.</summary>
    public static string Preview(string content)
    {
        var line = content.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (string.IsNullOrEmpty(line)) return "(empty)";
        return line.Length <= 80 ? line : line[..79] + "…";
    }

    public static string EmptyMessage(int totalNotes, string? color, string? shape, string? id = null)
    {
        if (totalNotes == 0) return "The scratchpad has no notes.";
        var filters = new List<string>();
        if (id is not null)    filters.Add($"id '{id}'");
        if (color is not null) filters.Add($"colour '{color}'");
        if (shape is not null) filters.Add($"shape '{shape}'");
        return filters.Count == 0 ? "No notes." : $"No notes match {string.Join(" + ", filters)}.";
    }
}
