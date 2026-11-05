# Artist

Artist edits one picture. It can be a photo, whose edits are kept beside it until you save them, or a layered picture you paint
and arrange. With no picture open, it helps you make one.

---

## Opening a picture

- In the [Images](help:Images) viewer, click **Edit** at the top right, or press E. The picture opens in its own Artist tab, and the viewer stays where it was.
- In the [File System](help:FileSystem) page, **Edit** in a picture's actions opens it here too, each selected picture in its own tab. Double-clicking an OpenRaster file (.ora) or a Photoshop file (.psd, .psb) also opens it here.
- The tab is named after the picture, and the breadcrumb shows its folder.

## The start screen

- **Artist** on the ribbon opens a tab with no picture. It shows the start screen, where you choose what to make. [Show me](locate:Images_Start)
- **Create** makes a blank canvas. It starts at your screen's size; you can pick another size by name, or type one in pixels, millimetres or inches at a resolution. ⇄ swaps width and height. Choose a transparent, white or coloured background. The canvas is a draft until you save it: **Save as** in the strip above it, or **Save**, asks where to keep it, and the tab becomes that file. [Show me](locate:Images_StartCreate)
- **Generate a picture** draws what you describe with the workspace's image model, as the canvas's first layer. While it draws, the Layers panel shows it with **Stop**. If the workspace has no image model, the screen says so and links to its AI settings. [Show me](locate:Images_StartGenerate,Images_StartAiSettings)
- **Paste from clipboard** makes a canvas from the picture on the clipboard. **Open picture…** edits a picture from disk. [Show me](locate:Images_StartPaste,Images_StartOpen)
- **Recent canvases** lists the layered pictures you opened lately, including unsaved drafts. Click one to open it again. [Show me](locate:Images_StartRecentList)

## Editing

The picture sits on a canvas you can pan and zoom. Beside it are the tools, in the order you usually need them: **Crop**,
**Adjust**, **Retouch**, **Select**, **Markup**, **Paint**, **Move** and **Resize**. [Show me](locate:Image_EditToolCrop)

- Each tool's panel is split into groups, such as **Light**, **Colour** and **Brush**. Click a group's title to fold it away. It stays folded, in every Artist tab, until you open it again. Some groups start folded, so each panel opens short.
- Every slider is one row with its value at the end. Drag it, double-click it to reset it, or click the value and type a new one.
- Your edits never change the file until you save. They are kept beside the picture in a hidden file named after it. Every view shows the picture with its edits: the carousel, thumbnails, full screen, and what the assistant sees. An edited picture is marked *edited* beside its name.

### The selection

There is one selection, and only the **Select** tool makes it. Every other tool shows it as a dotted outline and works on it:
Retouch erases it, lens blur can keep it sharp, and Paint paints only inside it. Press Esc to deselect.

### Crop

- **Crop** crops, turns, flips and straightens. It shows the whole picture with a crop frame over it. Drag a corner or an edge, or drag inside the frame to move it. Choose a shape — free, the picture's own, square, 4:3 or 16:9 — and switch it with **Portrait**. A rule-of-thirds grid helps you place the frame. Press Enter to apply the crop, or Esc to put it back as it was. [Show me](locate:Image_EditToolCrop,Image_EditAspectFree,Image_EditAspectPortrait)
- To straighten, drag the **Straighten** slider, or turn on **Level line** and draw along something that should be level or upright, like a horizon. The crop shrinks to keep empty corners out, and grows back if you turn back. [Show me](locate:Image_EditStraighten,Image_EditLevel)
- **Expand** grows a photo past its edges with the workspace's picture-editing model. Turn on **Generate the new area**, drag the frame's edges out, describe the new area if you like, and click **Generate**. The photo becomes a layered picture, with the new area as a layer of its own and the photo kept beneath it. [Show me](locate:Image_GroupCropExpand,Image_EditCropGenerate,Image_EditCropGenerateGo)

### Adjust

