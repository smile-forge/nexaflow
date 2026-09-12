# Tabular

Opens a delimited text file as a grid. Nothing you do here writes to the file.

---

## Opening a file

- Send a file here from the File System page with the **As Table** (▦) action.
- Comma, tab, semicolon and pipe separators are worked out for you, and quoted fields (`"` or `'`) stay whole.
- A file with no separator opens as fixed-width columns if it is space-aligned, otherwise as a single column.
- Leading `#` comment lines are skipped. Hover the ⓘ above the row numbers to see which.
- Check what was detected — separator, quoting, header row, column count — in the toolbar. [Show me](locate:Tabular_ShapeLabel)

## Reading the grid

- Select rows: click one, Ctrl-click to add or drop one, Shift-click for a range.
- The footer shows the file name, row count and encoding. `⟳ counting rows…` means the total is still settling.
- A file under 100 KB loads whole. Above that the grid holds 150 rows and re-reads from disk as you scroll, so a jump to the far end takes a moment.

## Sorting and filtering

- Sort a column: click the ↕ at the end of its header — ascending ▲, again for descending ▼, again for none.
- Sorting is only offered on files under 100 KB, because it needs the whole file. On a larger file that glyph is blank.
- Open the filter panel: click a header to select the column, Ctrl or Shift for more.
- Filters follow the column type: substring text with a regex switch, min/max for numbers and currency, from/to dates with optional times, checkboxes for true/false.
- Filters stack — a row must pass every active one — and they apply to a large file as its rows stream in.

## Reshaping columns

Right-click a column header. None of this writes to the file.

- **Rename column** gives it a new header.
- **Merge with previous** treats that separator as data rather than a delimiter.
- **Split by** a character breaks it in two. Characters absent from the column's sample are greyed out.
- **Evaluate as** changes its type. Only types that parse every sampled value are offered; text always is.

## Templates

- Save the current renames, splits, merges and types under a name: **[Template This](locate:Tabular_TemplateThis)**. Choose when it comes back: any file in this folder, any file matching a pattern like `sales-*.csv`, or only when you pick it.
- Apply, switch or delete a saved template: **[Apply Template](locate:Tabular_ApplyTemplate)**, whose ✕ deletes one. Incompatible templates are hidden.
- A matching template applies itself when the file opens; if several match, you are asked to choose. [Show me both](locate:Tabular_TemplateThis,Tabular_ApplyTemplate)
- Rename and delete saved templates in Options → Tabular Templates.

## Search and the assistant

- Search rows: type `?` and a term in [the bar at the bottom](locate:AiInputBox). A hit is a whole row; hits are marked in the grid and the toolbar chip counts them and steps through them.
- `fig*` matches from the start of a word, `*fig*` anywhere inside one. The scan stops after 5,000 matching rows and marks that count with a `+`.
- The assistant sees the detected shape, the columns and their types, the row count, any active filters, the visible rows and the rows you selected. It can read a range of rows, open the filter panel and jump to the first or last row, but it cannot change your data.
- Pin the tab to the ribbon and it comes back with your reshaping applied. Click the folder in the tab's breadcrumb to open a file explorer there.
