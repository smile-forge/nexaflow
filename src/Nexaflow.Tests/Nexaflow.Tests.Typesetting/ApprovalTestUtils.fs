module WpfMath.Tests.ApprovalTestUtils

open System
open System.Globalization
open System.IO
open System.Text
open System.Reflection
open System.Windows.Media

open ApprovalTests
open ApprovalTests.Namers
open ApprovalTests.Reporters
open ApprovalTests.Writers
open Newtonsoft.Json
open Newtonsoft.Json.Converters
open Newtonsoft.Json.Serialization

open WpfMath.Fonts
open WpfMath.Parsers
open WpfMath.Rendering
open XamlMath.Atoms

type private BomlessFileWriter(data: string, ?extensionWithoutDot: string) =
    inherit ApprovalTextWriter(data, defaultArg extensionWithoutDot "txt")
    override this.WriteReceivedFile(received: string): string =
        Directory.CreateDirectory(Path.GetDirectoryName(received)) |> ignore
        File.WriteAllText(received, this.Data)
        received

// Quiet rather than DiffReporter: this suite is run headlessly from inside Nexaflow, and a reporter that
// launches a diff tool opens one window per failing test — a hundred and forty of them on a first run.
// The .received.txt files are written either way, so nothing is lost; diff them yourself when you want to.
[<assembly: UseReporter(typeof<QuietReporter>)>]
[<assembly: UseApprovalSubdirectory("TestResults")>]
do
    WriterFactory.TextWriterCreator <- Func<_, _>(fun data -> upcast BomlessFileWriter data)
    WriterFactory.TextWriterWithExtensionCreator <- Func<_, _, _>(fun data extensionWithoutDot ->
        upcast BomlessFileWriter(data, extensionWithoutDot)
    )

type private InnerPropertyContractResolver() =
    inherit DefaultContractResolver()
    member private _.DoCreateProperty(p, ms) =
        base.CreateProperty(p, ms, Readable = true)

    override this.CreateProperties(``type``, memberSerialization) =
        // All properties including internal ones:
        let properties =
            ``type``.GetProperties(BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Instance)
            |> Seq.filter(fun p -> Array.isEmpty <| p.GetIndexParameters()) // no indexers
            |> Seq.filter(fun p -> p.Name <> "EqualityContract") // no EqualityContract generated for records
            // TexFormula.Root is a public view of RootAtom, which is serialized a few lines further down
            // in every one of these files. Taking both would put the whole atom tree in each of them
            // twice, for no assurance the first copy does not already give.
            |> Seq.filter(fun p -> not (p.DeclaringType = typeof<XamlMath.TexFormula> && p.Name = "Root"))
            // Atom.Origin points back at the parse tree a formula was built from. It is null for every
            // formula the parser read, which is all of them here — so taking it would put a null on every
            // atom of all 148 files, and a whole parse tree on each one the day a test builds a formula
            // the other way. What these record is what was built, not where it came from.
            |> Seq.filter(fun p -> p.Name <> "Origin")
            |> Seq.sortBy(fun p -> p.Name)
            |> Seq.map(fun p -> this.DoCreateProperty(p, memberSerialization))

        upcast [|
            // For Atoms, type name should be first
            if typeof<Atom>.IsAssignableFrom ``type`` then
                JsonProperty(
                    PropertyName = "[AtomType]",
                    PropertyType = typeof<string>,
                    Readable = true,
                    ValueProvider = {
                        new IValueProvider with
                            member this.GetValue _ = upcast ``type``.Name
                            member this.SetValue(_, _) = failwith "Not supported"
                    }
                )

            yield! properties
        |]

[<AbstractClass>]
type ReadOnlyJsonConverter<'a>() =
    inherit JsonConverter<'a>()
    override _.CanRead = false
    override _.ReadJson(_, _, _, _, _) = failwith "Not supported"

type private WpfGlyphTypefaceConverter() =
    inherit ReadOnlyJsonConverter<WpfGlyphTypeface>()
    override _.WriteJson(writer: JsonWriter, value: WpfGlyphTypeface, serializer: JsonSerializer) =
        serializer.Serialize(writer, value.Typeface)

type private GlyphTypefaceConverter() =
    inherit ReadOnlyJsonConverter<GlyphTypeface>()
    override _.WriteJson(writer: JsonWriter, value: GlyphTypeface, _: JsonSerializer) =
        if isNull value then
            writer.WriteNull()
        else
            writer.WriteValue(value.FontUri)

/// This converter should provide the same results on both .NET 4.6.1 and .NET Core 3.0 which is important for approval
/// tests. The roundtrippable double formatting (used by default) differs between these frameworks.
type private UniversalDoubleConverter() =
    inherit ReadOnlyJsonConverter<float>()
    override _.WriteJson(writer: JsonWriter, value: float, _: JsonSerializer) =
        let stringified = value.ToString("0.0###############", CultureInfo.InvariantCulture)
        writer.WriteRawValue stringified

type private WpfBrushConverter() =
    inherit ReadOnlyJsonConverter<WpfBrush>()
    override _.WriteJson(writer: JsonWriter, value: WpfBrush, _: JsonSerializer) =
        let stringified =
            match value.Value with
            | null -> null
            | _ -> value.Value.ToString()
        writer.WriteValue stringified

let private jsonSettings = JsonSerializerSettings(ContractResolver = InnerPropertyContractResolver(),
                                                  Formatting = Formatting.Indented,
                                                  Converters = [|
                                                      StringEnumConverter()
                                                      GlyphTypefaceConverter()
                                                      UniversalDoubleConverter()
                                                      WpfGlyphTypefaceConverter()
                                                      WpfBrushConverter()
                                                  |])

let private serialize o =
    JsonConvert.SerializeObject(o, jsonSettings).Replace("\r\n", "\n")

let verifyObject: obj -> unit =
    serialize >> Approvals.Verify

let verifyParseResult (formulaText: string): unit =
    let parser = WpfTeXFormulaParser.Instance
    let formula = parser.Parse formulaText
    verifyObject formula

let verifyParseResultScenario (scenario: string) (formulaText: string): unit =
    use block = NamerFactory.AsEnvironmentSpecificTest $"(%s{scenario})"
    verifyParseResult formulaText

let processSpecialChars (text: string): string =
    (StringBuilder text)
        .Replace("\\", "")
        .Replace('{', '(')
        .Replace('}', ')')
        .Replace('^', '↑')
        .ToString()
