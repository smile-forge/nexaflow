namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// One test's declared coverage of a product node, harvested from a <c>[CoversNode]</c> attribute. The
/// shape mirrors what a <c>type:"code"</c> <see cref="Snaplink"/> needs, so the Integrity page's "Add"
/// affordance can turn a ref straight into a snaplink: <see cref="File"/>→<c>doc</c>,
/// <see cref="Class"/>→<c>class</c> (simple name), <see cref="Method"/>→<c>method</c>.
/// </summary>
public sealed class TestRef
{
    /// <summary>Simple name of the test assembly the declaration was found in (for auditing/grouping).</summary>
    public string Assembly { get; set; } = string.Empty;

    /// <summary>Fully-qualified test class name (namespace + type). The snaplink uses only the simple name.</summary>
    public string Class { get; set; } = string.Empty;

    /// <summary>Test method the attribute sat on, or null for a class-level declaration.</summary>
    public string? Method { get; set; }

    /// <summary>Repo-relative path to the test source file, or empty when the PDB couldn't resolve it.</summary>
    public string File { get; set; } = string.Empty;
}
