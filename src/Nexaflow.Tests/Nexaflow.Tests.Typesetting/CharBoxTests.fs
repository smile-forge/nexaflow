namespace WpfMath.Tests

open System

open Foq
open Xunit

open WpfMath.Fonts
open WpfMath.Parsers
open WpfMath.Rendering
open XamlMath
open XamlMath.Boxes
open XamlMath.Exceptions
open XamlMath.Rendering

type CharBoxTests() =
    static do Utils.initializeFontResourceLoading()

    let environment = WpfTeXEnvironment.Create()



    [<Fact>]
    member _.``Currently unsupported characters like "Å" should result in TexCharacterMappingNotFoundException``() =
        Assert.IsType<TexCharacterMappingNotFoundException>(
            environment.MathFont.GetDefaultCharInfo('Å', TexStyle.Display).Error)

    [<Fact>]
    member _.``CharBox GetGlyphRun for \text{∅} should throw the TexCharacterMappingNotFoundException``() =
        let atom = Utils.parseRoot @"\text{∅}"
        let charBox : CharBox = downcast atom.CreateBox(environment)
        let action = Func<obj>(fun () -> upcast WpfCharInfoEx.GetGlyphRun(charBox.Character, 20.0, 0.5, 1.0))
        let exc = Assert.Throws<TexCharacterMappingNotFoundException>(action)
        Assert.Equal("The Arial font does not support '∅' (U+2205) character.", exc.Message)
