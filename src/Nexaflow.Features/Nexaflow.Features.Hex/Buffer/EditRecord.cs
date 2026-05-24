namespace Nexaflow.Features.Hex.Buffer;

public enum EditKind { Overwrite, Insert, Delete }

public readonly record struct EditRecord(long Offset, byte OldValue, byte NewValue, EditKind Kind);
