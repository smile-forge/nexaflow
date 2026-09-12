# AI Chat

Lists your saved conversations with the assistant, and starts new ones.

---

## Opening a conversation

- Open a conversation to bring back its messages and the pages that were pinned to it. [Show me](locate:AiChat_OpenConversation)
- Start an empty one with New conversation. It is saved once you send a first message. [Show me](locate:AiChat_NewConversation)
- Each one opens as a [Conversation](help:Conversation) tab.
- The list is ordered by when a conversation was last used.
- A conversation's name is generated from your first message, so search rather than the name is how you find one again.
- Conversations belong to the workspace you are in. Switch workspace and you get that workspace's conversations.

## Analysis

- Close a conversation after chatting and it is analysed in the background. The same happens to an older one you open and close that has no analysis yet. The row's summary fills in when it arrives.
- Read the analysis for a conversation: summary, topic and tone, what it understood about you and about the assistant, key decisions, important facts, and the attachments that mattered. [Show me](locate:AiChat_ShowAnalysis)
- Reopen the conversation later and the analysis stands in for the messages it covers.
- Turn the write-ups off with *Automatic Conversation Analysis* in Options.

## Searching

- Search every message in every conversation, yours and the assistant's: type `?` and a few words in the AI bar. [Show me](locate:AiInputBox)
- Wrap a term in slashes — `?/retr(y|ies)/` — to search with a regular expression.
- Search ignores the date filter, so it covers every saved conversation.
- The chip beside the filter counts the matches. Clear it and the date range applies again.
- Ask the assistant to find something and it runs the same search, then narrows the list to what it picks out.

## Keeping and removing

- Filter by today, the last 7 days, this month or this year. The filter goes by when a conversation was last used.
- Delete a conversation. You are asked to confirm, and it is then removed from disk. [Show me](locate:AiChat_DeleteConversation)
- Set how long conversations are kept with *Keep Conversations For* in Options: a week, a month, a year, five years or forever. The default is a year. Older ones are cleared when this page opens, again by last use.
- Export every conversation in a workspace as markdown, one file each, from that workspace's Configure Workspace page.

## Where conversations start

- New conversation on this page.
- *Continue as Conversation* on a reply, which saves that exchange, pins the page you asked from, and attaches any files the assistant touched.
- *Task to AI* and *Plan with AI* in Projects, which pin the project folder and send the task.
