using Nexaflow.Plugins;
using System;
using System.Collections.Generic;

namespace Nexaflow.Core.Services;

/// <summary>
/// Recognises the two constructor-parameter shapes by which a host feature asks for its subfeatures.
/// Kept out of <see cref="FeatureManager"/> so the DI resolver stays a flat list of readable cases.
/// </summary>
/// <remarks>
/// Both shapes were previously <i>unresolvable</i> parameter types — the DI reported them as a defect — so
/// recognising them changes nothing that used to work.
/// </remarks>
public static class SubfeatureArg
{
    /// <summary>
    /// <c>IReadOnlyList&lt;ISubfeatureHandle&lt;T&gt;&gt;</c> (or <c>IReadOnlyCollection</c> /
    /// <c>IEnumerable</c> of the same) → <c>T</c>. The lazy form.
    /// </summary>
    public static bool IsHandleList(Type parameterType, out Type? contract)
    {
        contract = null;
        if (!TryElement(parameterType, out var element)) return false;
        if (!element!.IsGenericType || element.GetGenericTypeDefinition() != typeof(ISubfeatureHandle<>))
            return false;

        contract = element.GetGenericArguments()[0];
        return true;
    }

    /// <summary>
    /// <c>IReadOnlyList&lt;T&gt;</c> (or <c>IReadOnlyCollection</c> / <c>IEnumerable</c> of the same) where
    /// <c>T</c> is an interface → <c>T</c>. The eager form.
    /// </summary>
    /// <remarks>
    /// Gated on <c>T</c> being an interface so an ordinary <c>IReadOnlyList&lt;string&gt;</c> parameter still
    /// reports the normal "the feature DI does not supply this" defect instead of being silently handed an
    /// empty list. A contract that no plugin implements yields an empty list, which is the correct answer —
    /// "no plugins are installed" is a legitimate state, not a defect.
    /// </remarks>
    public static bool IsInstanceList(Type parameterType, out Type? contract)
    {
        contract = null;
        if (!TryElement(parameterType, out var element)) return false;
        if (!element!.IsInterface) return false;
        if (element.IsGenericType && element.GetGenericTypeDefinition() == typeof(ISubfeatureHandle<>))
            return false;   // that's the handle form; IsHandleList owns it

        contract = element;
        return true;
    }

    /// <summary>The element type of a read-only sequence parameter, or false if it isn't one.</summary>
    private static bool TryElement(Type t, out Type? element)
    {
        element = null;
        if (!t.IsGenericType) return false;

        var def = t.GetGenericTypeDefinition();
        if (def != typeof(IReadOnlyList<>) && def != typeof(IReadOnlyCollection<>) && def != typeof(IEnumerable<>))
            return false;

        element = t.GetGenericArguments()[0];
        return true;
    }
}
