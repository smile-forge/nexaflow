# Project

Shows one project's description, completion criteria and backlog, and lets you change them.

---

## The two tabs
- Project Details holds the name, description and completion criteria. Backlog holds the items. [Show me](locate:Projects_Tab_Details,Projects_Tab_Backlog)
- The page opens on Backlog when the project already has a description, and on Project Details when it does not.
- The Projects breadcrumb goes back to the [project list](help:Projects).
- A project written in the old format opens read-only, and you are asked whether to upgrade it. Answering later is the Upgrade button on the banner; upgrading rolls the old scope, notes and plan fields into the description and enables editing.

## Project details
- The name and the description are written to the project's `.project` file as you change them. There is no save button for either.
- The description is markdown. Click a paragraph to edit it in place; the rest stays rendered.
- A solution file is shown when the `.project` file names one. It is not editable here.
- Modified, at the top right, is the time the `.project` file was last written.

## Completion criteria
- A criterion is a sentence saying what has to be true for the project to be finished.
- Add adds an empty one; the ✕ beside a criterion removes it. [Show me](locate:Projects_Detail_AddCriterion)
- Each carries one of four states: should, shouldn't, done, faulted. Pick it from the dropdown beside the text.
- Criteria are saved as you add, change and remove them.

## The backlog
- Add an item by typing its title and pressing Enter, or by clicking + Add. [Show me](locate:Projects_Detail_NewTodoTitle,Projects_Detail_AddTodo)
- Select an item to edit it on the right. Its title and description are saved only when you click Save; its status is saved as soon as you change it. [Show me](locate:Projects_Detail_SaveItem,Projects_Detail_DeleteTodo)
- Progress moves the item to the next status in the order set in Options, and is unavailable on the last one. The dropdown sets any status directly, including the cancelled one, which Progress steps past. [Show me](locate:Projects_Detail_StatusCombo,Projects_Detail_Progress)
- A cancelled item stays in the list, struck through.
- If a status has been deleted from Options, items on it are marked invalid and keep the old name until you pick a new status. They are never moved for you.
- Delete asks you to confirm, and cannot be undone.
- Type `?` and a word in the AI bar to narrow the list to items whose title or detail match.

## With the assistant
- It can read this project in full — description, completion criteria and every backlog item.
- It can read one backlog item, named by title or id, or the one you have selected.
- Task to AI opens a conversation asking it to carry the selected item out; Plan with AI asks it to plan the work first. Both attach the project folder as context, and both name the item's title, status and detail.
- It reads the project; changes to it are ones you make here.
