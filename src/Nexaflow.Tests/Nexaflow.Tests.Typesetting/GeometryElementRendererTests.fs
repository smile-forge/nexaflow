namespace WpfMath.Tests

open System.Windows.Media

open Foq
open Xunit

open WpfMath.Fonts
open WpfMath.Rendering
open XamlMath
open XamlMath.Boxes
open XamlMath.Rendering
open XamlMath.Rendering.Transformations

type GeometryElementRendererTests() =
    static do Utils.initializeFontResourceLoading()

    let geometry = GeometryGroup()
    let renderer = GeometryElementRenderer(geometry, 1.0) :> IElementRenderer

    [<Fact>]
    member _.``GeometryElementRenderer.RenderElement delegates to element.RenderTo``() : unit =
        let box = Mock.Of<Box>()
        renderer.RenderElement(box, 1.0, 2.0)
        Mock.Verify(<@ box.RenderTo(renderer, 1.0, 2.0) @>, once)

    [<Fact>]
    member _.``GeometryElementRenderer.RenderCharacter adds a PathGeometry group``() : unit =
        let font = DefaultTexFont(WpfMathFontProvider.Instance, 20.0)
        let environment = TexEnvironment(TexStyle.Display, font, font)
        let char = environment.MathFont.GetDefaultCharInfo('x', TexStyle.Display).Value
        renderer.RenderCharacter(char, 0.0, 0.0, WpfExtensions.ToPlatform Brushes.Black)

        let group = Seq.exactlyOne geometry.Children :?> GeometryGroup
        Assert.IsType<PathGeometry>(Seq.exactlyOne group.Children) |> ignore

    [<Fact>]
    member _.``GeometryElementRenderer.RenderRectangle adds a RectangleGeometry``() : unit =
        let rect = Rectangle(1.0, 2.0, 3.0, 4.0)
        renderer.RenderRectangle(rect, null)

        Assert.IsType<RectangleGeometry>(Seq.exactlyOne geometry.Children) |> ignore

    [<Fact>]
    member _.``GeometryElementRenderer.RenderTransformed adds a GeometryGroup``() : unit =
        renderer.RenderTransformed(HorizontalBox(), [| Transformation.Translate(1.0, 1.0) |], 0.0, 0.0)
        Assert.IsType<GeometryGroup>(Seq.exactlyOne geometry.Children) |> ignore

    [<Fact>]
    member _.``GeometryElementRenderer.RenderTransformed applies the transformations``() : unit =
        // Matrix is a struct, so the transformations have to be assigned back to the geometry rather
        // than applied to the copy Transform.Value hands out — they used to be dropped silently.
        renderer.RenderTransformed(HorizontalBox(), [| Transformation.Translate(3.0, 4.0) |], 0.0, 0.0)
        let group = Seq.exactlyOne geometry.Children :?> GeometryGroup
        Assert.Equal(System.Windows.Point(3.0, 4.0), group.Transform.Value.Transform(System.Windows.Point(0.0, 0.0)))

    [<Fact>]
    member _.``GeometryElementRenderer.RenderTransformed rotates before it translates``() : unit =
        // The drawing-context renderer pushes the transformations in this order, and there the last
        // one pushed is the first the content sees; the geometry has to agree with it.
        renderer.RenderTransformed(
            HorizontalBox(),
            [| Transformation.Translate(10.0, 0.0); Transformation.Rotate(90.0) |],
            0.0,
            0.0)
        let group = Seq.exactlyOne geometry.Children :?> GeometryGroup
        let moved: System.Windows.Point = group.Transform.Value.Transform(System.Windows.Point(1.0, 0.0))
        Assert.Equal(10.0, moved.X, 6)
        Assert.Equal(1.0, moved.Y, 6)
