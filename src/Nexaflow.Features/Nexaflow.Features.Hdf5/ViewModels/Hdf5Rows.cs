namespace Nexaflow.Features.Hdf5.ViewModels;

/// <summary>One label/value line of the details drawer. The label is in the user's language, the value as the file says.</summary>
public sealed record Hdf5DetailRow(string Label, string Value);

/// <summary>
/// One attribute in the drawer: its name, type and value, and a note when the value is only a preview or could
/// not be read.
/// </summary>
public sealed record Hdf5AttributeRow(string Name, string TypeText, string Value, string? Note)
{
    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool Matches(string filter) =>
        filter.Length == 0
        || Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Value.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

/// <summary>How many members of one kind a group holds.</summary>
public sealed record Hdf5MemberCount(string Kind, int Count);

/// <summary>What the content pane shows for the selected object.</summary>
public enum Hdf5ContentKind { None, Group, Table, Message }
