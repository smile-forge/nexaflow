using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Nexaflow.Markdown.Binding;

/// <summary>
/// An <see cref="IDataContext"/> over an ordinary object: <c>A.B.C</c> walks properties and fields, <c>A[0]</c> and
/// <c>A[key]</c> index a list or a dictionary, and <c>A.Method()</c> calls a method the host marked
/// <see cref="BindableAttribute"/>.
///
/// <para>
/// Null-propagating the whole way: a step that finds nothing ends the walk with no value, and nothing here throws,
/// because the path came out of a document and a document can say anything.
/// </para>
/// </summary>
public sealed class ReflectionDataContext(object? root) : IDataContext
{
    /// <summary>What each type answers to, worked out once per type and name.</summary>
    private static readonly ConcurrentDictionary<(Type Type, string Name), MemberInfo?> Known = new();

    /// <inheritdoc/>
    public bool TryGet(string path, out object? value)
    {
        value = null;
        if (root is null || string.IsNullOrWhiteSpace(path)) return false;

        var at = root;
        foreach (var step in Steps(path))
        {
            if (at is null || !Step(at, step, out at)) return false;
        }

        value = at;
        return true;
    }

    /// <summary>
    /// The steps of a path, in order: <c>A.B[0].C()</c> is <c>A</c>, <c>B</c>, <c>[0]</c>, <c>C()</c>. Written out
    /// rather than split, because a key may hold a dot.
    /// </summary>
    private static IEnumerable<string> Steps(string path)
    {
        var at = 0;
        while (at < path.Length)
        {
            if (path[at] is '.' or ' ') { at++; continue; }

            if (path[at] == '[')
            {
                var shut = path.IndexOf(']', at);
                if (shut < 0) yield break;

                yield return path[at..(shut + 1)];
                at = shut + 1;
                continue;
            }

            var end = at;
            while (end < path.Length && path[end] is not ('.' or '[')) end++;

            var step = path[at..end].Trim();
            if (step.Length > 0) yield return step;
            at = end;
        }
    }

    /// <summary>One step of the walk, and whether it landed anywhere.</summary>
    private static bool Step(object on, string step, out object? found)
    {
        found = null;

        if (step.Length > 1 && step[0] == '[' && step[^1] == ']')
            return Indexed(on, step[1..^1].Trim().Trim('"', '\''), out found);

        if (step.EndsWith("()", StringComparison.Ordinal))
            return Called(on, step[..^2].TrimEnd(), out found);

        return Named(on, step, out found);
    }

    /// <summary>A property, a field, or a key of a dictionary the object is.</summary>
    private static bool Named(object on, string name, out object? found)
    {
        found = null;

        switch (Member(on.GetType(), name))
        {
            case PropertyInfo property when property.GetIndexParameters().Length == 0:
                return Read(() => property.GetValue(on), out found);

            case FieldInfo field:
                return Read(() => field.GetValue(on), out found);
        }

        return Indexed(on, name, out found);
    }

    /// <summary>A zero-argument method the host said may be called.</summary>
    private static bool Called(object on, string name, out object? found)
    {
        found = null;

        return Member(on.GetType(), name) is MethodInfo method
            && method.GetCustomAttribute<BindableAttribute>() is not null
            && Read(() => method.Invoke(on, null), out found);
    }

    /// <summary>A key of a dictionary, or a place in a list.</summary>
    private static bool Indexed(object on, string key, out object? found)
    {
        found = null;

        if (on is IDictionary plain)
            return plain.Contains(key) && Read(() => plain[key], out found);

        if (on is IList list)
            return int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)
                && at >= 0 && at < list.Count
                && Read(() => list[at], out found);

        return false;
    }

    /// <summary>
    /// What a type answers to by that name: a property, a field or a method, whichever it has — the walk decides
    /// which of those it wanted. Case-insensitive, since a path is written by hand.
    /// </summary>
    private static MemberInfo? Member(Type type, string name) =>
        Known.GetOrAdd((type, name), static key =>
        {
            const BindingFlags How = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

            foreach (var member in key.Type.GetMember(key.Name, How))
                if (member is PropertyInfo or FieldInfo or MethodInfo)
                    return member;

            foreach (var member in key.Type.GetMembers(How))
                if (member is PropertyInfo or FieldInfo or MethodInfo
                    && string.Equals(member.Name, key.Name, StringComparison.OrdinalIgnoreCase))
                    return member;

            return null;
        });

    /// <summary>Reads what a member gives, and says nothing came of it where reading it threw.</summary>
    private static bool Read(Func<object?> reading, out object? found)
    {
        try
        {
            found = reading();
            return true;
        }
        catch
        {
            found = null;
            return false;
        }
    }
}
