# Images

Opens one picture, a selection, or every picture in a folder.

---

## Opening pictures

- Double-click a picture in the [File System](help:FileSystem) page. PNG, JPEG, GIF, BMP, ICO, TIFF and WebP open here.
- Open several as one set: select them and choose **As Image**.
- A picture inside an archive opens the same way.
- Where a folder holds a fair share of pictures, its actions offer **Slideshow** and **Album**, taking that folder's pictures in name order.
- A picture that cannot be read says so in place of the picture.
- A JPEG or TIFF taken on its side is shown upright, the way its camera recorded it.
- The tab is named for the picture you opened, or counts the set, and the breadcrumb names the folder. [Show me](locate:TabItem_Images)

## Views

When a tab holds more than one image, pick a view from the switch at the top left. The picture you were on stays current when you switch. [Show me](locate:Image_ViewCarousel,Image_ViewAlbum,Image_ViewExplore,Image_ViewCollage)

- Carousel shows one image at a time, and is where a tab opens unless something asked for another view.
- Album shows thumbnails with file names. Click one to make it current, double-click to open it in the carousel.
- Explore shows thumbnails beside the selected picture, with a draggable divider.
- Collage scatters the whole set on a canvas.

## Moving through a set

- Step back and forth in the carousel with the arrows either side, with Space, or with the left and right arrow keys. The arrow keys and the mouse wheel move between pictures in the carousel only. Navigation does not loop, and the arrows disappear at the first and last picture.
- The wheel steps to the next picture, except at actual size, where it pans instead.
- Jump to a picture: click its dot beneath the carousel. A long set shows twenty dots at a time, with arrows for the rest.

## Slideshow and full screen

- Advance on a timer: turn on **Auto**. It loops back to the first picture at the end and runs in whichever view you are in. [Show me](locate:Image_AutoAdvance,Image_AutoSpeed)
- Set the speed with the slider: slow, medium or fast — a picture every ten seconds, four seconds, or one. A change takes effect straight away.
- The slideshow stops when you move to another tab and resumes when you come back.
- Fill the display: [View full screen](locate:Image_FullScreen). Move forward with Space, the right or down arrow, or a click; back with the left or up arrow, or a right-click. The wheel steps too.
- Esc returns to the tab on the picture you stopped at and stops the slideshow. A slideshow started before you went full screen keeps running, and every picture is shown with its edits.

## Rotating, flipping, scaling and the collage

- Rotate in ninety-degree steps, flip across or upside down, or switch between fit and actual size, from the toolbar floating over the picture in the carousel and in Explore. At actual size the picture scrolls. [Show me](locate:Image_RotateLeft,Image_RotateRight,Image_FlipHorizontal,Image_FlipVertical,Image_FitMode)
- A turn or a flip is an edit: it is remembered with the picture and undone with Ctrl+Z, like everything in the editor below. Fit and actual size change only the view.
- In the collage, drag anywhere to pan and use the wheel to zoom about the pointer. The chips in the corner offer minus, plus, **Fit** and **1:1**.
- An overview appears in the bottom right once the pile runs past the edges; drag inside it to move the view. Collage positions are kept when you leave and come back. Double-click a picture to open it in the carousel.

## Editing

Open the editor with **Edit** at the top right, with E, or from **Edit** among a picture's actions in the [File System](help:FileSystem) page. The picture sits on a canvas you can pan and zoom, with the tools beside it. [Show me](locate:Image_EditMode)

