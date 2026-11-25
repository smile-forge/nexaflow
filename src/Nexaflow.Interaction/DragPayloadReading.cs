namespace Nexaflow.Interaction;

/// <summary>The shared kinds, read as what they are.</summary>
public static class DragPayloadReading
{
    public static bool ContainsAny(this IDragPayload payload, IEnumerable<string> kinds) => kinds.Any(payload.Contains);

    /// <summary>Whether another application could have dragged this in, as against a gesture of the app's own.</summary>
    public static bool IsShareable(this IDragPayload payload) => payload.ContainsAny(DragKinds.Shared);

    /// <summary>Whether it carries files, by path or by content, or content already staged.</summary>
    public static bool CarriesFiles(this IDragPayload payload)
        => payload.Contains(DragKinds.Files) || payload.Contains(DragKinds.FileContents) || payload.Contains(DragKinds.StagedFiles);

    /// <summary>The paths carried; empty where there are none.</summary>
    public static IReadOnlyList<string> Files(this IDragPayload payload)
        => payload.Read(DragKinds.Files) as string[] ?? [];

    public static IReadOnlyList<CarriedFile> Contents(this IDragPayload payload)
        => payload.Read(DragKinds.FileContents) as IReadOnlyList<CarriedFile> ?? [];

    /// <summary>The names of what is carried, for deciding while it hovers: paths where there are paths.</summary>
    public static IReadOnlyList<string> FileNames(this IDragPayload payload)
            => payload.Files() is { Count: > 0 } files ? files
             : payload.Staged() is { Count: > 0 } staged ? staged
             : [.. payload.Contents().Select(file => file.Name)];

        /// <summary>The paths of content a taken payload staged (<see cref="DragKinds.StagedFiles"/>); empty where there are none.</summary>
        public static IReadOnlyList<string> Staged(this IDragPayload payload)
            => payload.Read(DragKinds.StagedFiles) as string[] ?? [];

    /// <summary>
    /// The files as paths that outlive the drag: the paths carried, content a taken payload already staged, or what came as
    /// content written to the staging folder (<see cref="DropStaging"/>) now. For a drop, never a hover — it may write.
    /// </summary>
    public static IReadOnlyList<string> FilesOnDisk(this IDragPayload payload)
        => payload.Files() is { Count: > 0 } files ? files
         : payload.Staged() is { Count: > 0 } staged ? staged
         : payload.Contents() is { Count: > 0 } contents ? DropStaging.Default.Save(contents)
         : [];

    /// <summary>
    /// What <paramref name="payload"/> carries of <paramref name="kinds"/>, read in full while it can still be read: a payload
    /// that outlives the drag, or the clipboard's next change, so what takes it may wait before using it. Files that came as
    /// content are written to <paramref name="staging"/> and carried as <see cref="DragKinds.StagedFiles"/>; files by path
    /// keep whether they were cut (<see cref="DragKinds.FilesCut"/>), which travels with them. A kind it does not carry is
    /// left out. It reads and may write, so it is for a drop or a paste, never a hover.
    /// </summary>
    public static ComposedPayload Take(this IDragPayload payload, IEnumerable<string> kinds, DropStaging staging)
    {
        var taken = ComposedPayload.Empty;
        foreach (var kind in kinds.Distinct(StringComparer.Ordinal))
        {
            if (!payload.Contains(kind)) continue;
            switch (kind)
            {
                case DragKinds.FileContents:
                    if (!payload.Contains(DragKinds.Files) && staging.Save(payload.Contents()) is { Count: > 0 } staged)
                        taken = taken.With(DragKinds.StagedFiles, staged.ToArray());
                    break;
                case DragKinds.Files:
                    taken = taken.With(DragKinds.Files, payload.Files().ToArray());
                    if (payload.Contains(DragKinds.FilesCut)) taken = taken.With(DragKinds.FilesCut, true);
                    break;
                default:
                    if (payload.Read(kind) is { } value) taken = taken.With(kind, value);
                    break;
            }
        }
        return taken;
    }

    public static string? Link(this IDragPayload payload) => payload.Read(DragKinds.Link) as string;

    public static string? Text(this IDragPayload payload) => payload.Read(DragKinds.Text) as string;

    public static string? Html(this IDragPayload payload) => payload.Read(DragKinds.Html) as string;

    public static byte[]? Image(this IDragPayload payload) => payload.Read(DragKinds.Image) as byte[];
}
