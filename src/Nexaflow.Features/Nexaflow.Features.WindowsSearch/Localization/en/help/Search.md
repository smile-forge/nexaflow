# Search

Finds files by name or by what is inside them, using the Windows Search index.

---

## Running a search
- The query sits in the box at the top of the page. Edit it and press Enter to run it again. [Show me](locate:SearchQueryBox)
- Editing the box replaces the query, so that is how you drop a term you added earlier.
- Typing `?` and a query in the input bar narrows what is already here instead of replacing it — the new terms are added to the ones in the box.
- The breadcrumb names the folder being searched, or This PC when the search covers every drive.

## What the box accepts
- Words separated by spaces are all required. A word matches whole, so `needle` does not match `needless`.
- `|` inside one term means or: `*.txt|*.md`.
- Quote a phrase to match it exactly, spaces included: `"annual report"`.
- File globs match against the name: `*.xml`, `report*`.
- A regular expression goes between slashes: `/ma(ths|gic)/`. Add `c` after the closing slash to match case.
- Advanced Query Syntax property filters work, the same ones Explorer's search box takes: `kind:document`, `size:>1mb`, `author:john`, `modified:last week`.
- A regex with no literal text in it gives the index nothing to narrow on, and the page says so instead of showing an empty list.

## What the index can answer
- Only locations Windows indexes are searched. A file in a folder the indexer was never pointed at will not appear.
- Matching on what is inside a file depends on Windows having indexed that file's contents.
- The banner above the list says which case you are in: not indexed, only partly indexed, or indexed and nothing found.
- When the Windows Search service is not running, the banner says nothing could be looked up.
- At most 500 files come back, most recently modified first; the banner says when that limit was reached.

## Scanning a folder instead
- When the index has no answer, the banner offers Scan, which walks the folder and reads the files. It can run for minutes, so it is offered rather than started. [Show me](locate:ScanFolder,DeclineScan)
- Matches appear as they are found. Stop ends the walk, and the count it reports is a floor, not a total. [Show me](locate:StopScan)
- Scan again re-runs a scan you stopped. [Show me](locate:RescanFolder)
- Once a scan has produced the results, narrowing them runs another scan rather than going back to the index.

## Working with results
- A query the index can only narrow — a regex, a glob, several terms — puts rows in the list before they are proven. Up to 50 are checked for you.
- Past that the banner asks: Check them reads the rest, Skip leaves them unchecked. [Show me](locate:VerifyRemaining,SkipVerification)
- The badge on a row's icon says what the check found — not looked at yet, probably matches, could not be read, or does not match.
- Click a column header to sort by Name, Location, Size or Modified; click it again to reverse the order.
- Select a row, then Open to open the file with its default application, or Location to open the containing folder in a new Files tab. Both stay disabled until a row is selected.

## The assistant
- Ask it to search and it runs the query in this tab. It replies before results arrive, so it has to look again to read them.
- Ask why a folder is not being found and it reports the Windows crawl-scope rules for that path. It only reads them.
- It can tell you the current query, the scope and the number of results. It cannot open a file or a folder for you.
