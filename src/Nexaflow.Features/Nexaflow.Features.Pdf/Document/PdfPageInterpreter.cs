using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Pdf.Reading;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Colors.Icc;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.PdfFonts;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.XObjects;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// Reads one content stream — a page's, a pattern cell's, a soft mask's, an annotation's appearance — into the
/// <see cref="PdfGroupNode"/> tree it draws.
/// <para>
/// PdfPig runs the operators and keeps the graphics state; this turns what they paint into nodes. A clip opens a
/// group that lasts as long as the graphics state that set it, so content drawn through one clip shares one group.
/// Glyphs set by one text-showing operator become one <see cref="PdfTextNode"/>, and the page's text is gathered
/// from those runs as they are made.
/// </para>
/// </summary>
internal sealed class PdfPageInterpreter : BaseStreamProcessor<PdfGroupNode>
{
    /// <summary>How deep Type 3 glyphs may draw Type 3 glyphs before a document is taken to be looping.</summary>
    private const int GlyphProcedureDepth = 4;

    private static readonly NameToken OptionalContentKey = NameToken.Create("OC");
    private static readonly NameToken ActualTextKey      = NameToken.Create("ActualText");
    private static readonly NameToken GroupKey           = NameToken.Create("Group");
    private static readonly NameToken TransparencyName   = NameToken.Create("Transparency");
    private static readonly NameToken PostScriptName     = NameToken.Create("PS");
    private static readonly NameToken SubtypeKey         = NameToken.Create("Subtype");
    private static readonly NameToken MatrixKey          = NameToken.Create("Matrix");

    private readonly PdfReadingCaches _caches;
    private readonly Matrix _output;
    private readonly List<PdfUnsupported> _unsupported;
    private readonly StringBuilder? _text;
    private readonly bool _onPage;
    private readonly CropBox _cropBox;
    private readonly HashSet<string> _reported = [];

    private readonly GroupBuilder _root = new(PdfGroupRole.Root);
    private readonly Stack<GroupBuilder> _frames = new();
    private GroupBuilder? _nextFrame;

    private readonly PdfShape.Writer _path = new();
    private FillingRule? _pendingClip;

    private RunBuilder? _run;
    private Rect? _lastBox;
    private PdfShape.Writer? _textClip;
    private int _glyphProcedures;

    private readonly Stack<MarkedFrame> _marked = new();
    private int _hidden;
    private string? _actualText;
    private bool _actualTextPending;

    private readonly Dictionary<SoftMask, TransformationMatrix> _maskMatrices = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<TransformationMatrix> _streamBases = new();
    private readonly Dictionary<SoftMask, PdfSoftMask?> _masks = new(ReferenceEqualityComparer.Instance);

    /// <param name="output">From the space PdfPig's current transformation matrix maps into, to the space the tree is
    /// placed in: page space for a page, a mask or an appearance; pattern space for a pattern cell.</param>
    /// <param name="unsupported">Where what cannot be carried is recorded.</param>
    /// <param name="text">Where the stream's text is gathered; null for a stream whose text is no page's text.</param>
    /// <param name="onPage">
    /// Whether what the stream draws lands on a page — a page's content, an annotation's appearance — where a blend mode
    /// has a backdrop to blend with and the layout composites it. A pattern cell or a soft mask's group has none, so a
    /// blend mode there is reported as not carried.
    /// </param>
    public PdfPageInterpreter(
        int pageNumber, IResourceStore resourceStore, IPdfTokenScanner pdfScanner, IPageContentParser pageContentParser,
        ILookupFilterProvider filterProvider, CropBox cropBox, UserSpaceUnit userSpaceUnit, PageRotationDegrees rotation,
        TransformationMatrix initialMatrix, IIccProfile? outputIntent, ParsingOptions parsingOptions,
        PdfReadingCaches caches, Matrix output, List<PdfUnsupported> unsupported, StringBuilder? text, bool onPage)
        : base(pageNumber, resourceStore, pdfScanner, pageContentParser, filterProvider, cropBox, userSpaceUnit,
            rotation, initialMatrix, outputIntent, parsingOptions)
    {
        _caches      = caches;
        _output      = output;
        _unsupported = unsupported;
        _text        = text;
        _onPage      = onPage;
        _cropBox     = cropBox;
        _streamBases.Push(initialMatrix);
        _frames.Push(_root);
    }

    /// <summary>Every text run the stream set, in the order it set them.</summary>
    public List<PdfTextNode> Runs { get; } = [];

    public override PdfGroupNode Process(int pageNumberCurrent, IReadOnlyList<IGraphicsStateOperation> operations)
    {
        CloneAllStates();
        Guarded(() =>
        {
            PushState();
            ProcessOperations(operations);
            PopState();
        });
        return Finish();
    }

