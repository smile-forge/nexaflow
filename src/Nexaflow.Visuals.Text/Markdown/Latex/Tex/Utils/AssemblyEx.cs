using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace XamlMath.Utils;

internal static class AssemblyEx
{
    public static Stream ReadResource(this Assembly assembly, string resourceName) =>
        assembly.GetManifestResourceStream(resourceName)
        ?? throw new Exception($"Cannot find resource {resourceName} in assembly {assembly}.");

    /// <summary>
    /// The root element of an embedded XML resource.
    /// <para>
    /// Every caller reads a resource compiled into this assembly and immediately wants its root, so an
    /// absent resource or a document without one is a packaging fault rather than a runtime condition
    /// anyone can handle — the same reasoning that already makes <see cref="ReadResource"/> throw instead
    /// of returning null. Checking it here is what lets those callers hold a non-nullable
    /// <see cref="XElement"/>: assigning <see cref="XDocument.Root"/> straight to one silenced the
    /// compiler by deferring the failure to a <see cref="NullReferenceException"/> raised later, from a
    /// parse method, with nothing in it to say which file was wrong.
    /// </para>
    /// </summary>
    public static XElement ReadResourceRoot(this Assembly assembly, string resourceName)
    {
        using var stream = assembly.ReadResource(resourceName);
        return XDocument.Load(stream).Root
            ?? throw new Exception($"Resource {resourceName} in assembly {assembly} has no root element.");
    }
}
