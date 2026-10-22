using Nexaflow.Visuals.Common.Localization;

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
/// <para>
/// A page is built each time it is read rather than held. Its tooltips are in the reader's language,
/// and a language change restarts the window, not the process, so a page fixed once for the process
/// would go on speaking the language it was first built in. Building one is a few dozen small records,
/// paid on a page switch, never per frame.
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
    public static PaletteGroup Main => new("calc.main", "Main",
    [
        Cmd("2nd", SecondPageId, Str.Get("Solver.Calc.SecondPage")),
        Cmd("π e", ConstPageId, Str.Get("Solver.Calc.ConstPage")),
        Key("(", "(", Str.Get("Solver.Calc.OpenBracket"), PaletteKeyKind.Operator),
        Key(")", ")", Str.Get("Solver.Calc.CloseBracket"), PaletteKeyKind.Operator),
        Cmd("⌫", BackspaceId, Str.Get("Solver.Calc.Backspace")),
        Cmd("C", ClearId, Str.Get("Solver.Calc.Clear")),
        Key("÷", " / ", Str.Get("Solver.Calc.Divide"), PaletteKeyKind.Operator),
        Key("×", " * ", Str.Get("Solver.Calc.Multiply"), PaletteKeyKind.Operator),

        Fn("sin", "sin", Str.Get("Solver.Calc.Sine")),
        Fn("cos", "cos", Str.Get("Solver.Calc.Cosine")),
        Fn("tan", "tan", Str.Get("Solver.Calc.Tangent")),
        Digit("7"), Digit("8"), Digit("9"),
        Key("−", " - ", Str.Get("Solver.Calc.Subtract"), PaletteKeyKind.Operator),
        Post("xʸ", "^", Str.Get("Solver.Calc.Power"), PaletteKeyKind.Operator),

        Fn("ln", "ln", Str.Get("Solver.Calc.NaturalLog")),
        Fn("log", "log", Str.Get("Solver.Calc.Log")),
        Wrap("eˣ", "e^(", ")", Str.Get("Solver.Calc.Exp"), PaletteKeyKind.Function),
        Digit("4"), Digit("5"), Digit("6"),
        Key("+", " + ", Str.Get("Solver.Calc.Add"), PaletteKeyKind.Operator),
        Fn("√", "sqrt", Str.Get("Solver.Calc.SquareRoot")),

        Key("π", "pi", "3.14159…", PaletteKeyKind.Constant),
        Key("e", "e", "2.71828…", PaletteKeyKind.Constant),
        Post("n!", "!", Str.Get("Solver.Calc.Factorial"), PaletteKeyKind.Function),
        Digit("1"), Digit("2"), Digit("3"),
        Post("%", "%", Str.Get("Solver.Calc.Remainder"), PaletteKeyKind.Operator),
        Post("x²", "^2", Str.Get("Solver.Calc.Square"), PaletteKeyKind.Operator),

        Cmd("DEG", AngleToggleId, Str.Get("Solver.Calc.AngleToggle")),
        Fn("|x|", "abs", Str.Get("Solver.Calc.Abs")),
        Fn("⌊x⌋", "floor", Str.Get("Solver.Calc.Floor")),
        Digit("0"), Digit("."),
        Key(",", ", ", Str.Get("Solver.Calc.ListSeparator"), PaletteKeyKind.Operator),
        Variable("x"),
        Variable("y"),
    ]);

    /// <summary>Behind the <c>2nd</c> key: inverses and hyperbolics.</summary>
    public static PaletteGroup Second => new("calc.second.page", "2nd",
    [
        Cmd("2nd", SecondPageId, Str.Get("Solver.Calc.MainPage")),
        Cmd("π e", ConstPageId, Str.Get("Solver.Calc.ConstPage")),
        Key("(", "(", Str.Get("Solver.Calc.OpenBracket"), PaletteKeyKind.Operator),
        Key(")", ")", Str.Get("Solver.Calc.CloseBracket"), PaletteKeyKind.Operator),
        Cmd("⌫", BackspaceId, Str.Get("Solver.Calc.Backspace")),
        Cmd("C", ClearId, Str.Get("Solver.Calc.Clear")),
        Key("÷", " / ", Str.Get("Solver.Calc.Divide"), PaletteKeyKind.Operator),
        Key("×", " * ", Str.Get("Solver.Calc.Multiply"), PaletteKeyKind.Operator),

        Fn("sin⁻¹", "arcsin", Str.Get("Solver.Calc.ArcSine")),
        Fn("cos⁻¹", "arccos", Str.Get("Solver.Calc.ArcCosine")),
        Fn("tan⁻¹", "arctan", Str.Get("Solver.Calc.ArcTangent")),
        Digit("7"), Digit("8"), Digit("9"),
        Key("−", " - ", Str.Get("Solver.Calc.Subtract"), PaletteKeyKind.Operator),
        Wrap("ˣ√", "^(1/", ")", Str.Get("Solver.Calc.NthRoot"), PaletteKeyKind.Operator),

        Fn("sinh", "sinh", Str.Get("Solver.Calc.HyperbolicSine")),
        Fn("cosh", "cosh", Str.Get("Solver.Calc.HyperbolicCosine")),
        Fn("tanh", "tanh", Str.Get("Solver.Calc.HyperbolicTangent")),
        Digit("4"), Digit("5"), Digit("6"),
        Key("+", " + ", Str.Get("Solver.Calc.Add"), PaletteKeyKind.Operator),
        Post("∛", "^(1/3)", Str.Get("Solver.Calc.CubeRoot"), PaletteKeyKind.Operator),

        Fn("sec", "sec", Str.Get("Solver.Calc.Secant")),
        Fn("csc", "csc", Str.Get("Solver.Calc.Cosecant")),
        Fn("cot", "cot", Str.Get("Solver.Calc.Cotangent")),
        Digit("1"), Digit("2"), Digit("3"),
        Key("mod", " mod ", Str.Get("Solver.Calc.Modulo"), PaletteKeyKind.Operator),
        Post("x³", "^3", Str.Get("Solver.Calc.Cube"), PaletteKeyKind.Operator),

        Cmd("DEG", AngleToggleId, Str.Get("Solver.Calc.AngleToggle")),
        Fn("⌈x⌉", "ceil", Str.Get("Solver.Calc.Ceiling")),
        Fn("sgn", "signum", Str.Get("Solver.Calc.Sign")),
        Digit("0"), Digit("."),
        Key(",", ", ", Str.Get("Solver.Calc.Separator"), PaletteKeyKind.Operator),
        Variable("a"),
        Variable("n"),
    ]);

    /// <summary>Behind the <c>π e</c> key: the constants worth not having to look up.</summary>
    public static PaletteGroup Constants => new("calc.const.page", "Constants",
    [
        Cmd("2nd", SecondPageId, Str.Get("Solver.Calc.SecondPage")),
        Cmd("π e", ConstPageId, Str.Get("Solver.Calc.MainPage")),
        Key("(", "(", Str.Get("Solver.Calc.OpenBracket"), PaletteKeyKind.Operator),
        Key(")", ")", Str.Get("Solver.Calc.CloseBracket"), PaletteKeyKind.Operator),
        Cmd("⌫", BackspaceId, Str.Get("Solver.Calc.Backspace")),
        Cmd("C", ClearId, Str.Get("Solver.Calc.Clear")),
        Key("÷", " / ", Str.Get("Solver.Calc.Divide"), PaletteKeyKind.Operator),
        Key("×", " * ", Str.Get("Solver.Calc.Multiply"), PaletteKeyKind.Operator),

        Key("π", "pi", Str.Get("Solver.Calc.Pi"), PaletteKeyKind.Constant),
        Key("τ", "(2 * pi)", Str.Get("Solver.Calc.Tau"), PaletteKeyKind.Constant),
        Key("e", "e", Str.Get("Solver.Calc.Euler"), PaletteKeyKind.Constant),
        Digit("7"), Digit("8"), Digit("9"),
        Key("−", " - ", Str.Get("Solver.Calc.Subtract"), PaletteKeyKind.Operator),
        Post("xʸ", "^", Str.Get("Solver.Calc.Power"), PaletteKeyKind.Operator),

        Key("φ", "((1 + sqrt(5)) / 2)", Str.Get("Solver.Calc.GoldenRatio"), PaletteKeyKind.Constant),
        Key("√2", "sqrt(2)", "1.41421…", PaletteKeyKind.Constant),
        Key("√3", "sqrt(3)", "1.73205…", PaletteKeyKind.Constant),
        Digit("4"), Digit("5"), Digit("6"),
        Key("+", " + ", Str.Get("Solver.Calc.Add"), PaletteKeyKind.Operator),
        Fn("√", "sqrt", Str.Get("Solver.Calc.SquareRoot")),

        Key("i", "i", Str.Get("Solver.Calc.ImaginaryUnit"), PaletteKeyKind.Constant),
        Key("∞", "+oo", Str.Get("Solver.Calc.Infinity"), PaletteKeyKind.Constant),
        Key("½", "(1/2)", Str.Get("Solver.Calc.Half"), PaletteKeyKind.Constant),
        Digit("1"), Digit("2"), Digit("3"),
        Post("%", "%", Str.Get("Solver.Calc.Remainder"), PaletteKeyKind.Operator),
        Post("x²", "^2", Str.Get("Solver.Calc.Square"), PaletteKeyKind.Operator),

        Cmd("DEG", AngleToggleId, Str.Get("Solver.Calc.AngleToggle")),
        Key("⅓", "(1/3)", Str.Get("Solver.Calc.Third"), PaletteKeyKind.Constant),
        Key("¼", "(1/4)", Str.Get("Solver.Calc.Quarter"), PaletteKeyKind.Constant),
        Digit("0"), Digit("."),
        Key(",", ", ", Str.Get("Solver.Calc.Separator"), PaletteKeyKind.Operator),
        Variable("x"),
        Variable("y"),
    ]);

    /// <summary>The three pages, in toggle order.</summary>
    public static IReadOnlyList<PaletteGroup> Pages => [Main, Second, Constants];

    private static PaletteKey Key(string label, string insert, string tooltip, PaletteKeyKind kind)
        => new(label, insert, tooltip, kind);

    private static PaletteKey Digit(string d)
        => new(d, d, Str.Format("Solver.Calc.DigitFormat", d), PaletteKeyKind.Digit);

    /// <summary>A single-letter variable, typed as itself.</summary>
    private static PaletteKey Variable(string name)
        => new(name, name, Str.Format("Solver.Calc.VariableFormat", name), PaletteKeyKind.Symbol);

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
