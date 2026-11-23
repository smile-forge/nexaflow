namespace Nexaflow.Visuals.Common.Viewing;

/// <summary>
/// Marks a view as the one that draws <see cref="ModelType"/>. The model is whatever a contract hands out to be shown (a
/// page's view-model, a preview, a viewlet), and it knows nothing of the view: which toolkit draws it, and how, is decided
/// here, on the view's side, so a model is drawn by WPF today and by another toolkit's view of it later.
/// <para>
/// The view takes the model in its one constructor that takes a model, of whatever accessibility. A model whose own type no view names is drawn by the view of
/// the nearest type it derives from.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ViewForAttribute(Type modelType) : Attribute
{
    public Type ModelType { get; } = modelType;
}
