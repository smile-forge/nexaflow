using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Latex;

/// <summary>
/// The editor told that what it holds is one formula — how the Solver's Latex tab behaves.
///
/// <para>
/// The experience being pinned here is Word's, for maths: you type into the thing you are looking at,
/// it sets itself as you go, and nothing ever asks you to switch views to fix it. Space and Enter are
/// the moment it reassesses — settle what has been written, typeset what can be read, and leave what
/// cannot as the characters actually typed with a wave under them, so writing can carry on straight
/// over the trouble rather than stopping at it.
/// </para>
/// <para>
/// The <c>$$</c> never appears in any of this. It is the editor's own way of asking the markdown
/// renderer for maths, put on to typeset and taken off again, so the host holds a formula and the user
/// types a formula — which is why every assertion below reads plain LaTeX.
/// </para>
///
/// Shows a real (off-screen) window, because the editor builds its document during a render pass.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]   // spins an off-screen Window; concurrent WPF layout and focus make it flaky
[CoversNode("solver-latex-fence")]
public class SingleFormulaEditorTests
{
    // ── The fence is the editor's, not the host's ───────────────────────────

    [TestMethod]
    public void TheHostHandsItLatexAndTypesetsWithoutEverSeeingAFence()
    {
        RunInFormula(@"\frac{x^2}{2}", (editor, _) =>
        {
            Assert.IsNotNull(FormulaIn(editor), "the block typeset as maths");
            Assert.AreEqual(@"\frac{x^2}{2}", editor.Markdown,
                "and what comes back out is the formula — a fence here would be the editor's own "
                + "punctuation leaking into the host's text");
        });
    }

    [TestMethod]
    public void AnEmptyFormulaIsStillAFormula()
    {
        // Where the caret goes and the first character is typed. Rendered as an empty block instead,
        // the very first keystroke of a new formula would land in prose.
        RunInFormula(string.Empty, (editor, _) =>
            Assert.IsNotNull(FormulaIn(editor),
                "an empty formula is the one you are about to write, not an empty paragraph"));
    }

    [TestMethod]
    public void ManyLinesAreStillOneFormula()
    {
        // A blank line is what separates markdown blocks, and a formula written over several lines
        // would otherwise become a formula and a stray paragraph of LaTeX.
        RunInFormula("x +\n\ny", (editor, _) =>
        {
            Assert.IsNotNull(FormulaIn(editor));
            Assert.AreEqual("x +\n\ny", editor.Markdown, "it never split");
        });
    }

    // ── Space and Enter reassess ────────────────────────────────────────────

