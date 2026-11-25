using Nexaflow.Core.Models;

namespace Nexaflow.Core.Services;

/// <summary>
/// What a feature's constructor can be given, by the type each of its parameters asks for: the one registry the feature injection
/// reads. The session says once what it supplies — the runtime's own services, the app-wide tools, the features' configs and
/// services, their subfeature sets — and a parameter nothing supplies is a defect, named with everything that is.
/// </summary>
public sealed class FeatureInjectables
{
    private readonly List<Supplier> _suppliers = [];
    private readonly Lock _gate = new();

    /// <summary>Supplies a parameter of <typeparamref name="T"/>, or of any type <typeparamref name="T"/> is, with what
    /// <paramref name="supply"/> gives for the runtime a feature is built for.</summary>
    /// <param name="named">How the defect that lists what is supplied names it.</param>
    public void Add<T>(string named, Func<WorkspaceRuntime, T> supply)
        => Add(named, asked => asked.IsAssignableFrom(typeof(T)), (_, runtime) => Supplied.With(supply(runtime)));

    /// <summary>Supplies every parameter type <paramref name="asks"/> accepts — a family of types, such as each
    /// <c>ILogger&lt;T&gt;</c> — with what <paramref name="supply"/> gives for the type asked and the runtime.</summary>
    /// <param name="named">How the defect that lists what is supplied names it.</param>
    public void Add(string named, Func<Type, bool> asks, Func<Type, WorkspaceRuntime, Supplied> supply)
    {
        lock (_gate) _suppliers.Add(new Supplier(named, asks, supply));
    }

    /// <summary>What is supplied for a parameter of <paramref name="asked"/> on <paramref name="runtime"/>: from the first supplier
    /// that takes it, in the order they were added; null when none does.</summary>
    internal Supplied? Supply(Type asked, WorkspaceRuntime runtime)
    {
        Supplier[] suppliers;
        lock (_gate) suppliers = [.. _suppliers];
        foreach (var supplier in suppliers)
            if (supplier.Asks(asked)) return supplier.Supply(asked, runtime);
        return null;
    }

    /// <summary>Everything supplied, as a defect lists it.</summary>
    public string Named
    {
        get { lock (_gate) return string.Join(", ", _suppliers.Select(s => s.Named).Distinct()); }
    }

    private sealed record Supplier(string Named, Func<Type, bool> Asks, Func<Type, WorkspaceRuntime, Supplied> Supply);
}

/// <summary>What a supplier gives for a constructor parameter: a value, or why it cannot give one.</summary>
public readonly record struct Supplied(object? Value, string? Defect)
{
    /// <summary>The value given.</summary>
    public static Supplied With(object? value) => new(value, null);

    /// <summary>Nothing can be given, for <paramref name="defect"/>.</summary>
    public static Supplied Refused(string defect) => new(null, defect);
}
