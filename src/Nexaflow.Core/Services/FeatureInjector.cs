using System.Reflection;
using System.Runtime.ExceptionServices;
using Nexaflow.Core.Models;

namespace Nexaflow.Core.Services;

/// <summary>
/// Builds a feature's types for a workspace runtime: each through its richest constructor whose parameters
/// <see cref="Injectables"/> supplies, each kept in the runtime's <see cref="FeatureScope"/> and handed out again until the
/// runtime renews its services or ends.
/// </summary>
/// <param name="injectables">What a feature's constructor can be given.</param>
public sealed class FeatureInjector(FeatureInjectables injectables)
{
    /// <summary>What a feature's constructor can be given.</summary>
    public FeatureInjectables Injectables => injectables;

    /// <summary>
    /// The instance of <paramref name="targetType"/> built for <paramref name="workspace"/> — built the first time, and kept in its
    /// <see cref="WorkspaceRuntime.Features"/> until the runtime renews its services or ends.
    /// <para>
    /// <b>Never returns null — it throws.</b> Every way this can fail is a wiring defect: an unknown constructor parameter, a global
    /// config that never registered, a scoped config whose load threw, a constructor that threw, or a runtime that has already
    /// ended. Returning null for any of those is how a feature silently disappears — the caller enumerating implementations just
    /// skips it, nothing logs, and the symptom surfaces months later as "that never worked".
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The type has no public constructor, no constructor whose parameters can be
    /// satisfied, or the runtime has ended.</exception>
    public object Instantiate(Type targetType, WorkspaceRuntime workspace)
        => workspace.Features.GetOrBuild(targetType, () => BuildOrThrow(targetType, workspace));

    /// <summary>An instance of each of <paramref name="types"/> for <paramref name="workspace"/>, as <typeparamref name="T"/>.</summary>
    public IReadOnlyList<T> Instantiate<T>(IReadOnlyList<Type> types, WorkspaceRuntime workspace)
    {
        var result = new List<T>(types.Count);
        foreach (var type in types)
            if (Instantiate(type, workspace) is T instance)
                result.Add(instance);
        return result;
    }

    private object BuildOrThrow(Type t, WorkspaceRuntime workspace)
    {
        var ctors = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).ToList();
        if (ctors.Count == 0)
            throw new InvalidOperationException(
                $"The feature DI cannot build {t.FullName}: it has no public constructor.");

        // Longest-first, so an optional dependency is taken when it can be. The reason the FIRST failure is
        // the one reported: overloads are tried in descending arity, so it names the richest constructor the
        // author meant to be used rather than whatever minimal one happened to be last.
        string? defect = null;

        foreach (var ctor in ctors)
        {
            var args = TryResolveArgs(ctor, workspace, out var detail);
            if (args is null) { defect ??= detail; continue; }

            try { return ctor.Invoke(args)!; }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                // A constructor that throws is a defect in the feature, and swallowing it here would present
                // as the feature simply not existing. Rethrow the real exception with its original stack.
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;   // unreachable; satisfies definite-assignment
            }
        }

        throw new InvalidOperationException(
            $"The feature DI cannot build {t.FullName}: {defect}. It implements a discovered contract, so "
            + "returning null instead would leave the caller enumerating implementations to skip it in "
            + "silence, and the feature would look like it was never written.");
    }

    /// <summary>The constructor arguments, or null with <paramref name="defect"/> describing why none could be found. Every failure
    /// here is a defect rather than a tolerable state.</summary>
    private object?[]? TryResolveArgs(ConstructorInfo ctor, WorkspaceRuntime workspace, out string? defect)
    {
        var parms = ctor.GetParameters();
        var args  = new object?[parms.Length];
        defect    = null;

        for (int i = 0; i < parms.Length; i++)
        {
            var asked = parms[i].ParameterType;
            var name  = parms[i].Name;

            if (injectables.Supply(asked, workspace) is { } supplied)
            {
                if (supplied.Defect is { } refused)
                {
                    defect = $"{refused} (parameter '{name}')";
                    return null;
                }
                args[i] = supplied.Value;
            }
            else if (parms[i].IsOptional)
            {
                args[i] = Type.Missing;
            }
            else
            {
                defect = $"parameter '{name}' is of type '{asked.Name}', which the feature DI does not supply (it supplies "
                       + $"{injectables.Named}). If you meant a subfeature set, check that at least one type carrying [Subfeature] "
                       + "implements that contract and shipped as a scanned Nexaflow.Features.*.dll";
                return null;
            }
        }
        return args;
    }
}
