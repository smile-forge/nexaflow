# Integrity

Lists every snaplink whose target no longer exists, and lets you repair each one here.

---

## What a scan checks

- A snaplink is a link from a node in the product tree to something outside it: a file, a heading in a markdown file, a class or method, a URL, or another node.
- The checks are that the file exists, the heading path still resolves, the class and method are still declared, a URL is a well-formed absolute address, and a node id is still in the tree.
- A link that names only a file is checked for existence and nothing else — that was its whole claim.
- A link whose path goes through a linked git worktree is reported broken even though the file is there, and the detail names the repo path to use instead.
- A scan parses every file the links name, so it takes seconds and runs in the background. The page opens on the last saved result and swaps in the new one when it lands.
- Re-validate runs it again. The counts say how many links are broken, how many suggestions there are, and how many snaplinks across how many nodes were scanned; the line under them says when the tree was last validated.

## What a scan does not check

- A file with no grammar for its type, or one that cannot be read, leaves a class or method target unproven. It is left alone rather than called broken.
- The `ast` field on a link is never counted as broken. Nothing validated it before, so it holds prose as often as a path, and it is raised as an advisory instead.
- Nothing else about a node is checked — not its status, not its concerns. Only its links.

## Issues and advisories

- An issue is a link the scan proved broken. Those are the ones that fail a release build.
- An advisory is a suggestion. It never fails anything, and there are two kinds.
- A coverage suggestion is a test that declares with `[CoversNode]` that it covers a node the tree has not linked to it. If the id it names is not in the tree at all, the attribute is the stale part and the fix belongs on the test.
- A stale ast is a link whose file and class are fine but whose finer `ast` target no longer resolves.
- The box under the counts filters the list by node, scope, kind or detail.

## Repairing a link

- Select a row and edit the fields for that kind of link: document path, class and method, heading path, or URL.
- Moved? offers same-named files elsewhere under the product root. Pick offers the classes, methods or headings the named file actually has. Choosing from either fills the fields in and saves nothing.
- Apply fix writes the edited link to the tree and re-checks that one link, so you are told straight away whether it is sound, cannot be checked, or is still broken and why.
- Remove snaplink deletes the link from the node or concern it hangs off and saves the tree.
- Add link, on a coverage suggestion, writes that test onto the node's tests concern, and marks the concern done if it was not already.
- A stale ast shows one button, not both: Re-point ast writes the path the scan found, and Clear ast empties the field and leaves the link's file and class as they are. [Show me](locate:Integrity_RepointAst,Integrity_ClearAst)
- A row left over from an older scan is read-only until you re-validate.

## Going to the source

- Open target file opens the file the link names beside this page.
- Open node in Product opens the [Product](help:ProductManager) tree beside this page, on the node the link belongs to.
- Open this page from the Product tree with ⋮ → Validate snaplinks, or from the root's integrity tile. Its tab is named for the folder being validated. [Show me](locate:TabItem_ProductIntegrity)
- Closing the tab undoes nothing — every repair was written to the tree when you applied it. [Show me](locate:CloseTab_ProductIntegrity)

## With the assistant

- It can read what the scan found and say what is wrong with each link.
- It has the same product tools as the [Product](help:ProductManager) page, so it can re-point or remove a link and re-run the validation.
