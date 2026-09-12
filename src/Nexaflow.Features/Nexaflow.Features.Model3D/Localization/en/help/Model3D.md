# 3D Model viewer

Opens a 3D model file and lets you turn it, and says what the file holds.

---

## Opening a model

- Double-click a model in the [File System](help:FileSystem) page. STL, OBJ, glTF, FBX, 3MF, Collada and the rest are mapped here; **As 3D Model** is in the file menu too.
- Mesh formats STL, OBJ, 3DS and PLY open, and an OBJ picks up the companion .mtl beside it.
- glTF and GLB open with the whole scene graph evaluated — every node transform applied, every instanced part repeated — and the triangles grouped one mesh per material.
- FBX, 3MF, AMF, Collada, Blender, DirectX, X3D, DXF, LightWave and some fifty extensions in all open too. Faces are triangulated and given smooth normals where the file has none.
- A model inside an archive or a disk image opens like one on disk.
- The file is read in the background, so the rest of Nexaflow stays responsive.
- Each model opens in [a tab of its own](locate:TabItem_Model3D), named after the file. A multi-file selection opens the first.

## Moving the camera

- Rotate, zoom and pan with the mouse. A view cube and a coordinate gizmo show the orientation.
- Alt + right-drag spins the model about the green axis.
- The model opens framed to fill the stage. A glTF that carries a camera opens from that viewpoint instead.
- [Reset view](locate:Model3D_ResetView) returns to the opening view — the authored one, or the opening framing.

## Wireframe

[The Wireframe switch](locate:Model3D_Wireframe) draws the model as edges only, every triangle where it sits in the scene. The edges are built the first time you ask and kept after that.

Try it: [go wireframe, orbit into the model, then jump home](locate:Model3D_Wireframe,Model3D_ResetView).

## Colours

- Where a file leaves its untextured materials uncoloured, or gives them all the same grey, each one is painted a distinct colour from your theme's swatch palette.
- Where every untextured material already carries a colour of its own, the file's colours are kept as authored.
- A textured material is never re-tinted. Only OBJ files draw their textures; a glTF or FBX texture is shown as its base colour, with the image named in the inspector.

## What the file holds

- The footer names the format and counts triangles, vertices and meshes, beside the file size.
- The inspector lists every material — its name where the file gives one, a colour swatch, the hex value and any texture it names. It opens by itself when there is something to show, and [the Inspector switch](locate:Model3D_InspectorToggle) hides it.
- Animations, cameras, lights, skeletal rigs and full PBR texturing are counted and listed but not drawn. A rigged character is shown in its bind pose, and says so.
- A file that cannot be opened, or that parses but holds no mesh, says so in the middle of the stage.

## With the assistant

Ask in [the AI bar](locate:AiInputBox).

- It knows the model's name and format, its triangle, vertex, mesh and material counts, whether it is solid or wireframe, and what is not rendered.
- It can take a picture of the stage, move the camera — orbit, roll, zoom, pan, reset — and switch wireframe on or off, so it can turn the model over to answer a question about the underside or the back.
- It can give the exact counts, the full material list and everything not rendered.
- None of these touch the file, so none of them needs your approval.
