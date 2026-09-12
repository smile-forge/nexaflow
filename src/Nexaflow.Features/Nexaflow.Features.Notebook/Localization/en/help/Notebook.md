# Notebook

Opens a Jupyter notebook and shows its cells in order as a page you can read.

---

## Opening a notebook
- Double-click a `.ipynb` in the [File System](help:FileSystem) page, or pick **As Notebook** from the file's actions. [The tab](locate:TabItem_Notebook) takes the file's name.
- A notebook inside an archive or a disk image opens the same way, with nothing to extract first.
- [The breadcrumb](locate:Chrome_BreadcrumbBar) names the folder the notebook sits in; one opened from an archive points back at the archive.
- This is a reader. Nothing runs, no kernel starts, and the page never writes to the file.
- For a `.md` file use the [Markdown](help:Markdown) tab; for flat text, the [Text viewer](help:Text).

## Reading the cells
- Markdown cells are rendered: headings, lists, tables, block quotes and links.
- `$…$` inside a sentence and `$$…$$` on a line of its own are typeset as formulae.
- Mermaid diagrams are drawn. A wide one keeps its size and scrolls sideways.
- A code cell carries its execution label in the gutter: `In [3]` for the cell that ran third, `In [ ]` for one that never ran. Long lines scroll sideways.
- Select text in a markdown cell and copy it: plain text, HTML and the original markdown all reach the clipboard. Copy with nothing selected to take the whole cell.
- Stored outputs stay in the file rather than being replayed down the page. Ask the assistant to read them.
- Code is coloured for the kernel the notebook records. Python, Ruby, JavaScript, TypeScript and C# each have their own colouring; an unfamiliar kernel is read as Python.
- Raw cells are shown verbatim, with no colouring and no execution label.

## The outline
- A panel beside the cells lists what each code cell declares, grouped under the cell's number.
- Each class is listed with its members, and every top-level function by signature.
- The text is selectable, so a signature copies into your own code.
- A notebook that declares nothing has no outline column.

## Searching
- Type `?` and a word in the AI bar to search the whole notebook, markdown and code alike. [Show me](locate:AiInputBox,Notebook_SearchMatchCount,Notebook_SearchNext)
- A hit is a cell, so the count is a count of cells, and each one is marked down its left edge. In a code cell the matched words are highlighted as well.
- Cells that do not match stay where they are; nothing is hidden.
- Step through the hits with the arrows on the chip, which wrap round at either end, or [clear the search](locate:AiInputBox,Notebook_SearchClear) to leave the page as it was.
- A plain word matches that whole word, `fig*` a prefix and `*fig*` a substring. `/pattern/` runs a regular expression, with a `c` flag for case sensitivity. A pattern that will not compile is reported back rather than answered with an empty result.

## With the assistant
- Switch on page context in the AI bar and the assistant knows which notebook is open: its kernel, how many code and markdown cells it holds, and the opening line of the first fifteen. [Show me](locate:AiBar_ContextToggle) It waits until the file has parsed, so it never describes an empty stub.
- It can read the whole notebook, every cell in order with its full source, or one cell by the number the search reports.
- It reads stored outputs: printed text, results, and an error's message and traceback. Images are named by type rather than read.
- It can search and filter, and the cells it kept are the ones marked on the page.
- Nothing is executed and the file is never changed.
