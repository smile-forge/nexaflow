namespace Nexaflow.Features.Solver.Palette;

/// <summary>
/// The scientific keypad, shared by the Calc and Text tabs.
/// <para>
/// Three pages of the same eight-column grid rather than one wall of keys: the main pad has the
/// digits and the functions people reach for constantly, and the two shift keys in the top-left
/// swap in the inverse/hyperbolic functions and the constants. Every page keeps the digit block in
/// the same place, so paging never moves the keys you were about to press.
/// </para>
/// <para>
/// What a key <i>does</i> matters as much as what it says — see <see cref="KeyInsert"/>. A function
/// brackets your selection instead of replacing it, a factorial follows the number rather than
/// erasing it, and a square does nothing when there is nothing to square.
/// </para>
/// </summary>
public static class CalcPalette
{
    /// <summary>Width of every page. The grid is laid out by filling rows.</summary>
    public const int Columns = 8;

    /// <summary>Id of the shift key that swaps in the inverse and hyperbolic functions.</summary>
    public const string SecondPageId = "calc.second";

    /// <summary>Id of the shift key that swaps in the constants.</summary>
    public const string ConstPageId = "calc.const";

    /// <summary>Id of the clear key.</summary>
    public const string ClearId = "calc.clear";

    /// <summary>Id of the backspace key.</summary>
    public const string BackspaceId = "calc.backspace";

    /// <summary>Id of the degrees/radians toggle.</summary>
    public const string AngleToggleId = "calc.angle";

    /// <summary>The default page.</summary>
    public static PaletteGroup Main { get; } = new("calc.main", "Main",
    [
        Cmd("2nd", SecondPageId, "Inverse and hyperbolic functions"),
        Cmd("π e", ConstPageId, "Constants"),
        Key("(", "(", "Open bracket", PaletteKeyKind.Operator),
        Key(")", ")", "Close bracket", PaletteKeyKind.Operator),
        Cmd("⌫", BackspaceId, "Delete the character before the caret"),
        Cmd("C", ClearId, "Clear the definition"),
        Key("÷", " / ", "Divide", PaletteKeyKind.Operator),
        Key("×", " * ", "Multiply", PaletteKeyKind.Operator),

        Fn("sin", "sin", "Sine of the selection, or of what you type next"),
        Fn("cos", "cos", "Cosine"),
        Fn("tan", "tan", "Tangent"),
        Digit("7"), Digit("8"), Digit("9"),
        Key("−", " - ", "Subtract", PaletteKeyKind.Operator),
        Post("xʸ", "^", "Raise what precedes it to a power", PaletteKeyKind.Operator),

        Fn("ln", "ln", "Natural logarithm"),
        Fn("log", "log", "Logarithm — log(base, value)"),
        Wrap("eˣ", "e^(", ")", "e to a power", PaletteKeyKind.Function),
        Digit("4"), Digit("5"), Digit("6"),
        Key("+", " + ", "Add", PaletteKeyKind.Operator),
        Fn("√", "sqrt", "Square root"),

        Key("π", "pi", "3.14159…", PaletteKeyKind.Constant),
        Key("e", "e", "2.71828…", PaletteKeyKind.Constant),
        Post("n!", "!", "Factorial of what precedes it", PaletteKeyKind.Function),
        Digit("1"), Digit("2"), Digit("3"),
        Post("%", "%", "Remainder — follows a number", PaletteKeyKind.Operator),
        Post("x²", "^2", "Square what precedes it", PaletteKeyKind.Operator),

        Cmd("DEG", AngleToggleId, "Switch between degrees and radians"),
        Fn("|x|", "abs", "Absolute value of the selection"),
        Fn("⌊x⌋", "floor", "Round down"),
        Digit("0"), Digit("."),
        Key(",", ", ", "Separator — also separates a list of numbers for the stats chips", PaletteKeyKind.Operator),
        Key("x", "x", "The variable x", PaletteKeyKind.Symbol),
        Key("y", "y", "The variable y", PaletteKeyKind.Symbol),
    ]);

    /// <summary>Behind the <c>2nd</c> key: inverses and hyperbolics.</summary>
    public static PaletteGroup Second { get; } = new("calc.second.page", "2nd",
    [
        Cmd("2nd", SecondPageId, "Back to the main keys"),
        Cmd("π e", ConstPageId, "Constants"),
        Key("(", "(", "Open bracket", PaletteKeyKind.Operator),
        Key(")", ")", "Close bracket", PaletteKeyKind.Operator),
        Cmd("⌫", BackspaceId, "Delete the character before the caret"),
        Cmd("C", ClearId, "Clear the definition"),
        Key("÷", " / ", "Divide", PaletteKeyKind.Operator),
        Key("×", " * ", "Multiply", PaletteKeyKind.Operator),

        Fn("sin⁻¹", "arcsin", "Inverse sine"),
        Fn("cos⁻¹", "arccos", "Inverse cosine"),
        Fn("tan⁻¹", "arctan", "Inverse tangent"),
        Digit("7"), Digit("8"), Digit("9"),
        Key("−", " - ", "Subtract", PaletteKeyKind.Operator),
        Wrap("ˣ√", "^(1/", ")", "Nth root — the root goes in the brackets", PaletteKeyKind.Operator),

        Fn("sinh", "sinh", "Hyperbolic sine"),
        Fn("cosh", "cosh", "Hyperbolic cosine"),
        Fn("tanh", "tanh", "Hyperbolic tangent"),
        Digit("4"), Digit("5"), Digit("6"),
        Key("+", " + ", "Add", PaletteKeyKind.Operator),
        Post("∛", "^(1/3)", "Cube root of what precedes it", PaletteKeyKind.Operator),

        Fn("sec", "sec", "Secant"),
        Fn("csc", "csc", "Cosecant"),
        Fn("cot", "cot", "Cotangent"),
        Digit("1"), Digit("2"), Digit("3"),
        Key("mod", " mod ", "Remainder after division", PaletteKeyKind.Operator),
        Post("x³", "^3", "Cube what precedes it", PaletteKeyKind.Operator),

        Cmd("DEG", AngleToggleId, "Switch between degrees and radians"),
        Fn("⌈x⌉", "ceil", "Round up"),
        Fn("sgn", "signum", "Sign of a number"),
        Digit("0"), Digit("."),
        Key(",", ", ", "Separator", PaletteKeyKind.Operator),
        Key("a", "a", "The variable a", PaletteKeyKind.Symbol),
        Key("n", "n", "The variable n", PaletteKeyKind.Symbol),
    ]);

