using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Graphs;

/// <summary>
/// One member row of a UML class box — an attribute or a method, already formatted for display
/// (the visibility symbol is kept, Mermaid generics <c>~T~</c> are converted to <c>&lt;T&gt;</c>).
/// <see cref="IsStatic"/> underlines the row and <see cref="IsAbstract"/> italicises it, per UML.
/// </summary>
public sealed class ClassMember
{
    public required string Text { get; init; }
    public bool IsStatic   { get; init; }
    public bool IsAbstract { get; init; }

    /// <summary>Optional navigation target for the row. When set, the renderer draws the member as a link
    /// (accent colour, hand cursor) and a click routes through the markdown <c>OnNavigate</c> hook. Carried in
    /// the Mermaid source as a trailing <c>… @@&lt;url&gt;</c> token (stripped from <see cref="Text"/>), which
    /// "As Code" uses to point each member at its definition line.</summary>
    public string? Href { get; init; }
}

/// <summary>A lollipop interface attached to a class — a small circle on a short straight stub off the
/// class box, labelled with the interface name (it is a decoration, not a graph node). <see cref="Below"/>
/// hangs it under the box (the provided-interface / target side, <c>A --() iface</c>); otherwise it sits
/// above (the <c>iface ()-- A</c> side).</summary>
public sealed record Lollipop(string Name, bool Below);

/// <summary>
/// The structured payload of a <see cref="NodeShape.ClassBox"/> node: an optional «stereotype»
/// (<c>&lt;&lt;interface&gt;&gt;</c>, <c>&lt;&lt;abstract&gt;&gt;</c>, …), the attribute / method
/// compartments, and any lollipop interfaces. The class name itself is the owning <see cref="Node"/>'s <c>Label</c>.
/// </summary>
public sealed class ClassInfo
{
    public string? Stereotype { get; set; }
    public List<ClassMember> Attributes { get; } = [];
    public List<ClassMember> Methods    { get; } = [];
    public List<Lollipop>    Lollipops  { get; } = [];

    /// <summary>When true the box draws a single member compartment (the attributes) under the header,
    /// with no second (methods) compartment — used by requirement diagrams, whose boxes are
    /// «type» + name over one list of <c>key: value</c> fields.</summary>
    public bool SingleCompartment { get; set; }

    /// <summary>True when the class has any attribute or method — i.e. it draws the extra
    /// compartment(s) rather than just a name box.</summary>
    public bool HasMembers => Attributes.Count > 0 || Methods.Count > 0;
}

/// <summary>
/// Geometry for class boxes, shared by <c>SugiyamaLayout</c> (reserves space) and
/// <c>WpfGraphRenderer</c> (draws compartments) so the reserved size and the drawn size agree.
/// A char-width heuristic (no <c>FormattedText</c>) keeps it consistent with the rest of the layout.
/// </summary>
public static class ClassBoxMetrics
{
    public const double RowH = 18;   // height of one name / member row
    public const double PadX = 12;   // horizontal text padding inside the box
    public const double PadV = 5;    // vertical padding at the top & bottom of each compartment
    public const double MinW = 96;   // a class box never narrower than this
    public const double MinH = 34;   // … nor shorter

    // ── Lollipop interfaces (drawn off the box, so the layout must reserve room) ──
    public const double LollipopStub = 20;   // length of the straight stub from the box to the circle
    public const double LollipopR    = 5.5;  // circle radius
    public const double LollipopBand = 52;   // vertical room reserved on a side that carries lollipops
    public const double LollipopGapX = 50;   // horizontal spacing between lollipops on the same side

    private const double CharW = 6.8;

    /// <summary>
    /// Converts Mermaid generic syntax to display form: <c>List~int~</c> → <c>List&lt;int&gt;</c>, and
    /// nested <c>List~List~int~~</c> → <c>List&lt;List&lt;int&gt;&gt;</c>. Each <c>~</c> becomes an opening
    /// <c>&lt;</c> when it is immediately followed by a type-name character, otherwise a closing <c>&gt;</c>.
    /// (A leading visibility <c>~</c> is stripped by the parser before this runs, so it isn't misread.)
    /// </summary>
    public static string Generics(string s)
    {
        if (!s.Contains('~')) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '~') { sb.Append(s[i]); continue; }
            bool open = i + 1 < s.Length && (char.IsLetterOrDigit(s[i + 1]) || s[i + 1] == '_');
            sb.Append(open ? '<' : '>');
        }
        return sb.ToString();
    }

    /// <summary>The drawn (width, height) of just the class box — name + compartments, no lollipops.</summary>
    public static (double w, double h) MeasureBox(string name, ClassInfo info)
    {
        double maxChars = name.Length * 1.15;                       // the name renders bold → a touch wider
        if (info.Stereotype is { Length: > 0 } st)
            maxChars = Math.Max(maxChars, st.Length + 2);           // + the two guillemets
        foreach (var m in info.Attributes) maxChars = Math.Max(maxChars, m.Text.Length);
        foreach (var m in info.Methods)    maxChars = Math.Max(maxChars, m.Text.Length);

        double w = Math.Max(MinW, maxChars * CharW + 2 * PadX);

        int    headerRows = 1 + (info.Stereotype is { Length: > 0 } ? 1 : 0);
        double h = headerRows * RowH + 2 * PadV;
        if (info.HasMembers)
        {
            h += info.Attributes.Count * RowH + 2 * PadV;
            if (!info.SingleCompartment)
                h += info.Methods.Count * RowH + 2 * PadV;
        }
        return (w, Math.Max(MinH, h) + 4);                          // + a little for the 1.5px border chrome
    }

    /// <summary>The full (width, height) the layout must reserve for the node — the box plus a band above
    /// and/or below for any lollipop interfaces, and extra width when several share a side.</summary>
    public static (double w, double h) Measure(string name, ClassInfo info)
    {
        var (bw, bh) = MeasureBox(name, info);
        if (info.Lollipops.Count == 0) return (bw, bh);

        int above = info.Lollipops.Count(l => !l.Below);
        int below = info.Lollipops.Count(l => l.Below);
        double h = bh + (above > 0 ? LollipopBand : 0) + (below > 0 ? LollipopBand : 0);
        double w = Math.Max(bw, Math.Max(above, below) * LollipopGapX);
        return (w, h);
    }
}
