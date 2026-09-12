# Log viewer

Reads a log file, starting at the newest lines, and keeps up as the file grows. Nothing here writes to the file.

---

## Opening a log

- A `.log` file opens here from the [File System](help:FileSystem) page. **As Log** opens any other file here from its actions.
- A large file opens on its last few kilobytes only, snapped to a clean line start, and the earlier history loads above them in the background. A small one opens whole.
- The status bar says **Loading history…** until the rest has arrived, and the line count beside it counts only what has loaded.
- Change the encoding with the dropdown at the end of the toolbar. It is detected from the byte-order mark, then from whether the bytes are valid UTF-8.

## Following and highlighting

- Scroll new lines into view as they land with Follow, on from the moment the tab opens. Turn it off to hold your place. [Show me](locate:Log_Follow)
- Freeze the view with Pause. Lines that arrive meanwhile are added in one go when you resume, while the file size in the status bar keeps climbing. [Show me](locate:Log_Pause)
- Wash whole lines by severity with the five toggles, each independent of the others. `WRN`, `warning`, `CRITICAL`, `ERR`, `DBG`, `TRACE` and `VERBOSE` all reach the right toggle, bracketed, piped or bare. [Show me](locate:Log_HighlightFatal,Log_HighlightError,Log_HighlightWarning,Log_HighlightInfo,Log_HighlightDebug)
- Mark every occurrence of a word, whatever its case: put it in [the highlight box](locate:Log_HighlightTerm). Lines that have not arrived yet are marked too.

## JSON logs

A log with one JSON object per line is recognised on its own, and the status bar names the format it settled on.

- Level and timestamp are read from the field names Serilog, Bunyan, pino, python-json-logger, structlog and the ECS and Google Cloud formats use.
- A numeric level on either ladder — Bunyan and pino's tens, or syslog's nought to seven — lights the right toggle, and a Unix time in seconds or milliseconds is read as a time.
- Serilog's compact format leaves the level out for Information; those lines are read as Information. A stack-trace continuation, or a half-written last line, is shown as it is rather than failing the file.

## Filtering

Filters fade lines rather than removing them.

- Dim every line that does not match a regular expression: type it in [the pattern box](locate:Log_FilterRegex). Case is ignored, and a half-typed or invalid expression leaves the log unfiltered.
- Filter by time when the log carries timestamps: **From** and **To** appear below the pattern, filled with the log's own first and last times and refilled with the older ones once the history lands. **Apply** dims everything outside the range and jumps to the first line inside it. Clear **To** to run the range to the end of that day.
- Timestamps are read from plain text in ISO 8601, US-style dates and syslog times where they open the line; in JSON, from ISO timestamps and Unix times wherever the field sits.
- An active pattern is shown in the warning colour in the status bar, beside the line count, the file size and the detected format.

## Searching and copying

- Search the whole loaded log: type `?` and a term in [the AI bar](locate:AiInputBox). A chip in the status bar carries the count, says what you searched for, steps through the matches and clears the search.
- Use a word, `time*` for words starting that way, `*out*` for any part of a word, or `/time(out|d out)/` for a regular expression.
- Painting stops after a few thousand spans; the count does not. New lines are searched as they arrive, and you stay on the match you were reading. Searching leaves the pattern filter and the highlight term untouched.
- Search before the history has landed and it says only the recent lines were searched; the count completes once the rest arrives.
- Pick a line: click the thin strip down the left edge, and again to drop it. Picked lines stay tinted as you scroll. Copy them in line order with [the Copy button](locate:Log_CopySelected), which counts them and is greyed out until there is something to copy.

## The assistant

- It knows which file is open and how big it is, how many lines are loaded and whether that is still only the tail, the detected format, the span of the timestamps, whether the tail is live or paused, every filter and highlight in play, and how many lines you have picked.
- It can read the latest lines, a particular range, or the lines you picked; search the log and narrow the view to the matches; and set the pattern, the time range, the severity toggles or the pause. None of it touches the file.
- To change the file, open it in the [Text viewer](help:Text), which also splits a large one into smaller files.