- **Adjust** opens on the **Histogram**. It shows how the picture's brightness spreads from black on the left to white on the right, and follows every change you make. It reads 16- and 32-bit pictures at their own depth. The marks at its top corners light up when shadows or highlights clip, which means they lose all detail. Point at a mark to see where on the picture, in blue for shadows and red for highlights, or click it to keep them shown. [Show me](locate:Image_EditToolAdjust,Image_HistogramShadows,Image_HistogramHighlights)
- **Auto** sets the sliders for the picture in one go. You can still change any of them afterwards. **Reset** puts them all back. [Show me](locate:Image_EditAuto)
- **Light** has exposure, contrast, highlights and shadows. **Colour** has saturation, temperature and tint.
- **AI clean-up** removes softness, noise and JPEG blocks, and repairs faces, on this PC. It stays on until you turn it off. **Strength** sets how much of it you see. The first time, it needs the AI picture models, offered in the bar above the picture. A cleaned-up picture is kept, so coming back to it is instant. [Show me](locate:Image_EditAiEnhance)
- **Change picture** changes the picture as you describe it, like "make it night" or "turn it into a watercolour". In a layered picture it changes the active layer. **Generate** draws the result as a new layer above, so the original is kept. [Show me](locate:Image_GroupAdjustChange,Image_EditChangePrompt,Image_EditChangeGo)

### Depth

**Depth**, in Adjust, works out how far each part of the picture is from the camera, on this PC. The first time, it needs the AI
picture models. After that the picture's depth is kept, so changes here are quick. [Show me](locate:Image_DepthLensBlur)

- **Lens blur** blurs the picture the way a wide-open lens would. Choose how to focus:
  - **Click for depth**: click the picture where it should be sharp. Everything at that distance stays sharp. [Show me](locate:Image_DepthFocusPoint)
  - **From selection**: keeps everything you selected with Select sharp, with a crisp edge, and blurs the rest. [Show me](locate:Image_DepthFocusSelection)
  - **Specify depth**: shows a strip of the picture's distances, near on the left and far on the right. Drag the band's ends to keep more or less sharp, or drag the band to move it. Point at the strip to see what it keeps sharp. [Show me](locate:Image_DepthFocusRange)
- **Aperture** sets how blurred the rest becomes. **Bokeh shape** sets the shape of out-of-focus lights: round or hexagonal. While you drag Aperture or Density, the picture is drawn at lower detail so it keeps up, then drawn sharp when you let go.
- **Haze** adds haze that thickens with distance. Set its **Density**, where it **Starts at**, and its colour: the scene's own, or the **Current colour**, which you choose beside the picture. [Show me](locate:Image_DepthHaze,Image_DepthHazeInk)
- **Depth as layer** adds the depth as a layer of greys, white for near and black for far. In a layered picture, **Depth as mask** gives the active picture layer a mask from it. [Show me](locate:Image_DepthAsLayer)

### Retouch

**Retouch** removes things from the picture, like scratches, dust, wires, people and signs, and fills in where they were.
[Show me](locate:Image_EditToolRetouch)

- It removes what you brush over and anything selected with Select. Right-drag to unbrush. So you can select a person with Select, then brush over their shadow. **Brush size** sets the brush. [Show me](locate:Image_EraseBrushSize)
- Then choose how to fill. **AI** fills from what's around it, and is best where an edge or a shape runs through. The first time it needs the AI picture models. **Content-aware** copies the picture's own texture, and works well on grass, walls, roads, sky and fabric. **Heal** copies texture from where you Alt-click, matched to the light around it. [Show me](locate:Image_EraseFillAi)
- **Erase** applies it as one step of your edits, which you can undo. [Show me](locate:Image_EraseApply)

### Select

**Select** picks out part of the picture. [Show me](locate:Image_EditToolSelect)

