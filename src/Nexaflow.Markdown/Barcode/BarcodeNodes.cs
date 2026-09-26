using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Barcode;

/// <summary>
/// A barcode block as its stage leaves it (<see cref="Stages.EncodeBarcode"/>): how it is drawn, and why its value will not encode
/// where that is said under the value rather than by showing the block as written. What is drawn hangs under it: the bars
/// (<see cref="BarcodeBarsNode"/>) and what is printed with them (<see cref="BarcodeRunNode"/>). It prints as the fields written.
/// </summary>
internal sealed class BarcodeBlockNode : ContentNode
{
    internal BarcodeBlockNode(ContentNode written, BarcodeBlock settings, string? refusal) : base(written)
    {
        this.Settings = settings;
        this.Refusal = refusal;
    }

    /// <summary>The format, and how big and in what colours it is drawn.</summary>
    public BarcodeBlock Settings { get; }

    /// <summary>
    /// Why the value will not encode, where it is typed into where it is printed: the bars are a faint symbol of its kind, struck
    /// through, and this goes under the value. Null where it encodes, and where nothing is written yet.
    /// </summary>
    public string? Refusal { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new BarcodeBlockNode(shape, this.Settings, this.Refusal);
}

/// <summary>
/// The bars as drawn: the runs of ink along a row of equal modules, the guards that run down past the digits, and where an add-on
/// begins. Stands for nothing anybody wrote.
/// </summary>
internal sealed class BarcodeBarsNode : ContentNode
{
    internal BarcodeBarsNode(BarcodePattern drawn, bool standIn, int addOn, int main)
        : this(ContentNode.Leaf(BarcodeKinds.Bars, string.Empty, Roles.Derived), drawn.Width, [.. drawn.InkRuns()], drawn.Guards, standIn, addOn, main) { }

    private BarcodeBarsNode(ContentNode shape, int modules, IReadOnlyList<(int Start, int Length)> ink,
                            IReadOnlyList<(int Start, int Length)> guards, bool standIn, int addOn, int main)
        : base(shape)
    {
        this.Modules = modules;
        this.Ink = ink;
        this.Guards = guards;
        this.StandIn = standIn;
        this.AddOn = addOn;
        this.Main = main;
    }

    /// <summary>How many modules wide the bars are, quiet zones excluded.</summary>
    public int Modules { get; }

    /// <summary>Each run of ink, as its first module and how many.</summary>
    public IReadOnlyList<(int Start, int Length)> Ink { get; }

    /// <summary>Stretches of bar that run down past the digits: an EAN's start, centre and end guards.</summary>
    public IReadOnlyList<(int Start, int Length)> Guards { get; }

    /// <summary>Whether these bars are a symbol of the block's kind standing in for a value that will not encode or is not written.</summary>
    public bool StandIn { get; }

    /// <summary>The first module of an add-on, which lifts clear of the main bars — or <see cref="int.MaxValue"/> where there is none.</summary>
    public int AddOn { get; }

    /// <summary>How many modules wide the main symbol is: everything before an add-on.</summary>
    public int Main { get; }

    protected override ContentNode Reshaped(ContentNode shape) =>
        new BarcodeBarsNode(shape, this.Modules, this.Ink, this.Guards, this.StandIn, this.AddOn, this.Main);
}

/// <summary>
/// A run of what is printed with the bars — the caption over a publication's symbol, or a group of its number — and where it goes
/// against the bars. Its parts are what it prints, in order: a character of the value (<see cref="BarcodePrintedNode"/>) or
/// something worked out from it (<see cref="BarcodeKinds.Worked"/>). A run nothing of the value is in has no parts and is
/// <see cref="BarcodeKinds.Worked"/> whole. Stands for nothing anybody wrote.
/// </summary>
internal sealed class BarcodeRunNode : ContentNode
{
    internal BarcodeRunNode(ContentNode shape, BarcodeTextRun run) : base(shape) => this.Run = run;

    /// <summary>What it prints, and the stretch of bars it belongs to.</summary>
    public BarcodeTextRun Run { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new BarcodeRunNode(shape, this.Run);
}

/// <summary>
/// A printed character that is a character of the value: the one kind of thing printed with the bars that somebody typed, and so
/// the one a caret can stand beside. It prints what <see cref="Character"/> is and stands for nothing itself.
/// </summary>
internal sealed class BarcodePrintedNode : ContentNode
{
    internal BarcodePrintedNode(ContentNode character)
        : this(ContentNode.Leaf(BarcodeKinds.Printed, character.Text, Roles.Derived), character) { }

    private BarcodePrintedNode(ContentNode shape, ContentNode character) : base(shape) => this.Character = character;

    /// <summary>The character of the value as written.</summary>
    public ContentNode Character { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new BarcodePrintedNode(shape, this.Character);
}