    /// <summary>Behind the <c>π e</c> key: the constants worth not having to look up.</summary>
    public static PaletteGroup Constants { get; } = new("calc.const.page", "Constants",
    [
        Cmd("2nd", SecondPageId, "Inverse and hyperbolic functions"),
        Cmd("π e", ConstPageId, "Back to the main keys"),
        Key("(", "(", "Open bracket", PaletteKeyKind.Operator),
        Key(")", ")", "Close bracket", PaletteKeyKind.Operator),
        Cmd("⌫", BackspaceId, "Delete the character before the caret"),
        Cmd("C", ClearId, "Clear the definition"),
        Key("÷", " / ", "Divide", PaletteKeyKind.Operator),
        Key("×", " * ", "Multiply", PaletteKeyKind.Operator),

        Key("π", "pi", "Pi — 3.14159…", PaletteKeyKind.Constant),
        Key("τ", "(2 * pi)", "Tau — two pi", PaletteKeyKind.Constant),
        Key("e", "e", "Euler's number — 2.71828…", PaletteKeyKind.Constant),
        Digit("7"), Digit("8"), Digit("9"),
        Key("−", " - ", "Subtract", PaletteKeyKind.Operator),
        Post("xʸ", "^", "Raise what precedes it to a power", PaletteKeyKind.Operator),

        Key("φ", "((1 + sqrt(5)) / 2)", "Golden ratio — 1.61803…", PaletteKeyKind.Constant),
        Key("√2", "sqrt(2)", "1.41421…", PaletteKeyKind.Constant),
        Key("√3", "sqrt(3)", "1.73205…", PaletteKeyKind.Constant),
        Digit("4"), Digit("5"), Digit("6"),
        Key("+", " + ", "Add", PaletteKeyKind.Operator),
        Fn("√", "sqrt", "Square root"),

        Key("i", "i", "The imaginary unit", PaletteKeyKind.Constant),
        Key("∞", "+oo", "Infinity", PaletteKeyKind.Constant),
        Key("½", "(1/2)", "One half", PaletteKeyKind.Constant),
        Digit("1"), Digit("2"), Digit("3"),
        Post("%", "%", "Remainder — follows a number", PaletteKeyKind.Operator),
        Post("x²", "^2", "Square what precedes it", PaletteKeyKind.Operator),

        Cmd("DEG", AngleToggleId, "Switch between degrees and radians"),
        Key("⅓", "(1/3)", "One third", PaletteKeyKind.Constant),
        Key("¼", "(1/4)", "One quarter", PaletteKeyKind.Constant),
        Digit("0"), Digit("."),
        Key(",", ", ", "Separator", PaletteKeyKind.Operator),
        Key("x", "x", "The variable x", PaletteKeyKind.Symbol),
        Key("y", "y", "The variable y", PaletteKeyKind.Symbol),
    ]);

    /// <summary>The three pages, in toggle order.</summary>
    public static IReadOnlyList<PaletteGroup> Pages { get; } = [Main, Second, Constants];

    private static PaletteKey Key(string label, string insert, string tooltip, PaletteKeyKind kind)
        => new(label, insert, tooltip, kind);

    private static PaletteKey Digit(string d)
        => new(d, d, $"Type {d}", PaletteKeyKind.Digit);

    /// <summary>A named function: brackets the selection, or a placeholder ready to be typed over.</summary>
    private static PaletteKey Fn(string label, string name, string tooltip)
        => new(label, name + "(", tooltip, PaletteKeyKind.Function)
        { InsertKind = KeyInsert.Wrapping, Close = ")" };

    /// <summary>A bracketing key whose opening text is not simply a function name.</summary>
    private static PaletteKey Wrap(string label, string open, string close, string tooltip, PaletteKeyKind kind)
        => new(label, open, tooltip, kind) { InsertKind = KeyInsert.Wrapping, Close = close };

    /// <summary>An operator that follows an operand, and does nothing when there isn't one.</summary>
    private static PaletteKey Post(string label, string insert, string tooltip, PaletteKeyKind kind)
        => new(label, insert, tooltip, kind) { InsertKind = KeyInsert.Postfix };

    /// <summary>A key the view acts on rather than inserting; its id travels in its own slot.</summary>
    private static PaletteKey Cmd(string label, string id, string tooltip)
        => new(label, string.Empty, tooltip, PaletteKeyKind.Action) { CommandId = id };
}
