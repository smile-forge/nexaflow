# Search

Lists what a query matched in this product's knowledge graph, and opens each result where it lives.

---

## Running a query

- Type in the box beside Graph search and press Enter, or click Search. [Show me](locate:ProductSearch_Query,ProductSearch_Run)
- Plain text is matched as written, and case is ignored. `/pattern/` is a regular expression; add `c` after the closing slash to make it case-sensitive, or `i` to keep it insensitive.
- Filename filters do not apply to a graph, so a query that uses one is refused. Search a node name or a word in the source instead.
- The query that opened this tab has already been run. Searching again from the [Product](help:ProductManager) tree re-points this tab rather than opening a second one.
- The tab is named for the folder the product's `.product` folder sits in. [Show me](locate:TabItem_ProductSearch,CloseTab_ProductSearch)

## What gets searched

- Node names, in two senses: the nodes of the product tree, and the files, types and members the graph read out of the code.
- The source behind every code node, one row per matching line.
- A plain query is ranked — exact name first, then names starting with it, then names containing it. A regular expression or a case-sensitive query is matched in the graph's own order instead.
- Each of the two passes stops at 200 rows. The line under the box says how many node names and how many source lines matched, and says which pass was capped. [Show me](locate:ProductSearch_Status)
- If no knowledge graph has been built yet, that line says so. Build one from the [Product](help:ProductManager) tab's ⋮ menu.

## Reading a result

- The badge on the left is the kind of node the row came from. [Show me](locate:ProductSearch_Results)
- Under the label is where it is: the node's id when its name is what matched, or `path:line` when a source line did, with the matched line printed beneath it.

## Opening a result

- Open in tree focuses the [Product](help:ProductManager) tree on that node. It is there only for a row that is a product-tree node.
- Open file opens the file the node was read from, in whatever normally opens that kind of file. It is there for any other row that has a file behind it.
- A node with no file behind it gets neither, so those rows carry only In graph.
- In graph opens [Graph](help:GraphViewer) with that node selected.
- Nothing on this page changes the product tree. Every edit is made on the [Product](help:ProductManager) page.

## Searching from the AI bar

- Type `?` and a term in the AI bar to run a query without using the box on the page. [Show me](locate:AiInputBox)
- The same thing typed on the [Product](help:ProductManager) tree is what opens this page in the first place.

## With the assistant

- It can read the rows on screen — the first thirty of them, each with its kind, label and location.
- It can run a query of its own and narrow the page to the rows it kept, and clearing that search re-runs the query to bring the rest back.
- The tools that edit the tree are not here; ask for those on the [Product](help:ProductManager) page.
