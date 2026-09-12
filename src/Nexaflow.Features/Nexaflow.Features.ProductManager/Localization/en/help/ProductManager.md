# Product

Tracks a folder's software as a tree of nodes, each with a status, and lets you edit that tree here.

---

## Moving around the tree

- A node is one component of the product. Click its arc to focus it; the panel on the right then describes it, and the tab's breadcrumb bar shows the path to it.
- Click the centre to move up to the focused node's parent.
- The tab is named for the folder the product's `.product` folder sits in. [Show me](locate:TabItem_ProductManager,CloseTab_ProductManager)

## Editing a node

- Title, Description and Note are written when you leave the field. There is no save button — every edit on this page goes straight into `.product/tree.json` in that folder.
- The status pill beside the title cycles on each click: should, shouldn't, done, faulted. On a node with children it reads derived and does not respond, because that value is folded up from the children — faulted first, then should, then done, then shouldn't.
- Right-click a node for Add child node, Rename…, Delete…, Promote (out a level), Demote (under previous), Insert parent above…, and the four Status: entries.
- ⋮ → Restructure… shows the whole tree at once, where dragging a node onto another re-parents it.

## Concerns

- A concern is something tracked across the whole product but recorded per node — tests, theming, docs — and it carries its own status.
- Click a concern's status to cycle it through the same four values. Remove concern, on the box itself, takes the concern off this node.
- "+ concern" offers the concerns defined for this product that the node does not already carry.
- ⋮ → Settings… is where those definitions live: which concerns exist, which are added to every new node, and which need a snaplink before they can be marked done.

## Snaplinks

- A snaplink is a link from a node, or from one of its concerns, to a file, a heading in a markdown file, a class or method, or a URL.
- The link button at the bottom of the Properties panel opens the node's own links, and the one on a concern box opens that concern's. Link file picks a file on the left, then a heading, class or method inside it on the right; + Add selected records it. Link URL takes an address you type or drop.
- Open in a new tab follows a link: a code link opens the file in the code editor at the declaration, a document link in the markdown editor at the heading, a URL wherever URLs open. Remove deletes the link.
- A link whose target has moved still opens the path it was written with, so it opens nothing. [Integrity](help:ProductIntegrity) is what finds those and re-points them.

## Versions

- The dropdown at the top left picks Current, which is the tree as it stands, or one of the snapshots taken from it.
- ⋮ → Take snapshot exports the tree to the export folder and, when that folder is a git repo, commits it and tags it with the version number you give.
- A snapshot is read-only: nothing on the page can be edited while one is selected, and ⋮ → Delete this snapshot is the only version action it accepts.

## Checks and the graph

- The tile under CHECKS counts the snaplinks the last scan found broken. Clicking it opens [Integrity](help:ProductIntegrity), as does ⋮ → Validate snaplinks.
- ⋮ → Regenerate graph builds the knowledge graph — this tree crossed with the code its snaplinks reach — in the background, then opens it in [Graph](help:GraphViewer); ⋮ → Open graph opens the last one built without rebuilding it. The banner across the top appears only while that graph is missing or behind and something is being done about it.

## Searching and the assistant

- Type `?` and a term in the AI bar to search the knowledge graph: node names and the source behind them. The results open as their own tab, [Search](help:ProductSearch). Nothing matches until a graph has been built. [Show me](locate:AiInputBox)
- The assistant can read the focused node and what sits under it, and edit the tree: add, rename, move and delete nodes, set a node or concern status, add and remove snaplinks, run the snaplink validation, and re-point links after a rename.
- It can also query the knowledge graph — search it, read a node's neighbours, grep the source behind it, and rebuild it.
- Ask in the AI bar at the bottom of the window. [Show me](locate:AiInputBox)
