using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;

namespace Nexaflow.Icons;

/// <summary>
/// Reads the SVG of a pack's icon — the elements inside its <c>&lt;svg&gt;</c>, which is how Iconify keeps an icon — into the
/// layers it draws (<see cref="VectorLayer"/>), every outline carried into the icon's own coordinates.
/// <para>
/// It reads what icons are drawn with: paths, circles, ellipses, rectangles, lines, polylines and polygons, groups and
/// <c>use</c>; solid colours, <c>currentColor</c>, and linear and radial gradients, in either of their units and carried by
/// their own transforms; clip paths; transforms; fill and stroke opacity, fill and clip rules, joins, caps, mitre limits and
/// paint order — each written as an attribute or in a <c>style</c>, and inherited as SVG inherits it. An element's own
/// <c>opacity</c> is drawn into its fill and stroke, and a group's into everything in it. Text, masks, filters, patterns,
/// markers and nested <c>svg</c> elements are not icon drawing, and are not read.
/// </para>
/// </summary>
public static class IconBody
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string XLinkNamespace = "http://www.w3.org/1999/xlink";

    /// <summary>Elements drawn only where something names them, never where they stand — and what is not icon drawing.</summary>
    private static readonly HashSet<string> Unplaced = new(StringComparer.Ordinal)
    {
        "defs", "clipPath", "linearGradient", "radialGradient", "symbol", "mask", "pattern", "marker", "filter", "title", "desc",
        "metadata", "style", "text", "svg", "image", "foreignObject",
    };

    /// <param name="body">The icon's SVG elements, as a pack keeps them.</param>
    /// <param name="placed">What carries the body's coordinates into the icon's: a turn or a flip the pack asks for, else nothing.</param>
    /// <param name="width">How wide the icon's box is — what a length written as a percentage across it is of.</param>
    /// <param name="height">How tall it is.</param>
    public static IReadOnlyList<VectorLayer> Read(string body, VectorMatrix placed, double width, double height)
    {
        var root = XElement.Parse($"<svg xmlns=\"{SvgNamespace}\" xmlns:xlink=\"{XLinkNamespace}\">{body}</svg>");
        var reading = new Reading(root, width, height);

        reading.Children(root, Style.Initial with { Transform = placed });
        return reading.Layers;
    }

    /// <summary>What an element draws with: what it inherits from the elements round it, and what it says for itself.</summary>
    private sealed record Style
    {
        public static Style Initial { get; } = new();

        public string Fill { get; init; } = "black";
        public string Stroke { get; init; } = "none";
        public double StrokeWidth { get; init; } = 1;
        public double FillOpacity { get; init; } = 1;
        public double StrokeOpacity { get; init; } = 1;
        public bool EvenOdd { get; init; }
        public bool ClipEvenOdd { get; init; }
        public VectorJoin Join { get; init; }
        public double MiterLimit { get; init; } = 4;
        public VectorCap Cap { get; init; }
        public bool StrokeFirst { get; init; }
        public string? Color { get; init; }

        // Never inherited, but what everything inside an element is drawn through.
        public double Opacity { get; init; } = 1;
        public VectorMatrix Transform { get; init; } = VectorMatrix.Identity;
        public ImmutableList<VectorClip> Clips { get; init; } = [];
    }

    /// <summary>One icon being read: every element it names by id, and the layers drawn so far.</summary>
    private sealed class Reading
    {
        private readonly Dictionary<string, XElement> _named = new(StringComparer.Ordinal);
        private readonly HashSet<XElement> _using = [];
        private readonly double _width;
        private readonly double _height;

        public Reading(XElement root, double width, double height)
        {
            (_width, _height) = (width, height);

            foreach (var element in root.Descendants())
                if (element.Attribute("id")?.Value is { Length: > 0 } id) _named.TryAdd(id, element);
        }

        public List<VectorLayer> Layers { get; } = [];

        /// <summary>How long a length written as a percentage of the box's diagonal is — a radius, a stroke's width.</summary>
        private double Diagonal => Math.Sqrt(((_width * _width) + (_height * _height)) / 2);

        public void Children(XElement parent, Style style)
        {
            foreach (var child in parent.Elements()) Element(child, style);
        }

        private void Element(XElement element, Style inherited)
        {
            var name = element.Name.LocalName;
            var said = Said(element);
            if (Unplaced.Contains(name) || Hidden(said)) return;

            var style = Styled(element, said, inherited);
            switch (name)
            {
                case "g":
                    Children(element, style);
                    break;

                case "use":
                    if (Target(element) is { } target && _using.Add(target))
                    {
                        Element(target, style with { Transform = style.Transform * Placed(element) });
                        _using.Remove(target);
                    }
                    break;

                default:
                    if (Figures(element) is { Count: > 0 } figures) Layer(figures, style);
                    break;
            }
        }

        // ── Style ───────────────────────────────────────────────────────────────

        private Style Styled(XElement element, List<(string Property, string Value)> said, Style inherited)
        {
            var style = inherited;
            if (element.Attribute("transform")?.Value is { } transform) style = style with { Transform = style.Transform * Transformed(transform) };

            string? clip = null;
            foreach (var (property, value) in said)
            {
                if (value == "inherit") continue;

                style = property switch
                {
                    "fill" => style with { Fill = value },
                    "stroke" => style with { Stroke = value },
                    "stroke-width" when Coordinate(value, Diagonal) is { } width => style with { StrokeWidth = width },
                    "fill-opacity" when Fraction(value) is { } opacity => style with { FillOpacity = opacity },
                    "stroke-opacity" when Fraction(value) is { } opacity => style with { StrokeOpacity = opacity },
                    "opacity" when Fraction(value) is { } opacity => style with { Opacity = style.Opacity * opacity },
                    "fill-rule" => style with { EvenOdd = value == "evenodd" },
                    "clip-rule" => style with { ClipEvenOdd = value == "evenodd" },
                    "stroke-linejoin" => style with { Join = value switch { "round" => VectorJoin.Round, "bevel" => VectorJoin.Bevel, _ => VectorJoin.Miter } },
                    "stroke-miterlimit" when Number(value) is { } limit => style with { MiterLimit = limit },
                    "stroke-linecap" => style with { Cap = value switch { "round" => VectorCap.Round, "square" => VectorCap.Square, _ => VectorCap.Butt } },
                    "paint-order" => style with { StrokeFirst = StrokeBeforeFill(value) },
                    "color" => style with { Color = value },
                    _ => style,
                };

                if (property == "clip-path") clip = value;
            }

            // A clip path is in the coordinates of the element naming it, its own transform and all.
            return clip is not null && Clipped(clip, element, style) is { } clipped ? style with { Clips = style.Clips.Add(clipped) } : style;
        }

        /// <summary>What an element says of how it is drawn: its attributes, then what its <c>style</c> says over them.</summary>
        private static List<(string Property, string Value)> Said(XElement element)
        {
            var said = new List<(string Property, string Value)>();

            foreach (var attribute in element.Attributes())
                if (attribute.Name.Namespace == XNamespace.None && attribute.Name.LocalName is not ("style" or "transform"))
                    said.Add((attribute.Name.LocalName, attribute.Value.Trim()));

            if (element.Attribute("style")?.Value is { } declarations)
                foreach (var declaration in declarations.Split(';'))
                    if (declaration.IndexOf(':') is var colon and > 0)
                        said.Add((declaration[..colon].Trim(), declaration[(colon + 1)..].Trim()));

            return said;
        }

        private static bool Hidden(List<(string Property, string Value)> said) =>
            said.Any(each => (each.Property == "display" && each.Value == "none") || (each.Property == "visibility" && each.Value is "hidden" or "collapse"));

        /// <summary>Whether the stroke is drawn before the fill: the paint order written, what it leaves out following in SVG's own order.</summary>
        private static bool StrokeBeforeFill(string said)
        {
            var order = said.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            foreach (var paint in (ReadOnlySpan<string>)["fill", "stroke", "markers"])
                if (!order.Contains(paint)) order.Add(paint);

            return order.IndexOf("stroke") < order.IndexOf("fill");
        }

        // ── Layers ──────────────────────────────────────────────────────────────

        private void Layer(List<VectorFigure> local, Style style)
        {
            Box? box = null;
            Box? Bounds() => box ??= Box.Of(local);

            var fill = Paint(style.Fill, style.FillOpacity * style.Opacity, style, Bounds);
            var stroke = style.StrokeWidth > 0 ? Paint(style.Stroke, style.StrokeOpacity * style.Opacity, style, Bounds) : null;
            if (fill is null && stroke is null) return;

            Layers.Add(new VectorLayer(new VectorShape(Carried(local, style.Transform), style.EvenOdd), fill, stroke, style.StrokeWidth * style.Transform.Scale,
                                       style.Join, style.MiterLimit, style.Cap, style.StrokeFirst, style.Clips));
        }

        /// <summary>
        /// What a fill or a stroke written as <paramref name="said"/> paints with — null for none: a colour, the colour the icon is
        /// drawn in, or a gradient, by the colour after it where the gradient named is not there.
        /// </summary>
        private VectorPaint? Paint(string said, double opacity, Style style, Func<Box?> bounds)
        {
            if (opacity <= 0) return null;

            if (said.StartsWith("url(", StringComparison.Ordinal))
            {
                var close = said.IndexOf(')');
                if (close < 0) return null;

                if (_named.GetValueOrDefault(Referenced(said[..(close + 1)])) is { } named && Gradient(named, opacity, style.Transform, bounds) is { } gradient)
                    return gradient;

                var fallback = said[(close + 1)..].Trim();
                return fallback.Length > 0 ? Paint(fallback, opacity, style, bounds) : null;
            }

            if (said is "none" or "transparent") return null;
            if (said == "currentColor")
                return style.Color is { } colour && colour != "currentColor" && Colour(colour) is { } known ? Solid(known, opacity) : new VectorCurrent(opacity);

            return Colour(said) is { } solid ? Solid(solid, opacity) : null;
        }

        private static VectorPaint? Solid(VectorColor colour, double opacity) =>
            Faded(colour, opacity) is { A: > 0 } seen ? new VectorSolid(seen) : null;

        private static VectorColor Faded(VectorColor colour, double opacity) =>
            opacity >= 1 ? colour : colour with { A = (byte)Math.Round(colour.A * Math.Clamp(opacity, 0, 1)) };

        /// <summary>
        /// The gradient <paramref name="named"/> is, with what it takes from the gradients it names — or a colour where it has a single
        /// stop, and null where it has none or is measured across an outline with no width or no height, which SVG paints nothing with.
        /// </summary>
        private VectorPaint? Gradient(XElement named, double opacity, VectorMatrix transform, Func<Box?> bounds)
        {
            var linear = named.Name.LocalName == "linearGradient";
            if (!linear && named.Name.LocalName != "radialGradient") return null;

            var chain = Chain(named);
            string? Said(string attribute) => chain.Select(each => each.Attribute(attribute)?.Value).FirstOrDefault(value => value is not null);

            var stops = Stops(chain.FirstOrDefault(each => each.Elements().Any(child => child.Name.LocalName == "stop")), opacity);
            if (stops.Count == 0) return null;
            if (stops.Count == 1) return new VectorSolid(stops[0].Color);

            // Measured across the outline's bounds, nought to one, unless it says it is in the outline's own coordinates.
            var boxed = Said("gradientUnits") != "userSpaceOnUse";
            var space = transform;
            if (boxed)
            {
                if (bounds() is not { Width: > 0, Height: > 0 } box) return null;
                space *= new VectorMatrix(box.Width, 0, 0, box.Height, box.Left, box.Top);
            }

            if (Said("gradientTransform") is { } own) space *= Transformed(own);

            var (across, down, round) = boxed ? (1.0, 1.0, 1.0) : (_width, _height, Diagonal);
            double Length(string attribute, string otherwise, double of) => Coordinate(Said(attribute) ?? otherwise, of) ?? 0;

            if (linear)
                return new VectorLinear(stops, new VectorPoint(Length("x1", "0%", across), Length("y1", "0%", down)),
                                        new VectorPoint(Length("x2", "100%", across), Length("y2", "0%", down)), space);

            var centre = new VectorPoint(Length("cx", "50%", across), Length("cy", "50%", down));
            var focus = new VectorPoint(Coordinate(Said("fx") ?? string.Empty, across) ?? centre.X, Coordinate(Said("fy") ?? string.Empty, down) ?? centre.Y);
            return new VectorRadial(stops, centre, Length("r", "50%", round), focus, space);
        }

        /// <summary>A gradient, then each gradient it takes what it does not say from, nearest first.</summary>
        private List<XElement> Chain(XElement gradient)
        {
            var chain = new List<XElement> { gradient };
            while (Target(chain[^1]) is { } next && !chain.Contains(next)) chain.Add(next);
            return chain;
        }

        /// <summary>A gradient's stops, each at least as far along as the one before it, as SVG takes them.</summary>
        private static List<VectorStop> Stops(XElement? gradient, double opacity)
        {
            var stops = new List<VectorStop>();
            if (gradient is null) return stops;

            var last = 0.0;
            foreach (var stop in gradient.Elements().Where(child => child.Name.LocalName == "stop"))
            {
                var said = Said(stop);
                string? Value(string property) => said.LastOrDefault(each => each.Property == property).Value;

                last = Math.Max(last, Math.Clamp(Fraction(Value("offset") ?? "0") ?? 0, 0, 1));
                var colour = Colour(Value("stop-color") ?? "black") ?? new VectorColor(255, 0, 0, 0);
                stops.Add(new VectorStop(Faded(colour, (Fraction(Value("stop-opacity") ?? "1") ?? 1) * opacity), last));
            }

            return stops;
        }

        // ── Clips ───────────────────────────────────────────────────────────────

        /// <summary>The clip path <paramref name="said"/> names, in the coordinates of <paramref name="element"/>, which names it.</summary>
        private VectorClip? Clipped(string said, XElement element, Style style)
        {
            if (!said.StartsWith("url(", StringComparison.Ordinal) || _named.GetValueOrDefault(Referenced(said)) is not { Name.LocalName: "clipPath" } clip)
                return null;

            var space = style.Transform;
            if (clip.Attribute("transform")?.Value is { } transform) space *= Transformed(transform);

            if (clip.Attribute("clipPathUnits")?.Value == "objectBoundingBox")
            {
                if (Box.Of(Content(element).SelectMany(shape => shape.Figures)) is not { Width: > 0, Height: > 0 } box) return null;
                space *= new VectorMatrix(box.Width, 0, 0, box.Height, box.Left, box.Top);
            }

            var rule = Said(clip).LastOrDefault(each => each.Property == "clip-rule").Value == "evenodd";
            return new VectorClip([.. clip.Elements().SelectMany(child => Outlines(child, space, rule))]);
        }

        /// <summary>The outlines an element draws, in its own coordinates — what a box measured across it is the bounds of.</summary>
        private IEnumerable<VectorShape> Content(XElement element) => element.Name.LocalName switch
        {
            "g" => element.Elements().SelectMany(child => Outlines(child, VectorMatrix.Identity, false)),
            "use" when Target(element) is { } target => Outlines(target, Placed(element), false),
            _ => [new VectorShape(Figures(element), false)],
        };

        /// <summary>The outlines an element and everything in it draw, carried by <paramref name="space"/> — what a clip path is made of.</summary>
        private IEnumerable<VectorShape> Outlines(XElement element, VectorMatrix space, bool evenOdd)
        {
            var name = element.Name.LocalName;
            var said = Said(element);
            if (Unplaced.Contains(name) || Hidden(said) || !_using.Add(element)) yield break;

            try
            {
                if (element.Attribute("transform")?.Value is { } transform) space *= Transformed(transform);
                if (said.LastOrDefault(each => each.Property == "clip-rule").Value is { } rule) evenOdd = rule == "evenodd";

                var inside = name switch
                {
                    "g" => element.Elements().SelectMany(child => Outlines(child, space, evenOdd)),
                    "use" when Target(element) is { } target => Outlines(target, space * Placed(element), evenOdd),
                    _ => Figures(element) is { Count: > 0 } figures ? [new VectorShape(Carried(figures, space), evenOdd)] : [],
                };

                foreach (var shape in inside.ToList()) yield return shape;
            }
            finally { _using.Remove(element); }
        }

        // ── Shapes ──────────────────────────────────────────────────────────────

        /// <summary>The figures a shape element draws, in its own coordinates.</summary>
        private List<VectorFigure> Figures(XElement element)
        {
            double Across(string attribute) => Coordinate(element.Attribute(attribute)?.Value ?? "0", _width) ?? 0;
            double Down(string attribute) => Coordinate(element.Attribute(attribute)?.Value ?? "0", _height) ?? 0;
            double Round(string attribute) => Coordinate(element.Attribute(attribute)?.Value ?? "0", Diagonal) ?? 0;

            switch (element.Name.LocalName)
            {
                case "path":
                    return Path(element.Attribute("d")?.Value);

                case "circle":
                    return Ellipse(Across("cx"), Down("cy"), Round("r"), Round("r"));

                case "ellipse":
                {
                    var (rx, ry) = Radii(element, Across, Down);
                    return Ellipse(Across("cx"), Down("cy"), rx, ry);
                }

                case "rect":
                {
                    var (rx, ry) = Radii(element, Across, Down);
                    return Rectangle(Across("x"), Down("y"), Across("width"), Down("height"), rx, ry);
                }

                case "line":
                {
                    var outline = new Outline();
                    outline.Move(new VectorPoint(Across("x1"), Down("y1")));
                    outline.Line(new VectorPoint(Across("x2"), Down("y2")));
                    return outline.Done();
                }

                case "polyline" or "polygon":
                {
                    var outline = new Outline();
                    var numbers = Numbers(element.Attribute("points")?.Value ?? string.Empty);
                    for (var at = 0; at + 1 < numbers.Count; at += 2)
                    {
                        var point = new VectorPoint(numbers[at], numbers[at + 1]);
                        if (at == 0) outline.Move(point);
                        else outline.Line(point);
                    }

                    if (element.Name.LocalName == "polygon") outline.Close();
                    return outline.Done();
                }

                default:
                    return [];
            }
        }

        /// <summary>An ellipse's or a rounded rectangle's radii: each as written, or the other where only one is — SVG's <c>auto</c>.</summary>
        private static (double X, double Y) Radii(XElement element, Func<string, double> across, Func<string, double> down)
        {
            var x = element.Attribute("rx")?.Value is { } rx && rx != "auto" ? across("rx") : (double?)null;
            var y = element.Attribute("ry")?.Value is { } ry && ry != "auto" ? down("ry") : (double?)null;
            return (x ?? y ?? 0, y ?? x ?? 0);
        }

        /// <summary>The element a <c>use</c> or a gradient names by its <c>href</c>.</summary>
        private XElement? Target(XElement element)
        {
            var href = element.Attribute("href")?.Value ?? element.Attribute(XName.Get("href", XLinkNamespace))?.Value;
            return href is ['#', .. var id] ? _named.GetValueOrDefault(id) : null;
        }

        /// <summary>Where a <c>use</c> sets what it names: its <c>x</c> and <c>y</c>, past its own transform.</summary>
        private VectorMatrix Placed(XElement use) =>
            VectorMatrix.Translation(Coordinate(use.Attribute("x")?.Value ?? "0", _width) ?? 0, Coordinate(use.Attribute("y")?.Value ?? "0", _height) ?? 0);

        /// <summary>The id a <c>url(#id)</c> names.</summary>
        private static string Referenced(string url)
        {
            var open = url.IndexOf('(');
            var close = url.LastIndexOf(')');
            return open < 0 || close <= open ? string.Empty : url[(open + 1)..close].Trim().Trim('"', '\'').TrimStart('#');
        }
    }

    // ── Outlines ────────────────────────────────────────────────────────────────

    private static List<VectorFigure> Carried(List<VectorFigure> figures, VectorMatrix transform)
    {
        if (transform == VectorMatrix.Identity) return figures;

        return [.. figures.Select(figure => new VectorFigure(transform.Transform(figure.Start), [.. figure.Segments.Select(Segment)], figure.Closed))];

        VectorSegment Segment(VectorSegment segment) => segment switch
        {
            VectorCubic cubic => new VectorCubic(transform.Transform(cubic.First), transform.Transform(cubic.Second), transform.Transform(cubic.To)),
            VectorQuadratic quadratic => new VectorQuadratic(transform.Transform(quadratic.Control), transform.Transform(quadratic.To)),
            _ => new VectorLine(transform.Transform(segment.To)),
        };
    }

    private static List<VectorFigure> Ellipse(double cx, double cy, double rx, double ry)
    {
        var outline = new Outline();
        if (rx <= 0 || ry <= 0) return outline.Done();

        outline.Move(new VectorPoint(cx + rx, cy));
        outline.Arc(rx, ry, 0, large: false, sweep: true, new VectorPoint(cx - rx, cy));
        outline.Arc(rx, ry, 0, large: false, sweep: true, new VectorPoint(cx + rx, cy));
        outline.Close();
        return outline.Done();
    }

    private static List<VectorFigure> Rectangle(double x, double y, double width, double height, double rx, double ry)
    {
        var outline = new Outline();
        if (width <= 0 || height <= 0) return outline.Done();

        rx = Math.Clamp(rx, 0, width / 2);
        ry = Math.Clamp(ry, 0, height / 2);
        var (right, bottom) = (x + width, y + height);

        if (rx == 0 || ry == 0)
        {
            outline.Move(new VectorPoint(x, y));
            outline.Line(new VectorPoint(right, y));
            outline.Line(new VectorPoint(right, bottom));
            outline.Line(new VectorPoint(x, bottom));
        }
        else
        {
            outline.Move(new VectorPoint(x + rx, y));
            outline.Line(new VectorPoint(right - rx, y));
            outline.Arc(rx, ry, 0, false, true, new VectorPoint(right, y + ry));
            outline.Line(new VectorPoint(right, bottom - ry));
            outline.Arc(rx, ry, 0, false, true, new VectorPoint(right - rx, bottom));
            outline.Line(new VectorPoint(x + rx, bottom));
            outline.Arc(rx, ry, 0, false, true, new VectorPoint(x, bottom - ry));
            outline.Line(new VectorPoint(x, y + ry));
            outline.Arc(rx, ry, 0, false, true, new VectorPoint(x + rx, y));
        }

        outline.Close();
        return outline.Done();
    }

    /// <summary>
    /// The figures SVG path data draws, every command made absolute and every arc the curves that make it. Path data that goes
    /// wrong part-way draws what came before, as SVG draws it.
    /// </summary>
    private static List<VectorFigure> Path(string? data)
    {
        var outline = new Outline();
        if (string.IsNullOrWhiteSpace(data)) return outline.Done();

        var scan = new Scanner(data);
        var command = '\0';
        var previous = '\0';
        var control = default(VectorPoint);

        while (scan.More())
        {
            if (scan.Letter() is { } letter) command = letter;
            else if (command is '\0' or 'Z' or 'z') break;

            var relative = char.IsLower(command);
            var from = outline.Current;
            VectorPoint Point(double x, double y) => relative ? new VectorPoint(from.X + x, from.Y + y) : new VectorPoint(x, y);

            var upper = char.ToUpperInvariant(command);
            switch (upper)
            {
                case 'M':
                    if (!scan.Pair(out var mx, out var my)) return outline.Done();
                    outline.Move(Point(mx, my));
                    command = relative ? 'l' : 'L';
                    break;

                case 'L':
                    if (!scan.Pair(out var lx, out var ly)) return outline.Done();
                    outline.Line(Point(lx, ly));
                    break;

                case 'H':
                    if (!scan.Number(out var hx)) return outline.Done();
                    outline.Line(new VectorPoint(relative ? from.X + hx : hx, from.Y));
                    break;

                case 'V':
                    if (!scan.Number(out var vy)) return outline.Done();
                    outline.Line(new VectorPoint(from.X, relative ? from.Y + vy : vy));
                    break;

                case 'C':
                {
                    if (!scan.Pair(out var x1, out var y1) || !scan.Pair(out var x2, out var y2) || !scan.Pair(out var x, out var y)) return outline.Done();
                    control = Point(x2, y2);
                    outline.Cubic(Point(x1, y1), control, Point(x, y));
                    break;
                }

                case 'S':
                {
                    if (!scan.Pair(out var x2, out var y2) || !scan.Pair(out var x, out var y)) return outline.Done();
                    var first = previous is 'C' or 'S' ? Reflected(control, from) : from;
                    control = Point(x2, y2);
                    outline.Cubic(first, control, Point(x, y));
                    break;
                }

                case 'Q':
                {
                    if (!scan.Pair(out var x1, out var y1) || !scan.Pair(out var x, out var y)) return outline.Done();
                    control = Point(x1, y1);
                    outline.Quadratic(control, Point(x, y));
                    break;
                }

                case 'T':
                {
                    if (!scan.Pair(out var x, out var y)) return outline.Done();
                    control = previous is 'Q' or 'T' ? Reflected(control, from) : from;
                    outline.Quadratic(control, Point(x, y));
                    break;
                }

                case 'A':
                {
                    if (!scan.Number(out var rx) || !scan.Number(out var ry) || !scan.Number(out var turn)
                        || !scan.Flag(out var large) || !scan.Flag(out var sweep) || !scan.Pair(out var x, out var y))
                        return outline.Done();

                    outline.Arc(rx, ry, turn, large, sweep, Point(x, y));
                    break;
                }

                case 'Z':
                    outline.Close();
                    break;

                default:
                    return outline.Done();
            }

            previous = upper;
        }

        return outline.Done();

        static VectorPoint Reflected(VectorPoint control, VectorPoint about) => new((2 * about.X) - control.X, (2 * about.Y) - control.Y);
    }

    /// <summary>Figures drawn a step at a time, as path data draws them.</summary>
    private sealed class Outline
    {
        private readonly List<VectorFigure> _figures = [];
        private List<VectorSegment> _segments = [];
        private VectorPoint _start;

        /// <summary>Where the last step ended.</summary>
        public VectorPoint Current { get; private set; }

        public void Move(VectorPoint to)
        {
            Finish(closed: false);
            _start = Current = to;
        }

        public void Line(VectorPoint to) => Add(new VectorLine(to));

        public void Cubic(VectorPoint first, VectorPoint second, VectorPoint to) => Add(new VectorCubic(first, second, to));

        public void Quadratic(VectorPoint control, VectorPoint to) => Add(new VectorQuadratic(control, to));

        /// <summary>
        /// SVG's arc to <paramref name="to"/>, drawn as the cubic curves making it — one for each quarter turn or less of it — by the
        /// centre SVG's implementation notes work out from its ends; radii too small to reach are grown until they do.
        /// </summary>
        public void Arc(double rx, double ry, double degrees, bool large, bool sweep, VectorPoint to)
        {
            var from = Current;
            if (from == to) return;

            (rx, ry) = (Math.Abs(rx), Math.Abs(ry));
            if (rx == 0 || ry == 0)
            {
                Line(to);
                return;
            }

            var (sin, cos) = Math.SinCos(degrees * (Math.PI / 180));
            var (dx, dy) = ((from.X - to.X) / 2, (from.Y - to.Y) / 2);
            var (x1, y1) = ((cos * dx) + (sin * dy), (-sin * dx) + (cos * dy));

            var reach = ((x1 * x1) / (rx * rx)) + ((y1 * y1) / (ry * ry));
            if (reach > 1) (rx, ry) = (rx * Math.Sqrt(reach), ry * Math.Sqrt(reach));

            var below = (rx * rx * y1 * y1) + (ry * ry * x1 * x1);
            var root = below == 0 ? 0 : Math.Sqrt(Math.Max(0, ((rx * rx * ry * ry) - below) / below));
            if (large == sweep) root = -root;

            var (cx1, cy1) = (root * rx * y1 / ry, -root * ry * x1 / rx);
            var (cx, cy) = ((cos * cx1) - (sin * cy1) + ((from.X + to.X) / 2), (sin * cx1) + (cos * cy1) + ((from.Y + to.Y) / 2));

            var start = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
            var turned = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
            if (!sweep && turned > 0) turned -= 2 * Math.PI;
            else if (sweep && turned < 0) turned += 2 * Math.PI;

            var pieces = Math.Max(1, (int)Math.Ceiling((Math.Abs(turned) / (Math.PI / 2)) - 1e-9));
            var step = turned / pieces;
            var lean = 4.0 / 3 * Math.Tan(step / 4);

            for (var piece = 0; piece < pieces; piece++)
            {
                var (s1, c1) = Math.SinCos(start + (piece * step));
                var (s2, c2) = Math.SinCos(start + ((piece + 1) * step));

                Cubic(On(c1 - (lean * s1), s1 + (lean * c1)), On(c2 + (lean * s2), s2 - (lean * c2)), piece == pieces - 1 ? to : On(c2, s2));
            }

            // A point on the unit circle, carried onto the ellipse.
            VectorPoint On(double u, double v) => new(cx + (cos * rx * u) - (sin * ry * v), cy + (sin * rx * u) + (cos * ry * v));

            static double Angle(double ux, double uy, double vx, double vy) => Math.Atan2((ux * vy) - (uy * vx), (ux * vx) + (uy * vy));
        }

        public void Close()
        {
            Finish(closed: true);
            Current = _start;
        }

        public List<VectorFigure> Done()
        {
            Finish(closed: false);
            return _figures;
        }

        private void Add(VectorSegment segment)
        {
            _segments.Add(segment);
            Current = segment.To;
        }

        private void Finish(bool closed)
        {
            if (_segments.Count == 0) return;

            _figures.Add(new VectorFigure(_start, _segments, closed));
            _segments = [];
        }
    }

    /// <summary>The bounds of figures, curves and all — what a gradient or a clip measured across an outline is measured across.</summary>
    private readonly record struct Box(double Left, double Top, double Width, double Height)
    {
        public static Box? Of(IEnumerable<VectorFigure> figures)
        {
            var (left, top, right, bottom) = (double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
            void Take(VectorPoint point) => (left, top, right, bottom) = (Math.Min(left, point.X), Math.Min(top, point.Y), Math.Max(right, point.X), Math.Max(bottom, point.Y));

            foreach (var figure in figures)
            {
                var from = figure.Start;
                Take(from);

                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case VectorCubic cubic:
                            foreach (var t in Turns(from.X, cubic.First.X, cubic.Second.X, cubic.To.X).Concat(Turns(from.Y, cubic.First.Y, cubic.Second.Y, cubic.To.Y)))
                                Take(OnCubic(from, cubic, t));
                            break;

                        case VectorQuadratic quadratic:
                            foreach (var t in Turn(from.X, quadratic.Control.X, quadratic.To.X).Concat(Turn(from.Y, quadratic.Control.Y, quadratic.To.Y)))
                                Take(OnQuadratic(from, quadratic, t));
                            break;
                    }

                    Take(segment.To);
                    from = segment.To;
                }
            }

            return left > right ? null : new Box(left, top, right - left, bottom - top);
        }

        /// <summary>Where along a cubic curve, strictly between its ends, one coordinate of it turns back.</summary>
        private static IEnumerable<double> Turns(double p0, double p1, double p2, double p3)
        {
            var (a, b, c) = (p3 - (3 * p2) + (3 * p1) - p0, 2 * (p2 - (2 * p1) + p0), p1 - p0);

            if (Math.Abs(a) < 1e-12)
            {
                if (Math.Abs(b) > 1e-12 && -c / b is var t and > 0 and < 1) yield return t;
                yield break;
            }

            var square = (b * b) - (4 * a * c);
            if (square < 0) yield break;

            foreach (var t in new[] { (-b + Math.Sqrt(square)) / (2 * a), (-b - Math.Sqrt(square)) / (2 * a) })
                if (t is > 0 and < 1) yield return t;
        }

        /// <summary>Where along a quadratic curve one coordinate of it turns back.</summary>
        private static IEnumerable<double> Turn(double p0, double p1, double p2)
        {
            var below = p0 - (2 * p1) + p2;
            if (Math.Abs(below) > 1e-12 && (p0 - p1) / below is var t and > 0 and < 1) yield return t;
        }

        private static VectorPoint OnCubic(VectorPoint from, VectorCubic cubic, double t)
        {
            var u = 1 - t;
            var (a, b, c, d) = (u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t);
            return new((a * from.X) + (b * cubic.First.X) + (c * cubic.Second.X) + (d * cubic.To.X),
                       (a * from.Y) + (b * cubic.First.Y) + (c * cubic.Second.Y) + (d * cubic.To.Y));
        }

        private static VectorPoint OnQuadratic(VectorPoint from, VectorQuadratic quadratic, double t)
        {
            var u = 1 - t;
            var (a, b, c) = (u * u, 2 * u * t, t * t);
            return new((a * from.X) + (b * quadratic.Control.X) + (c * quadratic.To.X), (a * from.Y) + (b * quadratic.Control.Y) + (c * quadratic.To.Y));
        }
    }

    // ── Values ──────────────────────────────────────────────────────────────────

    /// <summary>A transform list as SVG writes one — <c>translate(1 2)scale(.5)</c> — the first written the outermost.</summary>
    private static VectorMatrix Transformed(string said)
    {
        var total = VectorMatrix.Identity;
        var at = 0;

        while (at < said.Length)
        {
            var open = said.IndexOf('(', at);
            var close = open < 0 ? -1 : said.IndexOf(')', open);
            if (close < 0) break;

            var name = said[at..open].Trim().TrimStart(',').Trim();
            var n = Numbers(said[(open + 1)..close]);

            total *= (name, n.Count) switch
            {
                ("matrix", 6) => new VectorMatrix(n[0], n[1], n[2], n[3], n[4], n[5]),
                ("translate", 1) => VectorMatrix.Translation(n[0], 0),
                ("translate", 2) => VectorMatrix.Translation(n[0], n[1]),
                ("scale", 1) => VectorMatrix.Scaling(n[0], n[0]),
                ("scale", 2) => VectorMatrix.Scaling(n[0], n[1]),
                ("rotate", 1) => VectorMatrix.Rotation(n[0]),
                ("rotate", 3) => VectorMatrix.Rotation(n[0], n[1], n[2]),
                ("skewX", 1) => new VectorMatrix(1, 0, Math.Tan(n[0] * (Math.PI / 180)), 1, 0, 0),
                ("skewY", 1) => new VectorMatrix(1, Math.Tan(n[0] * (Math.PI / 180)), 0, 1, 0, 0),
                _ => VectorMatrix.Identity,
            };

            at = close + 1;
        }

        return total;
    }

    private static List<double> Numbers(string said)
    {
        var numbers = new List<double>();
        var scan = new Scanner(said);
        while (scan.Number(out var number)) numbers.Add(number);
        return numbers;
    }

    private static double? Number(string said) =>
        double.TryParse(said.EndsWith("px", StringComparison.Ordinal) ? said[..^2] : said, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    /// <summary>A length written as a number, or as a percentage of <paramref name="of"/>.</summary>
    private static double? Coordinate(string said, double of) =>
        said.EndsWith('%') ? Number(said[..^1]) * of / 100 : Number(said);

    /// <summary>An opacity or an offset: a number, or a percentage of one, held between nought and one.</summary>
    private static double? Fraction(string said) => Coordinate(said, 1) is { } fraction ? Math.Clamp(fraction, 0, 1) : null;

    /// <summary>A colour as CSS writes one: <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, <c>rgb()</c>, <c>rgba()</c>, or its name.</summary>
    private static VectorColor? Colour(string said)
    {
        var text = said.Trim();

        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return null;

            return hex.Length switch
            {
                3 => new VectorColor(255, Doubled(value >> 8), Doubled(value >> 4), Doubled(value)),
                4 => new VectorColor(Doubled(value), Doubled(value >> 12), Doubled(value >> 8), Doubled(value >> 4)),
                6 => new VectorColor(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value),
                8 => new VectorColor((byte)value, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8)),
                _ => null,
            };
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) && text.IndexOf('(') is var open and > 0 && text.EndsWith(')'))
        {
            var parts = text[(open + 1)..^1].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is not (3 or 4)) return null;

            byte Channel(string part) => (byte)Math.Round(Math.Clamp(Coordinate(part, 255) ?? 0, 0, 255));
            var alpha = parts.Length == 4 ? (byte)Math.Round((Fraction(parts[3]) ?? 1) * 255) : (byte)255;
            return new VectorColor(alpha, Channel(parts[0]), Channel(parts[1]), Channel(parts[2]));
        }

        // CSS spells grey both ways; .NET's names only one.
        var named = System.Drawing.Color.FromName(text.Replace("grey", "gray", StringComparison.OrdinalIgnoreCase));
        return named.IsKnownColor && !named.IsSystemColor ? new VectorColor(named.A, named.R, named.G, named.B) : null;

        static byte Doubled(uint nibble) => (byte)((nibble & 0xF) * 17);
    }

    /// <summary>Path data, a transform's or a list's numbers, read a number, a flag or a command at a time.</summary>
    private sealed class Scanner(string text)
    {
        private int _at;

        public bool More()
        {
            Skip();
            return _at < text.Length;
        }

        public char? Letter()
        {
            Skip();
            return _at < text.Length && char.IsAsciiLetter(text[_at]) ? text[_at++] : null;
        }

        public bool Pair(out double x, out double y)
        {
            y = 0;
            return Number(out x) && Number(out y);
        }

        public bool Number(out double value)
        {
            Skip();
            value = 0;
            var start = _at;

            if (_at < text.Length && text[_at] is '+' or '-') _at++;

            var digits = false;
            while (_at < text.Length && char.IsAsciiDigit(text[_at])) (_at, digits) = (_at + 1, true);
            if (_at < text.Length && text[_at] == '.')
                for (_at++; _at < text.Length && char.IsAsciiDigit(text[_at]); _at++) digits = true;

            if (!digits)
            {
                _at = start;
                return false;
            }

            if (_at < text.Length && text[_at] is 'e' or 'E')
            {
                var mark = _at++;
                if (_at < text.Length && text[_at] is '+' or '-') _at++;

                if (_at < text.Length && char.IsAsciiDigit(text[_at]))
                    while (_at < text.Length && char.IsAsciiDigit(text[_at])) _at++;
                else
                    _at = mark;
            }

            return double.TryParse(text.AsSpan(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>An arc's flag: a single <c>0</c> or <c>1</c>, which path data may write with no space before what follows.</summary>
        public bool Flag(out bool value)
        {
            Skip();
            value = false;
            if (_at >= text.Length || text[_at] is not ('0' or '1')) return false;

            value = text[_at++] == '1';
            return true;
        }

        private void Skip()
        {
            while (_at < text.Length && (char.IsWhiteSpace(text[_at]) || text[_at] == ',')) _at++;
        }
    }
}
