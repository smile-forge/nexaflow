# Images

Opens one picture, a selection, or every picture in a folder.

---

## Opening pictures

- Double-click a picture in the [File System](help:FileSystem) page. PNG, JPEG, GIF, BMP, ICO, TIFF and WebP open here; an OpenRaster file (.ora) opens in [Artist](help:Artist), and **As Image** shows it here as the picture saved inside it.
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

## Details

- **Details** at the top left, or I, opens a panel beside the picture with what its file says about it: its name, type, size and dimensions; the camera and lens; the exposure — aperture, shutter speed, ISO, focal length and flash; when it was taken, by the camera's clock; where, from its GPS position; its description, author and copyright; and its colour profile. [Show me](locate:Image_Details)
- It follows the picture you are on, in every view, and shows only what the file holds: a screenshot, or a picture saved without its details, has its file and nothing more. An edited picture also says the size it will be saved at.
- What it shows is what **Save** keeps, unless you choose in the save options to remove it.

## Slideshow and full screen

- Advance on a timer: turn on **Auto**. It loops back to the first picture at the end and runs in whichever view you are in. [Show me](locate:Image_AutoAdvance,Image_AutoSpeed)
- Set the speed with the slider: slow, medium or fast — a picture every ten seconds, four seconds, or one. A change takes effect straight away.
- The slideshow stops when you move to another tab and resumes when you come back.
- Fill the display: [View full screen](locate:Image_FullScreen). Move forward with Space, the right or down arrow, or a click; back with the left or up arrow, or a right-click. The wheel steps too.
- Esc returns to the tab on the picture you stopped at and stops the slideshow. A slideshow started before you went full screen keeps running, and every picture is shown with its edits.

## Rotating, flipping, scaling and the collage

- Rotate in ninety-degree steps, flip across or upside down, or switch between fit and actual size, from the toolbar floating over the picture in the carousel and in Explore. At actual size the picture scrolls. [Show me](locate:Image_RotateLeft,Image_RotateRight,Image_FlipHorizontal,Image_FlipVertical,Image_FitMode)
- A turn or a flip is an edit: it is remembered with the picture and undone with Ctrl+Z, like everything done in Artist. Fit and actual size change only the view.
- In the collage, drag anywhere to pan and use the wheel to zoom about the pointer. The chips in the corner offer minus, plus, **Fit** and **1:1**.
- An overview appears in the bottom right once the pile runs past the edges; drag inside it to move the view. Collage positions are kept when you leave and come back. Double-click a picture to open it in the carousel.

## Editing

- **Edit** at the top right, or E, opens the picture you are on in [Artist](help:Artist), the image editor, in a tab of its own; the set stays here as it was. [Show me](locate:Image_Edit)
- Edits never touch the file until it is saved. They are kept beside the picture in a hidden file named after it, and every view here shows the picture with them applied — the carousel, the thumbnails, full screen, and what the assistant is shown. A picture with edits is marked *edited* beside its name.
- In the File System page a picture's edits go wherever it goes: copying, moving, renaming and deleting it take them along, a copy that is renamed to avoid a clash keeps its own, and dragging it to another program brings them too. **Revert edits** appears among the actions of an edited picture.
- If a picture is changed by another program after it was edited, the viewer asks whether to keep the edits on the new picture or discard them.

## Printing

- **Print** (Ctrl+P) on the toolbar prints the picture as edited. Fit it to the page, fill the page — cropping what overflows — print it at actual size, at its resolution, or at a width in millimetres; a picture larger than the page runs across pages. The page turns to suit the picture unless you choose portrait or landscape; set the margins and whether it sits in the middle. **Print…** then chooses the printer. The picture is drawn at full size for the printer, never from what is on screen. [Show me](locate:Image_Print)

## Converting a page to Markdown

- A picture of a page of writing — a scan, a photographed page, a form, a receipt — can be converted to Markdown. **Convert to Markdown** sits at the top right whenever the picture is one; until page reading is on this PC every picture offers it, with the download. [Show me](locate:Image_ConvertToMarkdown)
- A tinted line above the picture says how far the reading has got. **Stop** asks first: **Show what's converted** opens what has been read so far, **Discard** lets it go, and **Keep converting** carries on. Once it is read, the Markdown opens in the Markdown editor in the right-hand pane: columns as one text, headings, formulas as LaTeX, tables, and the page's figures cut out of it. A figure the workspace's image model can draw again as a diagram comes back as one.
- It reads the picture as you see it, edits and all, at its full size: crop to the page or straighten it first and it reads better. The reading is kept, so converting the same picture again opens at once.
- The Markdown is a draft to edit as you like. Its first save asks where to keep it — a Markdown Package, with its figures inside it, unless you pick a Markdown file.
- Page reading runs on this PC. The first time it is a download of about 4 GB, offered above the picture. [Show me](locate:Image_ConvertToMarkdown,PageConversion_Download)

## Deleting and the assistant

- Delete a picture: right-click it or its thumbnail and choose Delete. It asks first, sends the file and its edits to the Recycle Bin and removes it from every view; the selection moves to what is left, and a tab whose last picture goes says so.
- The assistant can look at the picture you are on, as edited, and answer questions about what is in it. It can report the format, dimensions and file size, and step to the next or previous picture.
- The assistant works on the picture you are on, and nothing else: a picture is its to see or change only once you have it open.
- Changing a picture with the assistant — picking things out, cutting them out, cleaning it up, erasing from it — happens in [Artist](help:Artist): open the picture there with **Edit**.
- Ask the assistant to read a page — "what does this letter say", "type up this receipt" — and it reads the picture with page reading, its text opening beside it.
- In a conversation, this tab's context chip previews every image it holds.
