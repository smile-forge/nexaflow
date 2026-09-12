# Audio player

Plays audio files, shows the waveform and lyrics, and edits the track's tags.

---

## Opening tracks

- Open a track or a selection **As Audio** from the [File System](help:FileSystem) page. A selection becomes the queue in the order you picked it, and anything that is not audio is left out.
- Open a single track **As Audio** and it plays on its own, with no folder queue.
- **Play Folder** is offered on any folder where at least 30% of the files are audio. It queues that folder's own tracks in name order, starts playing and names the tab after the folder.
- MP3, WAV, FLAC, M4A, AAC, WMA, Ogg Vorbis and Opus, including a track inside an archive you are browsing.

## The queue

- The playlist drawer lists every track. Double-click one to play it, or drag it to a new place — the track that is playing carries on.
- **Previous** and **Next** step through the queue and keep playing if you were. A counter such as *3 / 12* shows where you are.
- More than three seconds into a track, **Previous** starts it again rather than skipping back.
- Each track rolls on to the next as it ends. Turn that off under **Audio Player** in [Options](locate:Chrome_OptionsButton), where you can also set how many bars the spectrum draws.

## Playing

- [Play/pause, stop and the volume slider](locate:Audio_PlayPause,Audio_Stop,Audio_Volume). The volume you close the player at is where the next one starts.
- The waveform is the seek bar. Click anywhere on it to move to that point, lyrics and all. Elapsed and total time sit underneath.
- The waveform covers the whole track and is worked out as the file loads.
- The spectrum is read before the volume control, so the bars still move with the sound turned right down.
- Switch on [Background](locate:Audio_BackgroundToggle) and playback continues when you move to another tab, with a play/pause and next-track remote in the window's top bar. Coming back picks up wherever the track has got to. The switch is remembered.
- With **Background** off, leaving the tab pauses the track and coming back resumes it.
- Minimising the window does not stop playback.

## Lyrics

- Open [Lyrics](locate:Audio_LyricsToggle) to show the words beside the player.
- Put an `.lrc` file with the same name next to the track and the line being sung is highlighted and scrolled into view. Seek on the waveform and the highlight follows.
- A line stamped several times appears at each time, an `[offset:]` tag shifts the whole file, and a line that cannot be read is skipped rather than spoiling the rest.
- With no `.lrc` file, lyrics saved in the track's tags are shown as plain text.

## Tags

- The Tags drawer holds title, artist, album, year, track number, genre and comment, with the cover art above them.
- Edit a field, or [change the cover for a JPG, PNG or BMP, or remove it, then save](locate:Audio_TagsToggle,Audio_TagChangeArt,Audio_TagRemoveArt,Audio_TagSave).
- Changes are written into the track file itself.
- Saving during playback resumes from the same moment, with the new title and cover shown.
- The same fields cover ID3 in MP3, Vorbis comments in FLAC and Ogg, and MP4 tags in M4A.

## With the assistant

- It cannot hear the audio, but it knows the track and artist, whether it is playing, how far in it is and where it sits in the queue.
- Ask it to play, pause or stop, to seek to a time (*90* or *1:30*), or to skip forwards or back — for example "what's playing?" or "go to 2:10".
