using System;

namespace Nexaflow.Markdown.Binding;

/// <summary>
/// Where a <c>{{…}}</c> written in a diagram gets its value — the host's own object, asked by path.
///
/// <para>
/// An interface rather than reflection outright, so a host that will not have its objects walked can answer from a
/// dictionary, a view-model's own indexer, or anything else it likes.
/// </para>
/// </summary>
public interface IDataContext
{
    /// <summary>
    /// What <paramref name="path"/> comes to, and whether it came to anything. A path naming nothing answers false
    /// rather than throwing: a diagram that falls over on a typo is worse than one showing a gap.
    /// </summary>
    bool TryGet(string path, out object? value);
}

/// <summary>
/// Says a method may be called by a <c>{{…}}</c>.
///
/// <para>
/// Properties and fields are read freely, because reading one does nothing. A call is arbitrary code driven by a
/// string in a document, which is fine against a host's own view-model and not fine in a document that came from
/// anywhere — so the host says which of its methods it meant, one at a time.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class BindableAttribute : Attribute;