    /// <summary>
    /// Reads the form XObject <paramref name="form"/> on its own — a soft mask's group, an annotation's appearance —
    /// with this interpreter's starting matrix as the form's surroundings.
    /// </summary>
    public PdfGroupNode ProcessForm(StreamToken form, NameToken name)
    {
        CloneAllStates();
        Guarded(() => ProcessFormXObject(form, name));
        return Finish();
    }

    private PdfGroupNode Finish()
    {
        FlushRun();
        while (_frames.Count > 1) _frames.Pop();
        return _root.Build();
    }

    /// <summary>
    /// Runs <paramref name="read"/>, keeping what it drew when the content turns out to be damaged part-way through.
    /// Cancellation — the document's stream closed under it — is not damage, and goes on up.
    /// </summary>
    private void Guarded(Action read)
    {
        try
        {
            read();
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ObjectDisposedException or OutOfMemoryException))
        {
            Unsupported(PdfUnsupportedKind.DamagedContent, ex.Message);
        }
    }

    /// <summary>
    /// Runs the operators one at a time, so one that fails on damaged content — a missing resource, a malformed
    /// operand — costs what it would have drawn and nothing after it.
    /// </summary>
    protected override void ProcessOperations(IReadOnlyList<IGraphicsStateOperation> operations)
    {
        var cancellation = _caches.Cancellation;
        for (var i = 0; i < operations.Count; i++)
        {
            if ((i & 63) == 0) cancellation.ThrowIfCancellationRequested();

            var operation = operations[i];
            try
            {
                operation.Run(this);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or ObjectDisposedException or OutOfMemoryException or InsufficientExecutionStackException))
            {
                Unsupported(PdfUnsupportedKind.DamagedContent, $"{operation.Operator}: {ex.Message}");
            }
        }
    }

    // ── Graphics state ─────────────────────────────────────────────────────────────────────────────────────────────

    public override void PushState()
    {
        if (_glyphProcedures == 0) FlushRun();
        base.PushState();
        _frames.Push(_nextFrame ?? Open);
        _nextFrame = null;
    }

    public override void PopState()
    {
        if (!CanPopState || _frames.Count <= 1) return;
        if (_glyphProcedures == 0) FlushRun();
        base.PopState();
        _frames.Pop();
        _path.Clear();
        _pendingClip = null;
    }

    public override void SetNamedGraphicsState(NameToken stateName)
    {
        base.SetNamedGraphicsState(stateName);

        // A soft mask's group is drawn with the transformation in force when the mask was set, not when it is used.
        var state = GetCurrentState();
        if (state.SoftMask is { } mask) _maskMatrices.TryAdd(mask, state.CurrentTransformationMatrix);
    }

    private GroupBuilder Open => _frames.Peek();

    // ── Paths ──────────────────────────────────────────────────────────────────────────────────────────────────────

    public override void BeginSubpath()
    {
    }

    public override PdfPoint? CloseSubpath()
    {
        if (_path.IsEmpty) return null;
        _path.Close();
        return CurrentTransformationMatrix.Transform(new PdfPoint(_path.Current.X, _path.Current.Y));
    }

    public override void MoveTo(double x, double y)
    {
        _path.MoveTo(x, y);
        CurrentPosition = CurrentTransformationMatrix.Transform(new PdfPoint(x, y));
    }

    public override void LineTo(double x, double y)
    {
        if (_path.IsEmpty) return;
        _path.LineTo(x, y);
        CurrentPosition = CurrentTransformationMatrix.Transform(new PdfPoint(x, y));
    }

    public override void BezierCurveTo(double x2, double y2, double x3, double y3)
    {
        if (_path.IsEmpty) return;
        var from = _path.Current;
        _path.CubicTo(from.X, from.Y, x2, y2, x3, y3);
        CurrentPosition = CurrentTransformationMatrix.Transform(new PdfPoint(x3, y3));
    }

    public override void BezierCurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (_path.IsEmpty) return;
        _path.CubicTo(x1, y1, x2, y2, x3, y3);
        CurrentPosition = CurrentTransformationMatrix.Transform(new PdfPoint(x3, y3));
    }

    public override void Rectangle(double x, double y, double width, double height)
    {
        _path.Rectangle(x, y, width, height);
        CurrentPosition = CurrentTransformationMatrix.Transform(new PdfPoint(x, y));
    }

    public override void ClosePath() => _path.Close();

    public override void StrokePath(bool close)
    {
        if (close) _path.Close();
        PaintPath(fill: null, stroke: true);
    }

    public override void FillPath(FillingRule fillingRule, bool close)
    {
        if (close) _path.Close();
        PaintPath(fill: fillingRule, stroke: false);
    }

    public override void FillStrokePath(FillingRule fillingRule, bool close)
    {
        if (close) _path.Close();
        PaintPath(fill: fillingRule, stroke: true);
    }

    public override void EndPath() => FinishPath(_path.Take());

    /// <summary><c>W</c> / <c>W*</c>: the path being built becomes a clip once it is painted or ended.</summary>
    public override void ModifyClippingIntersect(FillingRule clippingRule) => _pendingClip = clippingRule;

    protected override void ClipToRectangle(PdfRectangle rectangle, FillingRule clippingRule)
    {
        var writer = new PdfShape.Writer();
        writer.Rectangle(rectangle.Left, rectangle.Bottom, rectangle.Width, rectangle.Height);
        Clip(writer.Take(), clippingRule, UserToOutput());
    }

    private void PaintPath(FillingRule? fill, bool stroke)
    {
        var shape = _path.Take();
        if (!shape.IsEmpty && _hidden == 0)
        {
            var state = GetCurrentState();
            var fillPaint   = fill is null ? null : Paint(state.CurrentNonStrokingColor, state.AlphaConstantNonStroking, stroking: false);
            var strokePaint = stroke ? Paint(state.CurrentStrokingColor, state.AlphaConstantStroking, stroking: true) : null;

            if (fillPaint is not null || strokePaint is not null)
            {
                FlushRun();
                Add(new PdfPathNode(shape, UserToOutput(), Rule(fill ?? FillingRule.NonZeroWinding),
                    fillPaint, strokePaint, Pen(state)));
            }
        }

        FinishPath(shape);
    }

    /// <summary>Ends a path: the clip <c>W</c> asked for takes effect now, after the painting, as PDF specifies.</summary>
    private void FinishPath(PdfShape shape)
    {
        if (_pendingClip is { } rule && !shape.IsEmpty) Clip(shape, rule, UserToOutput());
        _pendingClip = null;
        _path.Clear();
    }

    /// <summary>Content drawn from here until the graphics state is restored is drawn through <paramref name="shape"/>.</summary>
    private void Clip(PdfShape shape, FillingRule rule, Matrix toOutput)
    {
        FlushRun();
        var writer = new PdfShape.Writer();
        writer.Append(shape, toOutput);
        var clip = new GroupBuilder(PdfGroupRole.Clip) { Clip = writer.Take(), ClipRule = Rule(rule) };
        Open.Items.Add(clip);
        _frames.Pop();
        _frames.Push(clip);
    }

    private static FillRule Rule(FillingRule rule) => rule == FillingRule.EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero;

    private static PdfPen Pen(CurrentGraphicsState state)
    {
        var dash = state.LineDashPattern;
        var dashes = dash.Array is { Count: > 0 } array && HasLength(array) ? array : (IReadOnlyList<double>)[];
        return new PdfPen(
            state.LineWidth,
            state.CapStyle switch
            {
                LineCapStyle.Round            => PenLineCap.Round,
                LineCapStyle.ProjectingSquare => PenLineCap.Square,
                _                             => PenLineCap.Flat,
            },
            state.JoinStyle switch
            {
                LineJoinStyle.Round => PenLineJoin.Round,
                LineJoinStyle.Bevel => PenLineJoin.Bevel,
                _                   => PenLineJoin.Miter,
            },
            state.MiterLimit,
            dashes,
            dashes.Count > 0 ? dash.Phase : 0);
    }

    /// <summary>A dash array of only zeros draws nothing at all — PDF treats it as solid.</summary>
    private static bool HasLength(IReadOnlyList<double> dashes)
    {
        foreach (var d in dashes) if (d > 0) return true;
        return false;
    }

    // ── Paint ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What a colour from the graphics state paints with; null when it paints nothing this reading carries.</summary>
    private PdfPaint? Paint(IColor? colour, double alpha, bool stroking)
    {
        switch (colour)
        {
            case ShadingPatternColor shadingPattern:
                var shading = PdfShadings.TryRead(shadingPattern.Shading, GetCurrentState().RenderingIntent, out var unsupportedShading);
                if (shading is null)
                {
                    Unsupported(unsupportedShading!.Value);
                    return null;
                }

                return new PdfShadingPaint(shading, PatternToOutput(shadingPattern.Matrix), alpha);

            case TilingPatternColor tiling:
                Color? tint = null;
                if (tiling.PaintType == PatternPaintType.Uncoloured)
                {
                    var state = GetCurrentState();
                    var underlying = stroking ? state.CurrentStrokingUnderlyingColor : state.CurrentNonStrokingUnderlyingColor;
                    if (underlying is null) Unsupported(PdfUnsupportedKind.UncolouredPattern, stroking ? "stroke" : "fill");
                    else tint = PdfColours.ToColour(underlying, 1, state.RenderingIntent);
                }

                var cell = _caches.Cell(tiling.PatternStream, () => ReadCell(tiling));
                var box = tiling.BBox;
                return new PdfTilingPaint(
                    cell, new Rect(new Point(box.Left, box.Bottom), new Point(box.Right, box.Top)),
                    new Vector(tiling.XStep, tiling.YStep), PatternToOutput(tiling.Matrix), alpha, tint);

            default:
                return new PdfSolidPaint(PdfColours.ToColour(colour, alpha, GetCurrentState().RenderingIntent));
        }
    }

    /// <summary>From a pattern's space to the output: through the matrix of the stream the pattern is used in.</summary>
    private Matrix PatternToOutput(TransformationMatrix pattern) =>
        PdfSpace.ToMatrix(pattern.Multiply(_streamBases.Peek())) * _output;

    /// <summary>A tiling pattern's cell, read on its own in pattern space with a fresh graphics state.</summary>
    private PdfGroupNode ReadCell(TilingPatternColor tiling)
    {
        var operations = PageContentParser.Parse(PageNumber, new MemoryInputBytes(tiling.Data), ParsingOptions.Logger);
        var resources = tiling.Resources;
        if (resources is not null) ResourceStore.LoadResourceDictionary(resources);
        try
        {
            var cell = new PdfPageInterpreter(PageNumber, ResourceStore, PdfScanner, PageContentParser, FilterProvider,
                new CropBox(tiling.BBox), UserSpaceUnit, default, TransformationMatrix.Identity,
                GetCurrentState().OutputIntentProfile, ParsingOptions,
                _caches, Matrix.Identity, _unsupported, text: null, onPage: false);
            return cell.Process(PageNumber, operations);
        }
        finally
        {
            if (resources is not null) ResourceStore.UnloadResourceDictionary();
        }
    }

    // ── Drawables, and the effects the graphics state puts on them ─────────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="node"/> to the open group — inside a group of its own when the graphics state gives it a
    /// soft mask or a blend mode, which apply to it alone.
    /// </summary>
    private void Add(PdfContentNode node, bool ownSoftMask = false)
    {
        if (_hidden > 0) return;

        // An image with a soft mask of its own is masked by that alone: its SMask takes the place of the graphics state's
        // (ISO 32000-1, table 89), while the blend mode still applies.
        var state = GetCurrentState();
        var mask = !ownSoftMask && state.SoftMask is { } softMask ? Mask(softMask) : null;
        var blend = state.BlendMode;
        if (blend != BlendMode.Normal && !_onPage) Unsupported(PdfUnsupportedKind.BlendMode, blend.ToString());

        if (mask is null && blend == BlendMode.Normal)
        {
            Open.Items.Add(node);
            return;
        }

        Open.Items.Add(new PdfGroupNode(PdfGroupRole.Effects, [node], mask: mask, blend: blend));
    }

    /// <summary>A soft mask's group read once into page space, from where the mask was set.</summary>
    private PdfSoftMask? Mask(SoftMask mask)
    {
        if (_masks.TryGetValue(mask, out var known)) return known;

        PdfSoftMask? read = null;
        if (mask.TransparencyGroup is { } group)
        {


            var reader = new PdfPageInterpreter(PageNumber, ResourceStore, PdfScanner, PageContentParser, FilterProvider,
                _cropBox, UserSpaceUnit, Rotation,
                _maskMatrices.TryGetValue(mask, out var setAt) ? setAt : CurrentTransformationMatrix,
                GetCurrentState().OutputIntentProfile, ParsingOptions, _caches, _output, _unsupported,
                text: null, onPage: false);
            var drawing = reader.ProcessForm(group, NameToken.Create("SMask"));
            read = new PdfSoftMask(
                mask.Subtype == SoftMaskType.Luminosity ? PdfMaskKind.Luminosity : PdfMaskKind.Alpha,
                drawing, Backdrop(mask), PdfMaskTransfer.Of(mask.TransferFunction));
        }

        _masks[mask] = read;
        return read;
    }

    /// <summary>
    /// The colour a mask's group is drawn over: its /BC, in the colour space the group's /CS names (11.6.5.2), or read as
    /// grey, RGB or CMYK by how many components it has where the group names none.
    /// </summary>
    private Color Backdrop(SoftMask mask)
    {
        var components = mask.BC;
        var intent = GetCurrentState().RenderingIntent;
        if (components is { Length: > 0 } && GroupSpace(mask.TransparencyGroup) is { } space
            && space.NumberOfColorComponents == components.Length)
        {
            return PdfColours.ToColour(space.GetColor(components, intent), 1, intent);
        }

        return components switch
        {
            [var grey]                     => Color.FromRgb(PdfColours.ToByte(grey), PdfColours.ToByte(grey), PdfColours.ToByte(grey)),
            [var r, var g, var b]          => Color.FromRgb(PdfColours.ToByte(r), PdfColours.ToByte(g), PdfColours.ToByte(b)),
            [var c, var m, var y, var k]   => PdfCmyk.Colour(c, m, y, k, 255, intent),
            _                              => Colors.Black,
        };
    }

    /// <summary>
    /// The colour space a transparency group's attributes name its own (/CS), or null where they name none, or one this
    /// document does not define.
    /// </summary>
    private ColorSpaceDetails? GroupSpace(StreamToken group)
    {
        if (!group.StreamDictionary.TryGet(NameToken.Group, PdfScanner, out DictionaryToken? attributes)) return null;

        var named = attributes.TryGet(NameToken.Cs, PdfScanner, out NameToken? name) ? name
            : attributes.TryGet(NameToken.Cs, PdfScanner, out ArrayToken? array) && array.Length > 0 && array.Data[0] is NameToken first ? first
            : null;
        if (named is null) return null;

        try
        {
            var space = ResourceStore.GetColorSpaceDetails(named, attributes);
            return space is UnsupportedColorSpaceDetails ? null : space;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // ── XObjects ───────────────────────────────────────────────────────────────────────────────────────────────────

    public override void ApplyXObject(NameToken xObjectName)
    {
        if (_hidden > 0) return;

        if (ResourceStore.TryGetXObject(xObjectName, out var stream))
        {
            if (!Shows(stream.StreamDictionary)) return;

            if (stream.StreamDictionary.TryGet(SubtypeKey, out NameToken subtype) && subtype.Equals(PostScriptName))
            {
                Unsupported(PdfUnsupportedKind.PostScript, xObjectName.Data);
                return;
            }
        }

        // The XObject's reference is what its decoded picture is kept under: the stream token PdfPig hands over is read
        // afresh each time it is asked for, the reference is the same object on every page.
        _xObject = ResourceStore.TryGetXObjectReference(xObjectName, out var reference) ? reference : null;
        try
        {
            base.ApplyXObject(xObjectName);
        }
        finally
        {
            _xObject = null;
        }
    }

    /// <summary>The reference of the XObject being applied, while it is.</summary>
    private IndirectReference? _xObject;

    protected override void ProcessFormXObject(StreamToken formStream, NameToken xObjectName)
    {
        FlushRun();
        var parent = GetCurrentState();
        var transparency = formStream.StreamDictionary.TryGet(GroupKey, PdfScanner, out DictionaryToken? group) &&
                           group.TryGet(NameToken.S, PdfScanner, out NameToken? kind) && kind.Equals(TransparencyName);

        // A transparency group is composited before the parent's alpha, soft mask and blend mode apply to it as a whole;
        // its own content starts from fresh ones (PdfPig resets them inside).
        var form = new GroupBuilder(transparency ? PdfGroupRole.Transparency : PdfGroupRole.Form) { Tag = xObjectName.Data };
        if (transparency)
        {
            form.Opacity = parent.AlphaConstantNonStroking;
            form.Mask    = parent.SoftMask is { } softMask ? Mask(softMask) : null;
            form.Blend   = parent.BlendMode;
            if (form.Blend != BlendMode.Normal && !_onPage) Unsupported(PdfUnsupportedKind.BlendMode, form.Blend.ToString());
        }

        Open.Items.Add(form);
        _nextFrame = form;

        _streamBases.Push(FormMatrix(formStream).Multiply(CurrentTransformationMatrix));
        try
        {
            base.ProcessFormXObject(formStream, xObjectName);
        }
        finally
        {
            _streamBases.Pop();
            _nextFrame = null;
        }
    }

    private TransformationMatrix FormMatrix(StreamToken form)
    {
        if (!form.StreamDictionary.TryGet(MatrixKey, PdfScanner, out ArrayToken? values) || values.Length < 6)
            return TransformationMatrix.Identity;

        Span<double> m = stackalloc double[6];
        for (var i = 0; i < 6; i++) m[i] = values.Data[i] is NumericToken n ? n.Double : (i is 0 or 3 ? 1 : 0);
        return TransformationMatrix.FromValues(m[0], m[1], m[2], m[3], m[4], m[5]);
    }

    protected override void RenderXObjectImage(XObjectContentRecord xObjectContentRecord)
    {
        if (_hidden > 0) return;

        var picture = _caches.Picture(_xObject, () =>
        {
            var image = XObjectFactory.ReadImage(xObjectContentRecord, PdfScanner, FilterProvider, ResourceStore, ParsingOptions);
            var decoded = PdfPicture.TryDecode(image, out var why);
            return (decoded, why);
        }, out var failure);

        var dictionary = xObjectContentRecord.Stream.StreamDictionary;
        Image(picture, failure, ownSoftMask: dictionary.ContainsKey(NameToken.Smask) || PdfJpxSamples.CarriesSoftMask(dictionary));
    }

    protected override void RenderInlineImage(InlineImage inlineImage)
    {
        if (_hidden > 0) return;
        // An inline image has no soft mask of its own: the graphics state's masks it.
        Image(PdfPicture.TryDecode(inlineImage, out var failure), failure, ownSoftMask: false);
    }

    private void Image(PdfPicture? picture, string? failure, bool ownSoftMask)
    {
        if (picture is null)
        {
            Unsupported(PdfUnsupportedKind.UndecodableImage, failure ?? "unknown");
            return;
        }

        FlushRun();
        var state = GetCurrentState();
        var stencil = picture.IsStencil
            ? Paint(state.CurrentNonStrokingColor, state.AlphaConstantNonStroking, stroking: false)
            : null;
        Add(new PdfImageNode(picture, UserToOutput(), state.AlphaConstantNonStroking, stencil), ownSoftMask);
    }

    public override void PaintShading(NameToken shadingName)
    {
        if (_hidden > 0) return;

        var shading = PdfShadings.TryRead(ResourceStore.GetShading(shadingName), GetCurrentState().RenderingIntent, out var unsupported);
        if (shading is null)
        {
            Unsupported(unsupported!.Value);
            return;
        }

        FlushRun();
        Add(new PdfShadingNode(shading, UserToOutput(), GetCurrentState().AlphaConstantNonStroking));
    }

    // ── Marked content and optional content ────────────────────────────────────────────────────────────────────────

    public override void BeginMarkedContent(NameToken name, NameToken? propertyDictionaryName, DictionaryToken? properties)
    {
        properties ??= propertyDictionaryName is null ? null : ResourceStore.GetMarkedContentPropertiesDictionary(propertyDictionaryName);

        // A layer is known by its object, which the resources name: its dictionary alone could be another layer's too.
        IToken? membership = propertyDictionaryName is not null
            && ResourceStore.TryGetMarkedContentPropertiesReference(propertyDictionaryName, out var reference)
                ? new IndirectReferenceToken(reference)
                : properties;
        var hides = name.Equals(OptionalContentKey) && !_caches.OptionalContent.Shows(membership);
        var actual = properties is not null && properties.TryGet(ActualTextKey, PdfScanner, out StringToken? said) ? said.Data : null;

        _marked.Push(new MarkedFrame(hides, _actualText, _actualTextPending));
        if (hides) _hidden++;
        if (actual is not null)
        {
            _actualText = actual;
            _actualTextPending = true;
        }
    }

    public override void EndMarkedContent()
    {
        if (_marked.Count == 0) return;
        var frame = _marked.Pop();
        if (frame.Hides) _hidden--;
        _actualText = frame.ActualText;
        _actualTextPending = frame.ActualTextPending;
    }

    /// <summary>Whether a dictionary that may carry <c>/OC</c> — an XObject's, an annotation's — names a layer that shows.</summary>
    private bool Shows(DictionaryToken dictionary) =>
        !dictionary.TryGet(OptionalContentKey, out IToken? membership) || _caches.OptionalContent.Shows(membership);

    // ── Text ───────────────────────────────────────────────────────────────────────────────────────────────────────

    public override void BeginText()
    {
        base.BeginText();
        _textClip = null;
    }

    public override void EndText()
    {
        FlushRun();
        if (_textClip is { IsEmpty: false } clip) Clip(clip.Take(), FillingRule.NonZeroWinding, Matrix.Identity);
        _textClip = null;
        base.EndText();
    }

    public override void RenderGlyph(
        IFont font, CurrentGraphicsState currentState, double fontSize, double pointSize, int code, string unicode,
        long currentOffset, in TransformationMatrix renderingMatrix, in TransformationMatrix textMatrix,
        in TransformationMatrix transformationMatrix, CharacterBoundingBox characterBoundingBox)
    {
        if (_hidden > 0) return;

        // A state with no font state of its own draws as the default does: filled.
        var mode = (int)(currentState.FontState?.TextRenderingMode ?? UglyToad.PdfPig.Core.TextRenderingMode.Fill);
        var fills   = mode is 0 or 2 or 4 or 6;
        var strokes = mode is 1 or 2 or 5 or 6;
        var clips   = mode >= 4;

        var glyphToUser = renderingMatrix.Multiply(textMatrix);
        var toOutput = PdfSpace.ToMatrix(glyphToUser.Multiply(transformationMatrix)) * _output;
        var text = GlyphText(unicode);

        if (font is IType3Font type3)
        {
            if (fills || strokes) GlyphProcedure(type3, code, glyphToUser);
            Glyph(font, currentState, null, toOutput, text, characterBoundingBox, fills: false, strokes: false);
            return;
        }

        var outline = _caches.Glyphs.Outline(font, PdfScanner, code, unicode, characterBoundingBox.Width);
        if (outline is null && !string.IsNullOrWhiteSpace(unicode))
            Unsupported(PdfUnsupportedKind.MissingGlyph, Clean(font.Name?.Data));

        if (clips && outline is { IsEmpty: false })
        {
            _textClip ??= new PdfShape.Writer();
            _textClip.Append(outline, toOutput);
        }

        Glyph(font, currentState, outline, toOutput, text, characterBoundingBox, fills, strokes);
    }

    /// <summary>What a glyph says, giving way to the <c>/ActualText</c> of the marked content it is in.</summary>
    private string GlyphText(string unicode)
    {
        if (_actualText is null) return unicode;
        if (!_actualTextPending) return string.Empty;
        _actualTextPending = false;
        return _actualText;
    }

    private void Glyph(
        IFont font, CurrentGraphicsState state, PdfShape? outline, Matrix toOutput, string text,
        CharacterBoundingBox box, bool fills, bool strokes)
    {
        // One text-showing operator sets one run: nothing about its paint can change part-way through it.
        var key = new RunKey(font, TextSequence);
        if (_run is null || !_run.Key.Equals(key))
        {
            FlushRun();
            var fill   = fills ? Paint(state.CurrentNonStrokingColor, state.AlphaConstantNonStroking, stroking: false) : null;
            var stroke = strokes ? Paint(state.CurrentStrokingColor, state.AlphaConstantStroking, stroking: true) : null;
            var pen = Pen(state);
            _run = new RunBuilder(key, fill, stroke,
                pen with { Width = pen.Width * PdfSpace.MeanScale(UserToOutput()) },
                Clean(font.Name?.Data), font.IsVertical);
        }

        var (ascent, descent) = Extent(font);
        var advance = new Rect(new Point(0, descent), new Point(Math.Max(box.Width, 0), ascent));
        _run.Glyphs.Add(new PdfGlyph(outline, toOutput, text, Math.Max(box.Width, 0), PdfSpace.Transform(advance, toOutput)));
    }

    /// <summary>
    /// How far a font's glyphs reach above and below the baseline, in ems — its descriptor's ascent and descent through
    /// its font matrix, or the proportions of an ordinary Latin face when the descriptor gives none.
    /// </summary>
    private static (double Ascent, double Descent) Extent(IFont font)
    {
        var matrix = font.GetFontMatrix();
        var ascent  = matrix.Transform(0, font.GetAscent()).Item2 - matrix.F;
        var descent = matrix.Transform(0, font.GetDescent()).Item2 - matrix.F;
        if (ascent <= 0 || ascent > 2) ascent = 0.8;
        if (descent >= 0 || descent < -1) descent = -0.2;
        return (ascent, descent);
    }

    /// <summary>A Type 3 glyph: its procedure drawn where the glyph is set, in the graphics state it is set in.</summary>
    private void GlyphProcedure(IType3Font font, int code, TransformationMatrix glyphToUser)
    {
        if (_glyphProcedures >= GlyphProcedureDepth || !font.TryGetCharProc(code, out var procedure)) return;

        var operations = PageContentParser.Parse(PageNumber,
            new MemoryInputBytes(procedure.Decode(FilterProvider, PdfScanner)), ParsingOptions.Logger);
        var resources = font.Type3Resources;
        if (resources is not null) ResourceStore.LoadResourceDictionary(resources);

        var saved = (TextMatrices.TextMatrix, TextMatrices.TextLineMatrix);
        var glyph = new GroupBuilder(PdfGroupRole.Glyph);
        Open.Items.Add(glyph);
        _glyphProcedures++;
        try
        {
            _nextFrame = glyph;
            PushState();
            ModifyCurrentTransformationMatrix(font.GetFontMatrix().Multiply(glyphToUser));
            ProcessOperations(operations);
            PopState();
        }
        finally
        {
            _glyphProcedures--;
            (TextMatrices.TextMatrix, TextMatrices.TextLineMatrix) = saved;
            if (resources is not null) ResourceStore.UnloadResourceDictionary();
        }
    }

    /// <summary>
    /// Closes the run being set: it joins the open group, and its text joins the page's — after a line break when it
    /// starts on a new line, a space when it starts a gap away along the same one.
    /// </summary>
    private void FlushRun()
    {
        // A Type 3 glyph's procedure paints inside the run that sets the glyph; the run closes when its text does.
        if (_glyphProcedures > 0) return;

        var run = _run;
        _run = null;
        if (run is null || run.Glyphs.Count == 0) return;

        var said = string.Concat(run.Glyphs.Select(static g => g.Text));
        var start = 0;
        if (_text is not null && said.Length > 0)
        {
            if (_lastBox is { } before) _text.Append(Separator(before, run.Glyphs[0].Box, said, _text));
            start = _text.Length;
            _text.Append(said);
        }

        // A run that paints nothing still counts: invisible text over a scanned page is how its words are found.
        var made = new PdfTextNode(run.Glyphs, run.Fill, run.Stroke, run.Pen, run.FontName, run.Vertical, start);
        if (said.Length > 0)
        {
            _lastBox = run.Glyphs[^1].Box;
            if (_text is not null) Runs.Add(made);
        }

        Add(made);
    }

    /// <summary>
    /// What separates one run's text from the next: a line break when the next starts on another line, a space when it
    /// starts a gap away along the same one, nothing when the two read as one word.
    /// </summary>
    private static string Separator(Rect end, Rect start, string next, StringBuilder text)
    {
        if (text.Length == 0 || char.IsWhiteSpace(text[^1]) || char.IsWhiteSpace(next[0])) return string.Empty;
        if (end.IsEmpty || start.IsEmpty) return " ";

        var height = Math.Max(Math.Min(end.Height, start.Height), 1e-3);
        var overlap = Math.Min(end.Bottom, start.Bottom) - Math.Max(end.Top, start.Top);
        if (overlap < height / 2) return "\n";

        var gap = start.Left - end.Right;
        return gap > height * 0.15 || gap < -height ? " " : string.Empty;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private Matrix UserToOutput() => PdfSpace.ToMatrix(CurrentTransformationMatrix) * _output;

    private void Unsupported(PdfUnsupportedKind kind, string detail) => Unsupported(new PdfUnsupported(kind, detail));

    /// <summary>Records what was not carried, once per kind and detail on a page.</summary>
    private void Unsupported(PdfUnsupported what)
    {
        if (_reported.Add($"{what.Kind}:{what.Detail}")) _unsupported.Add(what);
    }

    private static string Clean(string? fontName) =>
        fontName is { Length: > 7 } && fontName[6] == '+' ? fontName[7..] : fontName ?? string.Empty;

    /// <summary>A group being gathered: its items are nodes, and groups still being gathered, in painting order.</summary>
    private sealed class GroupBuilder(PdfGroupRole role)
    {
        public PdfGroupRole Role { get; } = role;
        public PdfShape? Clip { get; init; }
        public FillRule ClipRule { get; init; } = FillRule.Nonzero;
        public double Opacity { get; set; } = 1;
        public PdfSoftMask? Mask { get; set; }
        public BlendMode Blend { get; set; } = BlendMode.Normal;
        public string? Tag { get; init; }
        public List<object> Items { get; } = [];

        /// <summary>
        /// The group as a node. A group left with nothing in it is dropped by its parent; a group that changes
        /// nothing about how its one child looks gives way to the child.
        /// </summary>
        public PdfGroupNode Build()
        {
            var children = new List<PdfContentNode>(Items.Count);
            foreach (var item in Items)
            {
                var node = item is GroupBuilder nested ? nested.Build() : (PdfContentNode)item;
                if (node is PdfGroupNode { Children.Count: 0 }) continue;
                children.Add(node);
            }

            return new PdfGroupNode(Role, children, Clip, ClipRule, Opacity, Mask, Blend, Tag);
        }
    }

    /// <summary>What decides that two glyphs belong to the same run.</summary>
    private readonly record struct RunKey(IFont Font, int Sequence);

    private sealed class RunBuilder(RunKey key, PdfPaint? fill, PdfPaint? stroke, PdfPen pen, string fontName, bool vertical)
    {
        public RunKey Key { get; } = key;
        public PdfPaint? Fill { get; } = fill;
        public PdfPaint? Stroke { get; } = stroke;
        public PdfPen Pen { get; } = pen;
        public string FontName { get; } = fontName;
        public bool Vertical { get; } = vertical;
        public List<PdfGlyph> Glyphs { get; } = [];
    }

    private readonly record struct MarkedFrame(bool Hides, string? ActualText, bool ActualTextPending);
}
