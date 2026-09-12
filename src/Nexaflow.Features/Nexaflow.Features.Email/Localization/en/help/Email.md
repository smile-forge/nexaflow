# Email viewer

Opens a saved `.eml` or Outlook `.msg` file and shows the message, its headers and its attachments. It reads only: there is no send, reply or forward here.

---

## Opening messages

- Double-click an `.eml` or `.msg` in the [File System](help:FileSystem) page, or choose **As Email** from its actions. A message inside a zip opens where it is, with nothing to extract first.
- Subject, From, To, Cc and the date, in your local time, sit above the body. A field the message does not have is not shown.
- A `.msg` takes its sender, its To and Cc lists and its sent date from Outlook's own storage, so the envelope is filled in for a message that never crossed the internet.
- A truncated export, or a file that is not really a message, shows the reason where the message would be.

## Reading the message

- The HTML body is rendered by Nexaflow: headings, emphasis, links, lists, quotes, code and tables come through, drawn in your theme's colours rather than the sender's.
- Pictures carried inside the message are shown in place, and a message with only a plain-text part shows that text.
- Select part of the rendered message and copy it, and the clipboard gets plain text, HTML and markdown at once.
- The toolbar offers [Rendered, Plain text and HTML source](locate:Email_Rendered,Email_PlainText,Email_HtmlSource). Every message opens on Rendered; the other two appear only when the message carries that form, so the toolbar also tells you what the sender sent.
- **Plain text** shows the text part as written, in a monospaced pane you can select and copy from. **HTML source** shows the raw markup, which is where to check where a link really points.
- [Open in browser](locate:Email_OpenInBrowser) writes the HTML and its embedded images to a scratch folder and opens them in a browser tab inside Nexaflow, so the message keeps the sender's layout. The copy is temporary and is removed when Nexaflow closes, and the button appears only for a message that has an HTML body.

## Headers

- [All headers](locate:Email_AllHeaders) lists every header the message carried: Received chains, DKIM and ARC signatures, message IDs and the rest.
- An Outlook message that never left the building may have none to show.
- The list stays folded until you open it, then scrolls inside its own capped height, so a forwarded message's headers cannot push the body off the screen.

## Attachments

- [Each attachment](locate:Email_AttachmentTile) has a tile with its name and size; hover for its type. Click one and it opens in the viewer that suits it, with nothing to save first.
- A message attached to a message opens in an Email tab of its own, with its own attachments a click away.
- Only real attachments get a tile. Pictures that belong inside the body stay in the body, and a message with no attachments has no strip.
- Two attachments with the same name each open their own file, and a part that arrived with no name is given one.
- Attachments are read straight out of the message as it sits on disk. Nothing is written back into it.

## Searching

- Type `?` and a word or two into [the AI bar](locate:AiInputBox) to search this message: the body, every attachment by name, the words inside a text attachment, and — while All headers is open — the headers.
- Matches are highlighted in the rendered body and selected in the Plain text and HTML source views, and a matching header row or attachment tile is marked.
- In an `.eml`, the search also looks inside a message forwarded as an attachment — its headers, its body and its own attachments.
- A chip beside the toolbar counts the matches, steps through the ones in the body, and clears the search in one click. No matches is reported too, so you know the search ran.

## With the assistant

- With [page context](locate:AiBar_ContextToggle) switched on, it sees the subject, sender, recipients, date, attachment names and the opening of the body. Ask it to summarise a long message, draft a reply, or explain what an attachment is for.
- It can fetch the complete body and every header for a long message, beyond what it is given to start with.
- It can list the attachments with their types and sizes, and read a text attachment — notes, CSV, JSON, XML — out of the message. Binary and very large files are left to their own viewers.
- All three of those are read-only, so it uses them without asking first.
- It cannot send mail from here. A reply it drafts is yours to copy and send however you like.
