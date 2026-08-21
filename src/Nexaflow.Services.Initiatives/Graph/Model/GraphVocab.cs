namespace Nexaflow.Services.Initiatives.Graph.Model;

/// <summary>
/// The graph is a <b>superset of graphify</b>: an open string vocabulary for node/edge/role kinds (so new
/// kinds never force a schema migration, exactly like <c>Snaplink.Type</c>), plus a numeric confidence ladder
/// that downcasts to graphify's EXTRACTED/INFERRED/AMBIGUOUS enum on export.
/// </summary>
public static class NodeType
{
    public const string Product  = "product";
    public const string File     = "file";
    public const string Type     = "type";      // class / struct / interface / enum
    public const string Member   = "member";    // method / property / field / ctor
    public const string External = "external";  // unresolved base / library symbol
}

/// <summary>Binary <see cref="GraphEdge.Relationship"/> values (open vocabulary).</summary>
public static class EdgeRelationship
{
    public const string Contains     = "contains";      // structural: product tree, file→type→member
    public const string Extends       = "extends";       // class inheritance
    public const string Implements    = "implements";    // interface implementation
    public const string Imports        = "imports";       // resolved file→file import
    public const string Calls          = "calls";         // inferred method invocation (member → member)
    public const string References    = "references";    // inferred type/member usage
    public const string Instantiates  = "instantiates";  // inferred `new X` (member → type)
    public const string Tests          = "tests";         // snaplink-derived
    public const string Documents      = "documents";     // snaplink-derived
    public const string ViewOf         = "view_of";       // a .xaml view → the code-behind class it declares (x:Class)
    public const string Handles        = "handles";       // a XAML element → the code-behind method wired to one of its events
    public const string UsesResource   = "uses_resource"; // a XAML element → the x:Key it resolves ({StaticResource …})
    public const string BindsTo        = "binds_to";      // a XAML element → the ViewModel member a {Binding} names
    public const string DependsOn      = "depends_on";    // a .csproj → a referenced project / package
    public const string Mentions       = "mentions";      // a member → a repo file named in one of its string literals (a hint)
}

/// <summary>N-ary <see cref="GraphHyperEdge.Relationship"/> values.</summary>
public static class HyperRelationship
{
    public const string Calls     = "calls";      // caller → callee, args[]
    public const string Signature = "signature";  // member → return, params[]
    public const string Annotated = "annotated";  // target → attr, named-args[]
}

/// <summary><see cref="HyperEndpoint.Role"/> values.</summary>
public static class EndpointRole
{
    public const string Caller   = "caller";
    public const string Callee   = "callee";
    public const string Arg      = "arg";
    public const string Member   = "member";
    public const string Return   = "return";
    public const string Param    = "param";
    public const string Target   = "target";
    public const string Attr     = "attr";
    public const string NamedArg = "named_arg";
}

/// <summary>
/// The confidence ladder. <c>Extracted</c> = syntactically explicit and endpoint-unambiguous; the inferred
/// rungs score a name-resolved cross-file target by how many plausible declarations it matched and whether the
/// target's scope is in view (a <c>using</c>/import). Downcasts on export: 1.0 → EXTRACTED, ≥0.6 → INFERRED,
/// &lt;0.6 → AMBIGUOUS.
/// </summary>
public static class GraphConfidence
{
    public const double Extracted   = 1.00;  // explicit syntax, unambiguous endpoint
    public const double NearCertain = 0.95;  // one match, in scope
    public const double Strong      = 0.85;  // one match globally (unique name)
    public const double Reasonable  = 0.75;  // contextual small set (receiver-typed guess)
    public const double Weak        = 0.65;  // multiple matches / overload set
    public const double Speculative = 0.55;  // no declaration found → external
}
