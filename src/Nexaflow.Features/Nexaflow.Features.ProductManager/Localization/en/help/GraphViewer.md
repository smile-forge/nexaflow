# Graph

Draws the product tree joined to the code its snaplinks reach, and lets you walk it a node at a time.

---

## What is on the canvas

- Nodes come in five kinds: product nodes from the tree, and files, types, members and externals from the code. An external is a library or base type the build could not resolve to a file.
- Lines are relationships between two nodes — contains, extends, implements, imports, calls, references, instantiates, tests, documents, depends_on, and the XAML ones (view_of, handles, binds_to, uses_resource).
- A solid line was read straight out of the source. A dashed, fainter one was inferred by matching names, and the fainter it is the less certain it is.
- A hyperedge joins more than two nodes at once — a call with its arguments, a member with its return and parameters, an attribute with its named arguments. It is drawn as a dot with a spoke to each end.
- Only the neighbourhood around one focused node is drawn, never the whole graph.

## Moving around

- Drag the canvas to pan. The wheel zooms, keeping whatever is under the pointer where it is. There are no keyboard shortcuts on this page.
- Zoom out and the finer nodes drop away — members first, then types and externals, then files. Product nodes are always drawn, and a line disappears with either of its ends.
- Click a node to select it. That also re-centres the view on it and redraws the neighbourhood around it, so clicking is how you travel.
- Depth is how many edges out from the focused node to draw, from 1 to 10. [Show me](locate:Graph_Depth)
- Conf ≥ hides inferred edges below that confidence; at 0 every edge is drawn. [Show me](locate:Graph_MinConfidence)
- Back returns to the node you were focused on before, and Root re-centres on the top of the product tree. [Show me](locate:Graph_Back,Graph_ResetFocus)
- Reset view fits what is currently drawn to the window without changing the focus. [Show me](locate:Graph_ResetView)

## How much is drawn at once

- At most 700 nodes are drawn. When more are in range, the nearest are kept — by hop distance, then by kind — and the focused node is always one of them.
- The rest are counted in the toolbar as a "+N more" note. Lower Depth, or focus on a node closer to what you want, to bring the count under that limit.

## Segments

- The rail on the left lists the segments in view: the clusters the graph build found, one row each, in the colour their nodes are drawn in and with how many there are.
- Clear a row's box to hide that segment's nodes; they stay hidden as you move to other nodes. All brings every segment back. [Show me](locate:Graph_ShowAllSegments)
- Hyperedges draws the hyperedges or leaves them out. It is there only when the graph has some.
- The chevron collapses the list to its header.

## The panel on the right

- Selecting a node shows its label, kind and id, and the file, language and confidence recorded for it, followed by anything else the build stored.
- Open in Code opens that file in the code or markdown editor, on the declaration when one was recorded. It is there only for a node that has a file.
- Nothing here changes the graph or the tree — it is a view over what was built.
- To pick up code that has changed since, use ⋮ → Regenerate graph on the [Product](help:ProductManager) tree; Open graph reopens the last one built without rebuilding it.
