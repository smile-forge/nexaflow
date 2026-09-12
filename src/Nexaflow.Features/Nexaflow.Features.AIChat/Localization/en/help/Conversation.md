# Conversation

A saved chat with the assistant that can read and act on the pages you pin to it.

---

## Talking to the assistant

- Type in the AI bar while this tab is in front and the message goes to this thread. [Show me](locate:AiInputBox)
- Select any part of a reply and copy it. Plain text, formatted HTML and the Markdown source all reach the clipboard.
- Expand *Ran N tools* to see each tool it ran with its result, and anything that failed marked out. [Show me](locate:Conversation_ToolBatchExpand)
- Accept or deny an action it needs your say-so for, with its reason above the buttons. The entry then settles into *Approved* or *Declined*. [Show me](locate:Conversation_ApprovalAccept,Conversation_ApprovalDeny)
- Type while it is working and your message is queued, then delivered at the end of the turn in progress.

## Pinning pages and files

- The Context Items banner holds everything the assistant can see.
- Pin something by dragging a tab onto the banner, dropping files on it, or choosing from your open tabs and the pages this workspace can make. [Show me](locate:Conversation_AddContext)
- A pinned tab stays live: pin a folder and the assistant gets its current listing — names, sizes and dates — not just a path. See [File System](help:FileSystem).
- Dropped files are attached with their text read in. One that has since been moved or deleted is marked.
- Pinning an open tab leaves it in the tab strip, and pinning the same thing twice flashes the chip you already have.
- Collapse the banner to one line naming the first three pinned items, then *and N more*. [Show me](locate:Conversation_ToggleContext)

## Risk badges

- A pinned page that gives the assistant wide reach carries a badge: elevated for a whole non-system drive; high for This PC, the system drive, the Windows folder, running processes, services or environment variables.
- The badge follows the page, so navigating a pinned folder somewhere sharper changes it.
- Collapsing the banner hides the detail, not the warning.

## Previewing what is pinned

- Click a chip to open a preview beside the thread, and click it again to close. [Show me](locate:Conversation_ContextChip)
- Images, fonts, Markdown, text and code get a read-only view. Any other page shows the exact context text the assistant will receive from it.
- Drag the divider to resize the panel. It keeps that width for the rest of the session.
- Close the preview from the panel itself. [Show me](locate:Conversation_ClosePreview)

## What the assistant can do with them

- It is given the tools of every page you pin, so it can act through them and not only read them.
- Where one tool arrives from two pages under different scopes, the pair is folded into a single tool that makes the assistant say which scope it means.
- Pin a page that is still gathering its data and your message waits until that page is ready.

## History

- Messages, pinned pages and attachments are saved as you go and come back when you reopen the conversation. The name is generated from your first message.
- Each of your messages is timestamped — *5 minutes ago* while it is still today, a date once it is not.
- Rewind your latest message to drop it and everything after it, from the thread, from the running token count and from the saved record, and get its text back in the AI bar to edit and send again. Rewind is refused while the assistant is working. [Show me](locate:Conversation_Rewind,AiInputBox)
- The footer tracks the conversation against the model's context window. What is sent is held to three quarters of that window: the oldest messages give way first, and once the conversation has been analysed the summary stands in for the part it covers.
- Search the thread by typing `?` and a word in the AI bar. Every matching message is marked where it sits, yours and the assistant's; the chip in the footer counts the matches and steps between them. The assistant can run the same search.
- Every conversation is listed and searchable in [AI Chat](help:AIChat). Close this tab after chatting and, unless you have turned summaries off, it is analysed in the background.
