# Video player

Plays a video file in a tab.

---

## Opening a video

- Choose **Play Video** on a video file in the File System page. Common video extensions also open here on a double-click.
- The tab opens with a *Loading…* placeholder while the file is read in the background.
- A file inside an archive or a disk image is extracted once to a temporary copy, and that copy is reused for everything else.
- Nothing plays until you press the large play button on the still that covers the picture. [Show me](locate:Video_BigPlay)
- If the file will not open, the reason is shown on the picture. An engine failure it cannot recover from closes the tab with a notification.
- Options → Video has one setting: hardware-accelerated decoding, off by default. Turn it on for smoother playback of very large video; it applies to videos you open from then on.

## Playback controls

- Play and pause. Clicking anywhere on the picture does the same. [Show me](locate:Video_PlayPause)
- Skip back or forward five seconds at a time. [Show me](locate:Video_StepBack,Video_StepForward)
- Scrub with the timeline. Elapsed time is on its left, total on its right. [Show me](locate:Video_SeekBar)
- Mute [Show me](locate:Video_Mute), or set the level on a 0–100 volume slider [Show me](locate:Video_Volume). The two are separate.
- Choose 1×, 1.25×, 1.5× or 2× speed. The button shows the rate you are on. [Show me](locate:Video_Speed)
- Switching to another tab pauses the video. Press play again when you come back.

## Subtitles and scenes

- If the file carries text tracks, a **CC** button appears on the bar. It lists every subtitle track the file declares — named from the track's own name, description or language — plus **Off**. A file with no text tracks does not show the button. [Show me](locate:Video_Subtitles)
- The scene strip is 32 thumbnails sampled evenly across the clip, each stamped with the time it came from. Click one to jump there. Click one before you have started the video and it starts and lands on that point once the first frame is up. [Show me](locate:Video_SceneStripToggle)
- Sampling runs on a second, short-lived decoder, so building the strip does not disturb what you are watching. The first thumbnail is the still you see before pressing play.

## File details

- Open the info panel to see what the player read out of the media. Rows appear only when the file reports them. [Show me](locate:Video_InfoToggle)
- File: name, container, size on disk and duration.
- Video: codec, resolution, frame rate and bitrate.
- Audio: codec, channel layout, sample rate and language.
- Subtitles: one entry per text track, with its language.

## Fullscreen

- Open a borderless window over the whole display, with the same transport bar driving the same playback. [Show me](locate:Video_Fullscreen)
- Esc returns you to the tab, and Space toggles playback while you are there.

## The assistant

- It knows which video is open, what the media is, and where the playhead is.
- Ask what is on screen and it captures the frame you are on and receives it as an image. The frame goes into the conversation and is not written to disk.
- Ask for the media details: container, duration, position, state, speed, volume and, once the file is read, the codec, resolution and track rows from the info panel.
- Ask it to move the playhead to a timestamp. That asks your approval first, because it changes what you are watching.
- Two video tabs pinned into one conversation stay separate, so it does not confuse one clip's tools for another's.
