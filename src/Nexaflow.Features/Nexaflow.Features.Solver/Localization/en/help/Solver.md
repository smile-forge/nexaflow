# Solver

Works out a maths problem you type in, and keeps each answer below it.

---

## Typing the problem
- Pick the editor on the rail: Calc for arithmetic, Latex for a formula, Text for a description. Each keeps its own text for as long as the tab is open. [Show me](locate:Solver_Mode_Calc,Solver_Mode_Latex,Solver_Mode_Text)
- Calc takes written-out maths — `2+2*3`, `sin(45)`, `sqrt(2)`, `log(2, 8)`, `x^2-4`. [Show me](locate:Solver_CalcInput)
- Latex holds one formula and typesets it as you type. You do not type the `$$` — it is already there. [Show me](locate:Solver_LatexInput)
- Text is a markdown document. The maths read out of it is the first `$$…$$` or `$…$` formula in it; wording on its own is only passed to the Solve chips.
- On Latex and Text, `</>` shows the characters instead of the typeset form, and `✕` empties the editor. [Show me](locate:Solver_SourceToggle,Solver_ClearDefinition)

## The palette
- The keypad types into Calc and Text. `2nd` swaps in the inverse and hyperbolic functions, `π e` swaps in the constants, and pressing the same key again goes back.
- A function key such as `sin` or `√` wraps what you have selected. With nothing selected it puts `x` between the brackets and leaves it selected, so what you type next replaces it.
- `DEG` switches between degrees and radians. `C` empties the definition, `⌫` deletes the character before the caret.
- Latex gets the symbol navigator instead of the keypad: categories, then groups, then the symbols themselves, with the path underneath as the way back to any level. Symbols you have used collect beside it and survive a restart.
- Fold the palette away with the chevron between it and the editor. [Show me](locate:Solver_TogglePalette)

## Chips
- A chip appears for each thing a solver recognises in what you have typed. Press one and its answer is appended below. A half-written formula offers only the Solve chips until it is finished.
- `=` works out the value, keeping an exact form alongside the rounded decimal.
- `simplify` collects like terms, `steps` shows each identity used to do it, and `factor` writes a one-variable expression as a product of factors — or a whole number as its primes.
- Calculus gives one `d/d` chip and one `∫ d` chip per variable in the expression, each labelled with that variable.
- A list of numbers separated by commas, spaces or new lines offers `stats`, `sum`, `avg`, `median` and `σ`.
- `Solve` and `Solve by steps` hand the whole definition to the AI. They need a model assigned to Problem Solving or Analysis in Manage AI.

## Answers
- Each answer is a cell carrying the chip it came from, the definition it was asked, and the result as markdown with the maths typeset. [Show me](locate:Solver_Results,Solver_ResultBody)
- The buttons on a cell copy the answer, use it as the next definition, or remove it.
- Answers are not saved. Closing the tab discards them.

## Settings
- Options → Solver sets Start In, Angles, Decimal Places and Show Palette. Angles and the palette can also be changed on the page, for that tab.

## Asking the assistant
- The assistant can read the mode, the definition, the chips on offer and every answer so far.
- It can also replace the definition, which it asks you to approve first, and run a chip that is currently on offer.