- **Object** picks something out on this PC. Click the thing to keep, right-click what to leave out, or drag a box round it. You can also type what it is, like "mug" or "child in a red vest", and click **Find** to select every one; then click to add something it missed, or right-click to drop one it found. The first time, it needs the AI picture models. [Show me](locate:Image_CutOutMarkPick)
- **Rectangle** and **Ellipse** (M) drag out a shape. Hold Shift for a square or circle, and Alt to draw from the centre. **Lasso** (L) draws round something freehand, and **Polygon** goes click by click: double-click or press Enter to close it, and Backspace removes the last point. **By colour** (W) selects colours close to the one you click, within its **Tolerance**. **By distance** selects what lies within a range of distances, and moving the range's ends selects again. None of these need a download. [Show me](locate:Image_SelectRectangle,Image_SelectColour)
- Each shape starts a new selection, or adds to it, subtracts from it or keeps only the overlap. Choose with the mode buttons, or hold Shift, Alt or both as you start. [Show me](locate:Image_RegionModeAdd)
- **All** (Ctrl+A), **None** (Ctrl+D) and **Invert** (Ctrl+Shift+I) work on the whole selection. **Subject** selects the photo's main subject, found from the picture's depth: the nearest thing towards the middle. **Feather**, **Grow**, **Shrink** and **Smooth** change the selection's edge by the pixels in **Refine by**. [Show me](locate:Image_RegionSelectSubject,Image_RegionFeather)
- With something selected you can:
  - **Move** it to float where you drag it. A photo becomes a layered picture for this, and keeps what was moved hidden under a mask. **New layer** copies it into a layer of its own. **Erase** takes it to Retouch. **Generate fill** redraws it from what you describe, as a new layer. [Show me](locate:Image_SelectMove,Image_SelectNewLayer,Image_SelectErase,Image_SelectGenerateFill)
  - **Copy** it without its background, or **Save cut-out** as a transparent PNG. **Remove background** makes everything else transparent, as an edit you can undo with **Keep background**. A JPEG can't be transparent, so it is then saved as a PNG copy. [Show me](locate:Image_CutOutSave,Image_CutOutRemoveBackground)
  - In a layered picture, **Copy**, **Copy merged** (every layer), **Cut**, **Paste**, **Fill** with the current colour (Shift+F5) and **Clear** (Delete) work on the active layer. [Show me](locate:Image_SelectFill)

### Markup

- **Markup** draws arrows, boxes, text, pen lines and highlighter. Pick a tool and drag on the picture. For text, click where it goes and type in the panel. The **Select** tool in Markup picks an annotation to move, restyle or delete with Del. [Show me](locate:Image_EditToolMarkup,Image_EditMarkupArrow,Image_EditMarkupText)
- Annotations always go on a markup layer. The first time you draw, a markup layer is added above the active layer, and a photo becomes a layered picture for it. Annotations a photo already had move onto that layer, staying where they were.

### The current colour

Markup, Paint and Haze share one colour. While one of them is in use, it shows on the left of the canvas with your palette below it.
[Show me](locate:Image_ColourCurrent)

- Click the current colour to open the colour picker over the canvas.
- Click a colour on the palette to use it, and right-click it to remove it. Click **+** to add the current colour to the palette. The palette is the same in every Artist tab. [Show me](locate:Image_ColourAdd)

### Resize and save

- **Resize** sets the size the picture is saved at, as a percentage or in pixels, keeping its proportions unless you say otherwise. [Show me](locate:Image_EditToolResize)
- **AI upscale** makes the picture two or four times larger on this PC, restoring detail rather than stretching it. It also turns AI clean-up on. A picture too large for four times is offered two times only. [Show me](locate:Image_EditUpscaleTwice)
- **Save options**, beside **Save as copy**, choose whether saving removes the camera details, location, dates and descriptions, and set the **Quality** of JPEG and WebP files. The colour profile is always kept. A dot on the button means there is something to know before you save, explained inside. [Show me](locate:Image_EditSaveOptions)
- **Undo** and **Redo** step through everything you did since the picture opened. A slider drag, a crop drag or a pen line counts as one step. **Before** shows the picture as it is on disk. **Revert** removes every edit, and can itself be undone. [Show me](locate:Image_EditUndo,Image_EditRedo,Image_EditCompare,Image_EditRevert)
- **Save** writes the edits into the picture after asking you. The original pixels are replaced, the picture is saved upright, and its camera details are kept unless you chose to remove them. **Save as copy** writes a new file in the format its name asks for, and leaves the original and its edits alone. A picture inside an archive, an animated GIF or WebP, or an icon can only be saved as a copy. [Show me](locate:Image_EditSave,Image_EditSaveCopy)
- Ctrl+Z and Ctrl+Y undo and redo. Ctrl+S saves and Ctrl+Shift+S saves a copy. Closing the tab keeps the edits beside the picture.
- In the File System page, a picture's edits go wherever it goes: copying, moving, renaming and deleting take them along. **Revert edits** appears in an edited picture's actions.
- If another program changes a picture after you edited it, Artist asks whether to keep your edits on the new picture or discard them. A picture inside an archive can be edited, but its edits last only while it is open, so save a copy to keep them.
- Tools that need a model this PC doesn't have yet are greyed out; hover one to see why. While such a tool's panel is open, the bar above the picture offers the AI picture models as one download.

