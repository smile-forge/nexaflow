namespace Nexaflow.Interaction;

/// <summary>The shared kinds, read as what they are.</summary>
public static class DragPayloadReading
{
    public static bool ContainsAny(this IDragPayload payload, IEnumerable<string> kinds) => kinds.Any(payload.Contains);

    /// <summary>Whether another application could have dragged this in, as against a gesture of the app's own.</summary>
    public static bool IsShareable(this IDragPayload payload) => payload.ContainsAny(DragKinds.Shared);

    /// <summary>Whether it carries files, by path or by content.</summary>
    public static bool CarriesFiles(this IDragPayload payload)
        => payload.Contains(DragKinds.Files) || payload.Contains(DragKinds.FileContents);

    /// <summary>The paths carried; empty where there are none.</summary>
    public static IReadOnlyList<string> Files(this IDragPayload payload)
        => payload.Read(DragKinds.Files) as string[] ?? [];

    public static IReadOnlyList<CarriedFile> Contents(this IDragPayload payload)
        => payload.Read(DragKinds.FileContents) as IReadOnlyList<CarriedFile> ?? [];

    /// <summary>The names of what is carried, for deciding while it hovers: paths where there are paths.</summary>
    public static IReadOnlyList<string> FileNames(this IDragPayload payload)
        => payload.Files() is { Count: > 0 } files ? files : [.. payload.Contents().Select(file => file.Name)];

    /// <summary>
    /// The files as paths that outlive the drag: the paths carried, or what came as content written to the staging
    /// folder (<see cref="DropStaging"/>). For a drop, never a hover — it may write.
    /// </summary>
    public static IReadOnlyList<string> FilesOnDisk(this IDragPayload payload)
        => payload.Files() is { Count: > 0 } files ? files
         : payload.Contents() is { Count: > 0 } contents ? DropStaging.Default.Save(contents)
         : [];

    public static string? Link(this IDragPayload payload) => payload.Read(DragKinds.Link) as string;

    public static string? Text(this IDragPayload payload) => payload.Read(DragKinds.Text) as string;

    public static string? Html(this IDragPayload payload) => payload.Read(DragKinds.Html) as string;

    public static byte[]? Image(this IDragPayload payload) => payload.Read(DragKinds.Image) as byte[];
}
