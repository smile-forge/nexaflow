namespace Nexaflow.Interaction;

/// <summary>A file a drag carries as content. Its name is whatever the source said, so it is never used as a path as it is.</summary>
/// <param name="Name">Its name, or its path within what was dragged.</param>
/// <param name="IsFolder">Whether it is a folder the files after it sit in.</param>
/// <param name="CopyTo">Writes its content into the stream given; valid only as long as the drag is.</param>
public sealed record CarriedFile(string Name, bool IsFolder, Action<Stream> CopyTo);