- Edits never touch the file until you save. They are kept beside the picture in a hidden file named after it, and every view shows the picture with them applied — the carousel, the thumbnails, full screen, and what the assistant is shown. A picture with edits is marked *edited* beside its name.
- **Crop** shows the whole picture with a frame over it. Drag the corners or edges, or drag inside to move it; it stops at the edge of the picture. Choose a shape — free, the picture's own, square, 4:3 or 16:9 — and flip it upright with **Portrait**. A rule-of-thirds grid helps placing. Enter applies the crop; Esc puts it back as it was when you opened the crop tool. [Show me](locate:Image_EditToolCrop,Image_EditAspectFree,Image_EditAspectPortrait)
- **Straighten** with the slider, or turn on **Level line** and drag along something that should be level — a horizon, a shelf — or upright, and the picture turns to match. The crop shrinks to keep empty corners out of the picture, and grows back if you turn back. [Show me](locate:Image_EditStraighten,Image_EditLevel)
- **Adjust** exposure, contrast, highlights, shadows, saturation, temperature and tint. Double-click a slider to reset it. **Auto enhance** reads the picture and sets the sliders for you, so any of them can still be changed after. [Show me](locate:Image_EditToolAdjust,Image_EditAuto)
- **AI enhance** cleans the picture up — softness, noise and JPEG blocks — with the picture tools on this machine, and repairs the faces in it as it goes; **Strength** mixes it with the picture as it was. The first time, the tools are a download of about 640 MB, offered in the banner above the picture. An enhanced picture is made once and kept, so coming back to it is instant. [Show me](locate:Image_EditToolAdjust,Image_EditAiEnhance)
- **Markup** draws arrows, boxes, text, pen lines and highlighter. Pick a tool and drag on the picture; for text, click where it goes and type in the panel. **Select** picks an annotation to move it, restyle it or delete it with Del. Annotations stay on what they mark if you crop again later. [Show me](locate:Image_EditToolMarkup,Image_EditMarkupArrow,Image_EditMarkupText)
- **Cut out** picks something out of the picture with picture selection on this machine: click the thing to keep,
  right-click (or Alt-click) what to leave out, or drag a box round it — or type what it is, like "mug" or "child in a red
  vest", and press **Find** to pick out every one. **Copy** puts it on the clipboard without its background, and
  **Save cut-out** saves it as a PNG, transparent around it and cropped to it; the picture itself is untouched. The first
  time, picture selection is a download of about 1.8 GB, offered in the panel. [Show me](locate:Image_EditToolCutOut,Image_CutOutPhrase,Image_CutOutSave)
- **Resize** sets the size the picture is saved at, as a percentage or in pixels, keeping its proportions unless you say otherwise. [Show me](locate:Image_EditToolResize)
- **AI upscale** makes the picture twice or four times its size with the picture tools, restoring detail rather than stretching it, and turns AI enhance on. Crops and annotations stay where they were. A picture too large to make four times its size is offered twice only. [Show me](locate:Image_EditToolResize,Image_EditUpscaleTwice)
- **Export** chooses whether saving keeps the camera details, location, dates and descriptions, or removes them, and sets the quality of a JPEG or WebP. The colour profile is always kept. [Show me](locate:Image_EditToolExport,Image_EditStripMetadata)
- **Undo** and **Redo** step through everything you did to the picture while it has been open — a slider drag, a crop drag or a pen line counts as one step. **Before** shows the picture as it is on disk, to compare. **Revert** removes every edit, and can itself be undone. [Show me](locate:Image_EditUndo,Image_EditRedo,Image_EditCompare,Image_EditRevert)
- **Save** writes the edits into the picture, after asking: the original pixels are replaced, the picture is saved upright, and its camera details are kept unless you chose to remove them. **Save as copy** writes a new file in the format its name asks for and leaves the original and its edits as they are. A picture inside an archive, an animated GIF or WebP, or an icon can only be saved as a copy. [Show me](locate:Image_EditSave,Image_EditSaveCopy)
- **Done**, or Esc, leaves the editor; the edits stay. Ctrl+Z and Ctrl+Y undo and redo, and Ctrl+S and Ctrl+Shift+S save, while editing. [Show me](locate:Image_EditDone)
- In the File System page a picture's edits go wherever it goes: copying, moving, renaming and deleting it take them along, a copy that is renamed to avoid a clash keeps its own, and dragging it to another program brings them too. **Revert edits** appears among the actions of an edited picture.
- If a picture is changed by another program after you edited it, the viewer asks whether to keep your edits on the new picture or discard them. A picture inside an archive can be edited, but its edits last only while it is open — save a copy to keep them.

## Deleting and the assistant

- Delete a picture: right-click it or its thumbnail and choose Delete. It asks first, sends the file and its edits to the Recycle Bin and removes it from every view; the selection moves to what is left.
- The assistant can look at the picture you are on, as edited, and answer questions about what is in it. It can report the format, dimensions and file size, and step to the next or previous picture.
- Ask the assistant to pick something out — "select the dog", "find the woman with red hair" — and it opens the editor's Cut out with every one it found picked, ready to copy or save. It can also cut something out of a picture file into a new PNG beside it without opening it.
- In a conversation, this tab's context chip previews every image it holds.
