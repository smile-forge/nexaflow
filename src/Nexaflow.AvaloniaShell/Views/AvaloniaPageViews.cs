using System.Collections.Concurrent;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.Logging;

namespace Nexaflow.AvaloniaShell.Views;

/// <summary>
/// The view each open page is drawn with, on Avalonia: made the first time the page is shown, by the view the feature catalogue
/// marks <see cref="AvaloniaViewForAttribute"/> as drawing its view-model, and kept until the page closes — Nexaflow.Core's
/// <c>PageViews</c> and <c>ViewRegistry</c>, over Avalonia controls.
/// <para>
/// A page no Avalonia view draws — every feature's today, whose views are WPF — is drawn as that gap, naming its view-model, rather
/// than left blank.
/// </para>
/// </summary>
internal sealed class AvaloniaPageViews(FeatureCatalog catalog, ILogger<AvaloniaPageViews> log) : IPageViews
{
    private readonly Dictionary<Page, Control> _views = [];
    private readonly ConcurrentDictionary<Type, ConstructorInfo?> _makers = new();

    /// <summary>The page's view, made on first use.</summary>
    public Control ViewOf(Page page)
    {
        if (_views.TryGetValue(page, out var view)) return view;
        view = page.GetOrCreateViewModel() is { } model ? Draw(model) : Failed(page.PageKind ?? "page", page.LoadException, null);
        _views[page] = view;
        return view;
    }

    /// <summary>A view of <paramref name="model"/> — a page's view-model, or an overlay's — or the gap where none draws it.</summary>
    public Control Draw(object model)
    {
        if (MakerFor(model.GetType()) is not { } maker) return Failed(model.GetType().Name, null, model.GetType());
        try
        {
            return (Control)maker.Invoke(BindingFlags.DoNotWrapExceptions, null, [model], null);
        }
        catch (Exception ex)
        {
            return Failed(model.GetType().Name, ex, null);
        }
    }

    public void Release(Page page)
    {
        if (_views.Remove(page, out var view) && view is IDisposable disposable)
            try { disposable.Dispose(); } catch { /* a view failing to let go must not stop the tab closing */ }
    }

    private ConstructorInfo? MakerFor(Type modelType) => _makers.GetOrAdd(modelType, Find);

    private ConstructorInfo? Find(Type modelType)
    {
        for (var type = modelType; type is not null && type != typeof(object); type = type.BaseType)
            if (catalog.ViewTypeFor(type) is { } view && typeof(Control).IsAssignableFrom(view))
                return view.GetConstructors().SingleOrDefault(ctor => ctor.GetParameters() is [var only] && only.ParameterType.IsAssignableFrom(modelType));
        return null;
    }

    /// <summary>What could not be drawn, said where it would have been: no Avalonia view for the model, or the exception that stopped it.</summary>
    private Control Failed(string what, Exception? error, Type? undrawn)
    {
        if (error is not null) log.LogError(error, "{What} could not be shown", what);
        var said = undrawn is not null
            ? $"No Avalonia view draws {undrawn.FullName}.\n\nAn Avalonia view marked [AvaloniaViewFor(typeof({undrawn.Name}))] would draw it here."
            : $"{what} could not be shown: {error?.Message ?? "its view-model could not be made."}";
        return new Border
        {
            Padding = new Avalonia.Thickness(24),
            Child = new TextBlock
            {
                Text = said,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.8,
            },
        };
    }
}