    [TestMethod]
    public void SpaceSettlesACommandAndTypesetsIt()
    {
        RunInFormula("x + ", (editor, rtb) =>
        {
            var formula = Focused(editor);
            MarkdownEditorHarness.Type(rtb, @"\alpha");

            // Still being written: shown as the characters typed, because flickering through six
            // failed parses on the way to \alpha tells the reader nothing.
            Assert.AreEqual(@"x + \alpha", formula.Latex);
            Assert.AreEqual((4, 6), formula.ShownAsWritten, "the command is standing as its own letters");

            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Space);

            Assert.AreEqual(@"x + \alpha ", formula.Latex, "the space is kept — LaTeX needs it to know "
                + "where the command's name stopped");
            Assert.IsNull(formula.ShownAsWritten, "and now the whole of it is set as maths");
        });
    }

    [TestMethod]
    public void SpaceSettlesWithoutLeavingAnythingBehind()
    {
        // A space is not typeset, so one left in the source is a character the reader cannot see and
        // cannot find — it looks like the key did nothing, and then backspace has to be pressed once
        // per invisible space before anything moves. Space asks for a fresh reading; that is all.
        RunInFormula("x", (editor, rtb) =>
        {
            var formula = Focused(editor);
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Space);
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Space);
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Enter);

            Assert.AreEqual("x", formula.Latex, "three settling keys, nothing added");

            // Except where LaTeX needs it: the space is what says where a command's name stopped, and
            // without it \alpha followed by x would read as the unknown command \alphax.
            MarkdownEditorHarness.Type(rtb, @"\alpha");
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Space);
            Assert.AreEqual(@"x\alpha ", formula.Latex);

            MarkdownEditorHarness.Type(rtb, "y");
            Assert.AreEqual(@"x\alpha y", formula.Latex, "and the command kept its own name");
        });
    }

    [TestMethod]
    public void WhatCannotBeReadStaysVisibleUnderAWaveAndTypingCarriesOn()
    {
        RunInFormula("x + ", (editor, rtb) =>
        {
            var formula = Focused(editor);
            MarkdownEditorHarness.Type(rtb, @"\nosuchcommand");
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Space);

            // Reassessed and found wanting — which costs it nothing. The part that reads still reads.
            Assert.AreEqual(@"x + \nosuchcommand ", formula.Latex);
            Assert.IsNotNull(formula.Layout, "the formula did not vanish for being wrong");
            Assert.AreNotEqual(0, formula.Diagnostics.Count,
                "and it says which stretch could not be read, which is what wears the red wave");

            // The whole point of leaving it there: you keep writing.
            MarkdownEditorHarness.Type(rtb, " + y");
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Space);
            StringAssert.EndsWith(formula.Latex, "+ y");
        });
    }

    [TestMethod]
    public void EnterIsALineInTheExpressionAndNeverASecondFormula()
    {
        RunInFormula("x", (editor, rtb) =>
        {
            var formula = Focused(editor);
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Enter);

            // You are inside one expression, not between two paragraphs, so there is nowhere to split to.
            Assert.IsNotNull(FormulaIn(editor), "still one formula");
            Assert.IsFalse(editor.Markdown.Contains("$$"), "and still no fence anywhere in the host's text");
            Assert.AreEqual(formula, Focused(editor), "and the caret never left it");
        });
    }

    // ── Editing what is already there ───────────────────────────────────────

    [TestMethod]
    public void TypingAfterAnExponentGoesIntoIt()
    {
        // LaTeX lets a one-token argument go unbraced, so x^2 is x to the 2 — and typing a 3 meaning
        // twenty-three writes x^23, which says x squared followed by a 3. The keystroke has to mean
        // the obvious thing, so the argument is re-braced around it.
        RunInFormula("x^2", (editor, rtb) =>
        {
            var formula = Focused(editor);
            MarkdownEditorHarness.Type(rtb, "3");

            Assert.AreEqual("x^{23}", formula.Latex, "the 3 joined the exponent instead of escaping it");
            Assert.AreEqual(5, formula.Caret, "and the caret is after it, still inside the exponent");

            MarkdownEditorHarness.Type(rtb, "4");
            Assert.AreEqual("x^{234}", formula.Latex, "and it keeps going, because the braces are there now");
        });
    }

    [TestMethod]
    public void TypingAfterAnOrdinaryNumberJustFollowsIt()
    {
        // The guard against re-bracing everything: "1" here is a term, not a construct's argument, so
        // a 2 after it is twelve and nothing needs saying about it.
        RunInFormula("a + 1", (editor, rtb) =>
        {
            var formula = Focused(editor);
            MarkdownEditorHarness.Type(rtb, "2");

            Assert.AreEqual("a + 12", formula.Latex);
        });
    }

    [TestMethod]
    public void ClickingSomewhereDoesNotSelectWhatIsThere()
    {
        // A pointer moves a pixel or two under any real hand. Treated as a drag, that selected the
        // piece under the click — so the next key replaced it, and the formula could not be edited at
        // all: every keystroke overwrote the last.
        RunInFormula("a + 1", (editor, _) =>
        {
            var formula = Focused(editor);
            var at = new System.Windows.Point(formula.ActualWidth - 2, formula.ActualHeight / 2);

            formula.BeginPointerSelect(at);
            formula.ExtendPointerSelect(new System.Windows.Point(at.X + 1, at.Y));   // a hand, not a drag
            formula.EndPointerSelect();

            Assert.AreEqual(0, formula.SelectionLength, "a click selects nothing, so typing follows it");
        });
    }

    // ── Holes waiting to be filled ──────────────────────────────────────────

    [TestMethod]
    public void AnEmptyArgumentBecomesAHoleInTheTreeAndNotInTheSource()
    {
        // The whole design, in one assertion pair. \frac{}{} sets as a bar with two invisible sides,
        // so the typesetter puts a symbol where each missing argument would have gone — laid out
        // exactly where an x would have been had one been written. The source keeps saying {}, because
        // that is what the reader wrote and what has to be saved, copied and solved.
        RunInFormula(@"\frac{}{}", (editor, _) =>
        {
            var formula = Focused(editor);

            Assert.AreEqual(@"\frac{}{}", formula.Latex, "nothing was written that the reader did not");
            Assert.IsNotNull(formula.Layout, "and it draws");
            Assert.AreEqual(2, formula.Layout!.Tree.Placeholders.Count,
                "as a fraction with two holes in it — one symbol each, like any other symbol");
        });
    }

    [TestMethod]
    public void AFormulaWithHolesLeftInItIsNotFinished()
    {
        // It renders perfectly and it does not mean anything yet, and both of those are true at once.
        // The wave says which part is still missing, and nothing downstream will try to solve it.
        RunInFormula(@"\frac{}{2}", (editor, _) =>
        {
            var formula = Focused(editor);

            Assert.IsNotNull(formula.Layout, "drawn");
            Assert.AreEqual(1, formula.Diagnostics.Count, "and reported, once per hole");
            Assert.IsFalse(LatexSyntax.IsWellFormed(formula.Latex),
                "so a solver is never handed a formula with a hole in it");
        });
    }

    [TestMethod]
    public void TabWalksTheHolesAndWrapsRound()
    {
        RunInFormula(@"\frac{}{}", (editor, rtb) =>
        {
            var formula = Focused(editor);

            // The caret lands in each hole rather than over it: a hole covers nothing, so there is
            // nothing to select — what gets typed goes inside the braces.
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Tab);
            var first = formula.Caret;

            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Tab);
            Assert.AreNotEqual(first, formula.Caret, "the next one");

            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Tab);
            Assert.AreEqual(first, formula.Caret,
                "and round to the first — filling in a construct is a loop until it is finished");
        });
    }

    [TestMethod]
    public void TypingFillsTheHoleTabLandedOn()
    {
        // Why Tab leaves the hole picked out: the next keystroke is the answer, with nothing to delete
        // first and nowhere to aim.
        RunInFormula(@"\frac{}{}", (editor, rtb) =>
        {
            var formula = Focused(editor);
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Tab);
            MarkdownEditorHarness.Type(rtb, "a");

            StringAssert.StartsWith(formula.Latex, @"\frac{a}", "the numerator was written into");
            Assert.AreEqual(1, formula.Layout!.Tree.Placeholders.Count, "and one hole is left");
        });
    }

    [TestMethod]
    public void APaletteKeyTakesWhatIsSelectedInsteadOfReplacingIt()
    {
        // Selecting 3+7 and pressing √ means the root of 3+7. It replaced it instead, because a key
        // with a hole in its template is inserted like any other text — and every structural key on
        // the palette is one of those, so the whole palette was unusable over a selection.
        RunInFormula("3+7", (editor, _) =>
        {
            var formula = Focused(editor);
            formula.SelectAll();

            editor.InsertLatexAtCaret(@"\sqrt{}", caretBack: 1);
            Assert.AreEqual(@"\sqrt{3+7}", formula.Latex);
        });
    }

    [TestMethod]
    public void WhatIsSelectedGoesInTheSlotTheKeyWouldHaveTypedInto()
    {
        // Which hole is not a new thing to know: a key already says where it expects to be typed next,
        // and that is the same place. A fraction over a selected 3+7 is a fraction of it, in the
        // numerator, and its denominator is left as a box with the box selected.
        RunInFormula("3+7", (editor, _) =>
        {
            var formula = Focused(editor);
            formula.SelectAll();

            editor.InsertLatexAtCaret(@"\frac{}{}", caretBack: 3);

            Assert.AreEqual(@"\frac{3+7}{}", formula.Latex);
            Assert.AreEqual(1, formula.Layout!.Tree.Placeholders.Count,
                "and the denominator is left as a hole, drawn and waiting");
            Assert.AreEqual(formula.Layout.Tree.Placeholders[0].SourceStart, formula.Caret,
                "with the caret already in it, ready to be typed into");
        });
    }

    [TestMethod]
    public void BackspaceDeletesACharacterRatherThanHidingTheRunItWasIn()
    {
        // Un-rendering is for a symbol that took more source to write than it takes to draw — six
        // characters of \alpha for one α — so backspace puts the reader in front of what they wrote.
        // A run of ordinary symbols is not one of those. Treating it as one meant backspace at the end
        // of a denominator hid the whole denominator, which then read as an empty argument and drew a
        // hole in front of what had been typed.
        RunInFormula(@"\frac{4}{7+5}", (editor, rtb) =>
        {
            var formula = Focused(editor);
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Left);   // in past the closing brace
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Back);

            Assert.AreEqual(@"\frac{4}{7+}", formula.Latex, "the 5 went, and nothing else changed");
        });
    }

    [TestMethod]
    public void ArrowingOffTheEndOfTheOnlyFormulaStaysPut()
    {
        // There is nowhere to step out to when the formula is the whole editor. Handing the caret back
        // to the document anyway let the RichTextBox take it somewhere of its own choosing — the start
        // of the line, then off the right-hand edge, then down a line — for a key that should do
        // nothing at all.
        RunInFormula("x+1", (editor, rtb) =>
        {
            var formula = Focused(editor);
            var end = formula.Latex.Length;

            for (var i = 0; i < 3; i++) MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Right);

            Assert.AreSame(formula, editor.FocusedFormula, "the formula still has the caret");
            Assert.AreEqual(end, formula.Caret, "and it is still at the end, where it ran out of formula");
        });
    }

    [TestMethod]
    public void ASelectionSweepingAcrossAHoleShowsItPickedUp()
    {
        // A hole covers no characters, so a wash driven by what a piece covers skips it — and a
        // selection over a half-written fraction highlighted everything except the part still missing,
        // which is the piece the reader most needs to see they have got hold of.
        RunInFormula(@"\frac{}{7}", (editor, _) =>
        {
            var formula = Focused(editor);
            var tree = formula.Layout!.Tree;
            var hole = tree.Placeholders.Single();

            var washed = tree.RangeRects(0, formula.Latex.Length);
            Assert.IsTrue(washed.Any(r => r.Contains(hole.Bounds.TopLeft) || r.IntersectsWith(hole.Bounds)),
                "the hole is washed along with everything else");
        });
    }

    // ── Pasting ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void PastingSettlesAndTypesetsJustAsSpaceWould()
    {
        RunInFormula(string.Empty, (editor, rtb) =>
        {
            var formula = Focused(editor);

            // Mid-command, so this also proves the paste is not read as more of the command being
            // written: \al + pha would otherwise quietly become \alpha.
            MarkdownEditorHarness.Type(rtb, @"\al");
            Assert.IsTrue(editor.PasteIntoFormula(@"\beta + 1"), "a formula holds the caret, so it takes it");

            Assert.AreEqual(@"\al\beta + 1", formula.Latex);
            Assert.AreEqual(@"\al\beta + 1", formula.Layout?.Tree.Latex,
                "settled and set on arrival — pasting reassesses exactly as space does");
        });
    }

    [TestMethod]
    public void PastingDoesNotBringTheNewlineWithIt()
    {
        // A copy almost always carries a trailing newline, and inside one expression a newline means a
        // space. Left on, that is a character the reader cannot see sitting at the end of their
        // formula — so the first backspace deletes it and appears to do nothing at all.
        RunInFormula(string.Empty, (editor, _) =>
        {
            var formula = Focused(editor);
            Assert.IsTrue(editor.PasteIntoFormula("\\frac{a}{b} + \\frac{c}{d}\r\n"));

            Assert.AreEqual(@"\frac{a}{b} + \frac{c}{d}", formula.Latex);
            Assert.AreEqual(formula.Latex.Length, formula.Caret, "with the caret at the end of it");
        });
    }

    [TestMethod]
    public void PasteIsCleanedWhicheverRouteItTakes()
    {
        // Cleaning used to happen only where a formula already held the caret. A paste arriving a
        // moment earlier — before anything had adopted one — went in through the other door with its
        // delimiters still attached, and the reader got a formula with dollar signs in it.
        Assert.AreEqual("x^2, x_i, x^{2n}, x_{i,j}",
                        InlineMarkdownEditor.AsFormula("$x^2, x_i, x^{2n}, x_{i,j}$"));

        Assert.AreEqual(@"\sqrt{x^2+1}", InlineMarkdownEditor.AsFormula("  \\[\\sqrt{x^2+1}\\]\r\n"));
        Assert.AreEqual("a + b", InlineMarkdownEditor.AsFormula("a\r\n+ b\r\n"), "one expression, trimmed");
    }

    [TestMethod]
    public void TextDraggedInFromAnotherWindowLandsAsAFormula()
    {
        // Dropping was doing nothing at all. The editor suppresses the RichTextBox's own text drop (it
        // rejects files and images, and would insert straight into the rendered document) and offered
        // the drop to the host instead — so on any surface whose host does not handle drops, which is
        // every one but the scratchpad, the drag simply ended. A drop is a paste that names where it
        // goes, so it is cleaned the same way and lands the same way.
        RunInFormula("x + ", (editor, _) =>
        {
            var dragged = new System.Windows.DataObject();
            dragged.SetData(System.Windows.DataFormats.UnicodeText, "```\r\n\\alpha\r\n```");

            editor.DropContent(dragged, new System.Windows.Point(4, 4));
            MarkdownEditorHarness.Pump();

            Assert.AreEqual(@"x + \alpha", editor.Markdown,
                "the fence came off on the way in, exactly as it does for a paste");
            Assert.IsNotNull(editor.FocusedFormula,
                "and the formula holds the caret, so the next thing typed carries on from the drop");
        });
    }

    [TestMethod]
    public void AHostThatClaimsADropKeepsIt()
    {
        // The other half of the contract: a host that handles images and files says so, and the editor
        // must not then insert the text as well.
        RunInFormula("x", (editor, _) =>
        {
            editor.ContentDropped = (_, _) => true;

            var dragged = new System.Windows.DataObject();
            dragged.SetData(System.Windows.DataFormats.UnicodeText, "y");

            editor.DropContent(dragged, new System.Windows.Point(4, 4));
            MarkdownEditorHarness.Pump();

            Assert.AreEqual("x", editor.Markdown, "the host took it");
        });
    }

    [TestMethod]
    public void PastingFromAPageThatShowedTheFormulaAsCodeStripsTheFence()
    {
        // Reported from the app, and the whole of why it was visible: a backtick is an opening quote in
        // TeX, so a fence left on arrives as three quotation marks at each end of the formula.
        //
        // It gets there because a browser puts HTML on the clipboard as well as text, and a formula
        // shown on the page as code converts to a fenced block. A fence is the same kind of wrapper as
        // $$ — it says what the text is and is not part of it — so it comes off with the rest.
        Assert.AreEqual(
            @"S (\omega)=\frac{\alpha g^2}{\omega^5}",
            InlineMarkdownEditor.AsFormula("```\r\n\\begin{equation} S (\\omega)=\\frac{\\alpha g^2}{\\omega^5} \\end{equation}\r\n```"),
            "the fence and the environment inside it both come off");

        Assert.AreEqual("x+1", InlineMarkdownEditor.AsFormula("```latex\nx+1\n```"),
            "an info string is a language name, not code");

        // Backticks that were actually typed stay: only a fence opening the first line and closing the
        // last is a wrapper, and a formula is not code so nothing else here should touch them.
        Assert.AreEqual("a ` b", InlineMarkdownEditor.AsFormula("a ` b"));
        Assert.AreEqual("``` x+1 ```", InlineMarkdownEditor.AsFormula("``` x+1 ```"),
            "all on one line is not a fenced block");
    }

    [TestMethod]
    public void PastingStripsWhateverSaidThisIsMaths()
    {
        // LaTeX copied from a paper, a chat or another editor comes wrapped in that place's way of
        // saying "maths follows". Pasting into a formula, the surface has already said it — keeping the
        // wrapper hands the parser commands it never heard of and the reader a red wave under their own
        // formula.
        (string Pasted, string Expected)[] cases =
        [
            (@"\[\sqrt{x^2+1}\]",                                  @"\sqrt{x^2+1}"),
            ("$$x+1$$",                                            "x+1"),
            ("$x+1$",                                              "x+1"),
            (@"\(x+1\)",                                           "x+1"),
            (@"\begin{equation}x+1\end{equation}",                 "x+1"),
            (@"\begin{equation*}x+1\end{equation*}",               "x+1"),
            (@"\begin{displaymath}x+1\end{displaymath}",           "x+1"),
            (@"\begin{math}x+1\end{math}",                         "x+1"),
            (@"\[\begin{align}x+1\end{align}\]",                   "x+1"),   // they nest
            // …but an environment that IS the formula stays. Stripping this would take the matrix apart.
            (@"\begin{matrix}a&b\\c&d\end{matrix}",                @"\begin{matrix}a&b\\c&d\end{matrix}"),
        ];

        foreach (var (pasted, expected) in cases)
            RunInFormula(string.Empty, (editor, _) =>
            {
                var formula = Focused(editor);
                Assert.IsTrue(editor.PasteIntoFormula(pasted), pasted);
                Assert.AreEqual(expected, formula.Latex, pasted);
            });
    }

    // ── Source is asked for, never fallen into ──────────────────────────────

    [TestMethod]
    public void ClickingAndTypingNeverDropTheFormulaIntoSource()
    {
        // The bug that made the whole tab feel wrong: the editor shows one block as source — the one
        // the caret is in — so focusing it, clicking a number or arrowing about would each replace the
        // typeset maths with its own LaTeX. Rendered maths is the tab, so it has to survive all three.
        RunInFormula(@"\frac{a}{b}", (editor, rtb) =>
        {
            Focused(editor);
            MarkdownEditorHarness.Type(rtb, "+1");
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Space);
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Left);

            Assert.IsNotNull(FormulaIn(editor), "still typeset");
            Assert.IsFalse(editor.Markdown.Contains("$$"), "and no fence was ever exposed to the host");
        });
    }

    [TestMethod]
    public void AskingForSourceShowsTheLatexAndNotTheFence()
    {
        RunInFormula(@"\frac{a}{b}", (editor, rtb) =>
        {
            editor.EditAsSource = true;

            Assert.IsNull(FormulaIn(editor), "nothing is typeset while the source is being read");
            StringAssert.Contains(TextOf(rtb), @"\frac{a}{b}", "the characters written are on show");
            Assert.IsFalse(TextOf(rtb).Contains("$$"),
                "but not the fence — that is the editor's own way of asking for maths, not something "
                + "the reader wrote or should have to keep intact");

            editor.EditAsSource = false;
            Assert.IsNotNull(FormulaIn(editor), "and it typesets again on the way back");
        });
    }

    [TestMethod]
    public void TheSourceOnShowCanBeSelected()
    {
        // While the formula is typeset the document's selection is worthless — the formula is a single
        // indivisible position in it, so the only thing the document can say is "all of it", which
        // washes the whole line behind maths that is already showing what it has picked out. So the
        // editor clears it.
        //
        // Held open as source there is no formula to have an opinion, and the document's selection is
        // the only selection there is. Clearing it there took the source view's selection away as fast
        // as it could be made: you could not select, copy or replace a single character of it.
        RunInFormula(@"\frac{a}{b}", (editor, rtb) =>
        {
            editor.EditAsSource = true;

            var start = rtb.Document.ContentStart.GetPositionAtOffset(1) ?? rtb.Document.ContentStart;
            rtb.Selection.Select(start, rtb.Document.ContentEnd);
            Assert.IsFalse(rtb.Selection.IsEmpty, "the source can be swept over");
            StringAssert.Contains(rtb.Selection.Text, @"frac{a}{b}", "and what was swept is what is selected");
        });
    }

    [TestMethod]
    public void TheHiddenTabsEditorTakesTheSourceToggleWithoutBeingOnShow()
    {
        // Rendered-or-source is one preference across the tabs, so the toggle reaches the editor behind
        // the tab you are not looking at as well - which asks it to hold a block open while it has no
        // layout at all. It has to survive that quietly: the alternative is coming back to a tab that
        // is rendered when the one you left was not.
        UiThread.Run(() => MarkdownEditorHarness.Run(@"\frac{a}{b}", (editor, _) =>
        {
            editor.EditAsSource = true;
            editor.EditAsSource = false;

            Assert.AreEqual(@"\frac{a}{b}", editor.Markdown, "and it still holds what it was given");
        },
        e => { e.SingleFormula = true; e.Visibility = System.Windows.Visibility.Collapsed; }));
    }

    // ── One caret ───────────────────────────────────────────────────────────

    [TestMethod]
    public void OnlyOneCaretIsEverDrawn()
    {
        // Both surfaces know how to draw a caret, and the document's sits at the text position its
        // block occupies — right beside the formula's. Left visible, two carets blink at you and only
        // one of them is where the keys are going.
        RunInFormula("x", (editor, rtb) =>
        {
            // Focused at all is enough: a caret is what "focused and editable" looks like, so the
            // formula takes it the moment the editor has the keyboard rather than waiting to be asked.
            // Before that it waited for the first keystroke, which meant no caret until you typed.
            Assert.IsNotNull(editor.FocusedFormula, "the formula holds the caret because the editor is focused");
            Assert.AreEqual(Brushes.Transparent, rtb.CaretBrush,
                "and the document is not drawing a second one beside it");

            editor.SingleFormula = false;   // rebuilds the document, which takes the caret back
            Assert.IsNull(editor.FocusedFormula);
            Assert.AreNotEqual(Brushes.Transparent, rtb.CaretBrush,
                "the document draws it again once nothing else is");
        });
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="test"/> against an editor holding <paramref name="latex"/> as one formula.</summary>
    private static void RunInFormula(string latex, System.Action<InlineMarkdownEditor, RichTextBox> test) =>
        UiThread.Run(() => MarkdownEditorHarness.Run(latex, test, e => e.SingleFormula = true));

    /// <summary>The formula holding the caret, having handed it the caret if nothing had it.</summary>
    private static FormulaElement Focused(InlineMarkdownEditor editor)
    {
        Assert.IsTrue(editor.FocusFormulaAtCaret(), "there is a formula to type into");
        var formula = FormulaIn(editor);
        Assert.IsNotNull(formula);
        return formula;
    }

    /// <summary>Everything the document is showing as text.</summary>
    private static string TextOf(RichTextBox rtb) =>
        new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text;

    private static FormulaElement? FormulaIn(InlineMarkdownEditor editor)
    {
        var rtb = MarkdownEditorHarness.RichTextBoxOf(editor);
        return rtb.Document.Blocks
            .OfType<BlockUIContainer>()
            .Select(b => b.Child)
            .OfType<FormulaElement>()
            .FirstOrDefault();
    }
}