## Layers

- **Add layer** on the toolbar turns a photo into a layered picture: the photo becomes the bottom layer, with its edits, and an empty layer goes above it. **Paste as layer** does the same with the picture on the clipboard. Painting or drawing markup on a photo does this for you. The photo itself is never overwritten; the layered picture is kept separately until you save it. [Show me](locate:Image_EditAddLayer,Image_EditPasteLayer)
- **New → Layered picture (.ora)** in the [File System](help:FileSystem) page makes a blank canvas in the folder and opens it here. Choose its size, background, colour mode (colour or grey) and depth: 8 or 16 bits per channel, or 32-bit for light brighter than white.
- The **Layers** panel beside the tools lists the layers, top first. **Layers** on the toolbar shows or hides it. Click a layer's thumbnail to make it active, and drag its row or use the arrows to move it. The eye hides and shows it. You can name it, set its opacity and blend mode, **Clip** it to the layer beneath, **Keep transparency** so painting stays where it already has pixels, or **Lock** it. **Layer**, **Notes**, **Group**, **Duplicate** and **Delete** are at the top. A group can be **Isolated**, so its layers blend only with each other. [Show me](locate:Image_EditShowLayers,Image_LayersPanel,Image_LayersAddPixel,Image_LayersOpacity,Image_LayersBlend)
- Crop, Adjust, Retouch, Select and Resize work on the active picture layer. On a pixel layer, **Erase** in Retouch and **Remove background** in Select change the pixels themselves.
- A **Mask** hides part of a layer without erasing it. You can reveal or hide all of it, make it from the selection, from the layer's transparency, or from what Select picked, invert it, apply it, or delete it. Click the mask's thumbnail to paint and fill the mask. Alt-click shows the mask alone, Ctrl-click loads it as the selection, and Shift-click turns it off. [Show me](locate:Image_LayersMaskSection)
- **Generate layer** draws a new layer above the active one with the workspace's image model. Pictures dropped on the canvas become layers too. [Show me](locate:Image_LayersGenerate)
- Every generated edit — **Generate fill**, **Expand**, **Change picture**, and a canvas grown with **Generate the new area** — shows in the Layers panel while it is made, with **Stop**, and arrives as a new layer above the active one. [Show me](locate:Image_LayersPictures)
- **Paint** paints on the active pixel layer, or on its mask while you edit the mask, and only inside the selection if there is one. If the active layer can't take paint, or the picture is a photo, painting adds a new pixel layer first. The **Brush** (B) paints in the current colour, with its size, hardness, opacity and flow. The **Eraser** (E) removes paint, and on a mask shows the layer again. The **Eyedropper** (I, or Alt with the brush) picks a colour from the picture, and **Fill** (G) fills colours close to the one you click. Each stroke is one undo step. Whether pen pressure sets the brush's size and opacity, and how closely the brush is spaced, are in the Images options. [Show me](locate:Image_EditToolPaint,Image_PaintBrush)
- **Move** (V) drags the active layer, or what's selected on a pixel layer, which floats until you **Drop** it (or press Enter). To move part of a photo, select it and click **Move** in Select. [Show me](locate:Image_ArrangeMoving) **Transform** (Ctrl+T) puts a frame round the layer or selection: drag a side or corner to scale (Shift keeps proportions), the round handle to rotate (Shift snaps to 15°), or inside to move. **Flip** and **Turn** are in the panel. **Apply** (Enter) keeps it, **Cancel** (Esc) puts it back. Photos and notes layers are placed again rather than resampled, so they never get softer. [Show me](locate:Image_EditToolMove,Image_ArrangeTransform)
- **Rasterize** turns notes or a picture layer into pixels, **Merge down** joins a layer with the one beneath, and **Flatten** makes one layer of them all.
- **Canvas** sets a new canvas size, anchored at any of nine points, or crops the canvas to the selection. With **Generate the new area** on, the workspace's picture-editing model draws what a larger canvas gains. Canvas also converts the picture to another colour mode or depth. A 32-bit picture is shown at a **view exposure** you set there, and Paint's **Intensity** paints brighter than white. [Show me](locate:Image_LayersCanvasSection)
- Undo and redo step through layers and selections too. How much history a layered picture keeps is set in the Images options.
- **Save** keeps a layered picture as OpenRaster or Photoshop. The first time, it asks where, starting beside the photo it came from. OpenRaster holds 8 and 16 bits; a 32-bit picture is saved as Photoshop. **Save as copy** exports it flattened, as a PNG or another format, at 16 bits where PNG and TIFF allow.

