using System.Collections.Concurrent;
using System.Reflection;
using System.Windows;

namespace Nexaflow.Visuals.Common.Viewing;

/// <summary>
/// Makes the view that draws a model: the one marked <see cref="ViewForAttribute"/> for its type, or for the nearest type
/// it derives from. Which views exist is the host's to say through <paramref name="viewNaming"/> (the app's catalog of
/// assemblies, or a test's list), so views can live in any assembly the host knows, apart from their models.
/// <para>
/// Each model type is looked up once; after that a view costs its own constructor.
/// </para>
/// </summary>
/// <param name="viewNaming">The view type marked as drawing exactly this model type, or null.</param>
public sealed class ViewRegistry(Func<Type, Type?> viewNaming)
{
    private readonly ConcurrentDictionary<Type, Func<object, FrameworkElement>?> _makers = new();

    /// <summary>
    /// The registry a window hands down to everything in it, so a control deep inside a feature (a <see cref="ModelHost"/>)
    /// draws models with the app's views without being given the registry, and without a static one.
    /// </summary>
    public static readonly DependencyProperty RegistryProperty = DependencyProperty.RegisterAttached(
        "Registry", typeof(ViewRegistry), typeof(ViewRegistry),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>The registry handed down to <paramref name="element"/>; null outside a window that hands one down.</summary>
    public static ViewRegistry? GetRegistry(DependencyObject element) => (ViewRegistry?)element.GetValue(RegistryProperty);

    /// <summary>Hands <paramref name="registry"/> down to <paramref name="element"/> and everything in it.</summary>
    public static void SetRegistry(DependencyObject element, ViewRegistry? registry) => element.SetValue(RegistryProperty, registry);

    /// <summary>The model a view made here draws, for whatever reaches the view and needs what it shows (a drop, a key).</summary>
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.RegisterAttached("Model", typeof(object), typeof(ViewRegistry), new PropertyMetadata(null));

    /// <summary>The model <paramref name="view"/> was made to draw; null for an element no registry made.</summary>
    public static object? ModelOf(DependencyObject view) => view.GetValue(ModelProperty);

    /// <summary>Whether some view draws models of <paramref name="modelType"/>.</summary>
    public bool Draws(Type modelType) => MakerFor(modelType) is not null;

    /// <summary>A new view of <paramref name="model"/>. Throws when no view draws it, naming the model so the gap is plain.</summary>
    public FrameworkElement Create(object model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var make = MakerFor(model.GetType())
            ?? throw new InvalidOperationException(
                $"No view draws {model.GetType().FullName}: mark the view that does [ViewFor(typeof({model.GetType().Name}))].");
        var view = make(model);
        view.SetValue(ModelProperty, model);
        return view;
    }

    private Func<object, FrameworkElement>? MakerFor(Type modelType) => _makers.GetOrAdd(modelType, Find);

    private Func<object, FrameworkElement>? Find(Type modelType)
    {
        for (var type = modelType; type is not null && type != typeof(object); type = type.BaseType)
            if (viewNaming(type) is { } view) return MakerOf(view, modelType);
        return null;
    }

    private static Func<object, FrameworkElement> MakerOf(Type view, Type modelType)
    {
        if (!typeof(FrameworkElement).IsAssignableFrom(view))
            throw new InvalidOperationException($"{view.FullName} is marked as drawing {modelType.FullName} but is not an element.");

        var made = view.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(ctor => ctor.GetParameters() is [var only] && only.ParameterType.IsAssignableFrom(modelType))
            .ToList();
        if (made is not [var ctor])
            throw new InvalidOperationException(
                $"{view.FullName} draws {modelType.FullName}, so it needs exactly one constructor taking it; it has {made.Count}.");

        // Not wrapped: a view whose constructor fails reports its own exception, not reflection's.
        return model => (FrameworkElement)ctor.Invoke(BindingFlags.DoNotWrapExceptions, null, [model], null);
    }
}
