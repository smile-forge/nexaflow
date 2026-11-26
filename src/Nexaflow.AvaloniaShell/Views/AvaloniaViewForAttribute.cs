namespace Nexaflow.AvaloniaShell.Views;

/// <summary>
/// Marks an Avalonia view as drawing models of <see cref="ModelType"/>, its constructor's one parameter: the marker this app hands
/// the session as <see cref="ShellPlatform.ViewMarker"/>, so the feature catalogue indexes these views where the WPF app's
/// <c>[ViewFor]</c> indexes its own.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AvaloniaViewForAttribute(Type modelType) : Attribute
{
    /// <summary>The model the marked view draws.</summary>
    public Type ModelType { get; } = modelType;
}
