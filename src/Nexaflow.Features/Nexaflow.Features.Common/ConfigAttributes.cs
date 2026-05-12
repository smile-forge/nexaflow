namespace Nexaflow.Features.Common;

/// <summary>Friendly label shown for a property row in the Options panel.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigDisplayNameAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}

/// <summary>
/// Marks a string property as a filesystem folder path.
/// The Options panel renders a TextBox with a "…" browse button and validates that the path exists.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FolderPathAttribute : Attribute { }

/// <summary>
/// Marks a string property as list-sourced.
/// The Options panel invokes <see cref="SourceType"/>.<see cref="MethodName"/>() —
/// a public static parameterless method — and uses the returned <see cref="IEnumerable{T}"/>
/// of strings as ComboBox items.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ListSourceAttribute(Type sourceType, string methodName) : Attribute
{
    public Type   SourceType { get; } = sourceType;
    public string MethodName { get; } = methodName;

    public IEnumerable<string> Invoke()
    {
        var m = SourceType.GetMethod(MethodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return m?.Invoke(null, null) as IEnumerable<string> ?? [];
    }
}
