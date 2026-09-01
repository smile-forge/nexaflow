module WpfMath.Tests.Utils

open System
open System.Windows

open Nexaflow.Maths.Latex
open WpfMath.Parsers
open XamlMath

let initializeFontResourceLoading =
    let monitor = obj()
    fun () ->
        lock monitor (fun () ->
            if not(UriParser.IsKnownScheme "pack")
            then new Application() |> ignore)

/// The tables: which symbol a name means, which face it is set in, and what there is a drawing for.
/// All that is left of TexFormulaParser now that it reads nothing.
let private knowledge = WpfTeXFormulaParser.Instance

/// Reads markup and builds a formula from the reading, exactly as the app does.
///
/// The engine used to read its own LaTeX, and every file in this suite called TexFormulaParser.Parse.
/// That reader is gone: reading is Nexaflow.Maths' job now and the parse tree is the thing the boxes
/// are built from, so the suite says the whole pipeline out loud rather than naming a parser that no
/// longer parses.
///
/// Which is also the point of keeping these tests. They pin the shape the engine built for 148
/// formulas, recorded from the very parser that has been replaced — so running them through the
/// replacement is the one direct comparison of the two, and an approval that still passes is a shape
/// carried across intact.
let readAndBuild (markup: string) =
    let read = TexPipeline.Read(markup, (fun name -> TexFormulaBuilder.Draws(name, knowledge)))
    let reading = TexReading.Of read
    reading, TexFormulaBuilder.Build(reading.Root, knowledge)

/// The formula. Fails only where nothing at all could be set as maths — which the app answers by
/// setting the source as its own characters, a decision about presentation that belongs there.
let parse (markup: string) : TexFormula =
    let _, formula = readAndBuild markup
    if isNull (box formula) then failwithf "nothing in '%s' could be set as maths" markup
    formula

/// The atom at the top of it, which is what most of this suite is really asking about.
let parseRoot (markup: string) = (parse markup).RootAtom

/// The stretches the reader could not read at all — a command nobody has heard of, and the like.
///
/// Nothing throws any more. Where the old parser raised a TexParseException and abandoned the
/// formula, a stretch it cannot take at face value is carried through and marked, and the reader is
/// shown the characters they typed with a squiggle under them. So the question every test that used
/// to say `assertParseThrows` now asks is not "which exception, with which message" but "which
/// stretch, and in which of the two ways" — this one, or <see cref="undrawn"/>.
let unreadable (markup: string) : string list =
    let reading, _ = readAndBuild markup
    reading.Root.SelfAndDescendants()
    |> Seq.filter (fun part -> not (isNull part.Trouble))
    |> Seq.map (fun part -> part.Print())
    |> Seq.distinct
    |> List.ofSeq

/// What the reading has to say about each of those, which is what the reader is shown on hovering it.
let reasons (markup: string) : string list =
    let reading, _ = readAndBuild markup
    reading.Root.SelfAndDescendants()
    |> Seq.choose (fun part -> if isNull part.Trouble then None else Some part.Trouble)
    |> Seq.distinct
    |> List.ofSeq

/// The stretches that were read but have no drawing, and so are set as the characters written rather
/// than as maths. The other of the two ways — see <see cref="unreadable"/>.
let undrawn (markup: string) : string list =
    let _, formula = readAndBuild markup
    if isNull (box formula) then [ markup ]
    else formula.Ignored |> Seq.map (fun part -> part.Print()) |> Seq.distinct |> List.ofSeq