## OpenRaster files

- Double-click an .ora file — from Krita, GIMP, MyPaint, Pinta, or saved here — to open it in Artist. Its layers come back with their opacity, blend modes, visibility, locks, masks and groups, and those programs can open what Nexaflow saves.
- If a file holds a layer Nexaflow can't draw, such as a Krita filter layer, it opens on the look the other program saved, with a bar naming the layer. **Use the saved look** replaces the layers with one showing that look. **Edit the layers** shows what Nexaflow can draw, and keeps the other layer to save it back unchanged. Editing anything first chooses Edit the layers. [Show me](locate:Image_SavedLookUse,Image_SavedLookEditLayers)
- In the File System page, an .ora file also opens as a folder, like any package.

## Photoshop files

- Double-click a .psd or .psb file — RGB or grayscale, at 8, 16 or 32 bits — to open it on its layers: pixels, groups, opacity and fill, blend modes, visibility, locks and masks. A layer clipped to the one beneath stays clipped. Editing saves it back as Photoshop.
- Text, smart objects and shapes open as the pixels Photoshop drew for them. If you leave them alone they are saved back as they were; if you paint on them they become pixels.
- Layer effects, vector masks and adjustment layers are kept and saved back, but not drawn here. A file with any of them opens on the look Photoshop saved, with the same choice as for OpenRaster files.
- CMYK, Lab, indexed and other print modes don't open. Convert the file to RGB or grayscale in Photoshop first.

## Printing

- **Print** (Ctrl+P) at the top left prints the picture as edited, or a layered picture as it shows. You can fit it to the page, fill the page, print at actual size, at its resolution, or at a width in millimetres. A picture larger than the page runs across several pages. Choose portrait or landscape, the margins, and whether to centre it, then click **Print…** to choose the printer. The picture is printed at full size, never from the screen. [Show me](locate:Image_Print)

## Drafts and the assistant

- A picture made for you — drawn by the assistant, cut out, or a frame taken from a video — opens here as a draft, with a strip saying it isn't saved yet. **Save as** keeps it, with any edits, and the tab becomes that file. A draft you never save is kept only until Windows clears its temporary files. [Show me](locate:Draft_SaveAs)
- The assistant can see the picture as edited, and works on this picture only.
- Ask it to select something — "select the dog", "find the woman with red hair" — and Select opens with everything it found selected. Ask it to cut something out, and the cut-out opens in its own Artist tab as a draft.
- Ask it to clean up or enlarge the picture, or to remove something — "remove the sign", "get rid of the people in the background" — and it does so as one of your edits, which you can undo.
- If the workspace has a picture-editing model, ask it to fill the selection, extend the picture, or change it — "put a lamp on the table", "make it look like autumn" — and it adds a new layer, as Generate fill, Expand and Change picture do.