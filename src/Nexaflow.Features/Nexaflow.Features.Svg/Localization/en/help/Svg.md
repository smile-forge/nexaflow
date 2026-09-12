# SVG viewer

Shows an SVG drawing and lets you zoom into it without it going soft.

---

## Opening files
- Double-click an `.svg` or `.svgz` in the [File System](help:FileSystem) page, or pick **As SVG** from the file's actions.
- One drawing per tab, named after the file, so several variants can sit side by side in [their own tabs](locate:TabItem_Svg).
- An SVG inside an archive or a disk image opens like one on disk — see [Archives](help:Compressed).
- Compressed files are recognised by their content, not their name, so gzip labelled `.svg` opens too.
- Parsing and rendering happen off the interface thread, so a busy drawing does not freeze the window.
- Where the drawing would be, you are told if the file could not be read, if it is past 64 MB and will not be rendered, or if it parsed but contains nothing to draw.
- Scripts and animation are shown but never run: interactive content is switched off and the finished drawing is frozen.
- Document type definitions are skipped, external entities are never resolved, entity expansion is capped, and external resource references are ignored, so the file cannot make the viewer fetch anything.

## Zoom and pan
- The drawing is scaled to fit the tab and centred when it opens.
- Scroll to zoom. The point under the pointer stays put. The range runs from a twentieth of size to sixty-four times it.
- Drag to pan. The drag begins only once the pointer has moved, so a click is still a click.
- Zooming redraws the shapes rather than enlarging pixels, and lettering is drawn as outlines.
- [Reset view](locate:Svg_ResetView) refits the drawing to the window and re-centres it.

## Background
- The canvas starts on a checkerboard, so you can tell which parts of the drawing are transparent.
- [Turn the checkerboard off](locate:Svg_Checkerboard) for a flat backdrop.

## What the file says
- The declared width and height, exactly as authored — `512`, `24px`, `100%`. Where the document gives none, the rendered extent is shown instead.
- The viewBox, verbatim, when the file has one.
- A count of drawable elements: paths, rectangles, circles, ellipses, lines, polylines, polygons, text, images and uses.
- The file size on disk, which for a `.svgz` is the compressed size.

## With the assistant
- Ask in [the AI bar](locate:AiInputBox). The assistant already knows the file's name, its dimensions and viewBox, how many drawable elements it holds, and how big it is.
- It can look at the artwork: it renders the whole drawing to an image, backed with white and about a thousand pixels on its long side, and examines that. Ask what a logo shows or which way an arrow points.
- It can report the exact figures, the same ones shown for the file.
- Both only read the drawing already loaded, so neither changes your file and neither waits for your approval.
- Until the file has finished loading the assistant holds back, and it is released even when the load fails.
