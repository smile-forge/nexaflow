# DICOM viewer

Opens a DICOM study, scrolls through its images, measures them and lists their tags. It only reads, so nothing on the disc changes.

---

## Opening a study

- Choose *As DICOM* on the [File System](help:FileSystem) page for a single `.dcm`, a selection of files, a folder of loose instances or a DICOMDIR. It all opens as one study in one tab, and the breadcrumb leads back to where the study came from.
- It is offered on any folder holding a DICOMDIR, a `.dcm` or a `.dicom`, including a drive root. Files with no extension, such as `IM_0001`, are recognised by the DICOM marker inside the file rather than by the name.
- A zipped study reads the same as one on disk, and compressed images decode through codecs installed up front. An instance that will not parse is recorded as a warning instead of failing the load, and a DICOMDIR that is damaged or lists nothing falls back to scanning the medium.
- Open a PDF report stored with the study: select it in the tree beside the images. It opens in its own tab with a breadcrumb back to this study, and the extracted copy is deleted when you close the study.

## Moving through the images

- Jump to an image, series, study or patient: click it in [the Contents tree](locate:Dicom_ContentTree), which orders slices by instance number and shows a multi-frame image's frame count. Clicking a patient, study or series goes to its first image.
- Scroll a series with the mouse wheel, one slice per notch, stopping at the first and the last. The tree keeps the current slice in view.
- Zoom with **Ctrl** and the wheel; the point under the cursor stays under the cursor. Pan by dragging with [the Pan tool](locate:Dicom_Tool_Pan), which is selected to begin with, or with *Probe* armed.
- Size the image to the stage or show its true pixels: [Fit or 1:1](locate:Dicom_Fit,Dicom_ActualSize).
- A multi-frame image gets a transport bar: play the sequence as a loop, step a frame either way, or scrub with the slider. It pauses while you are in another tab or the window is minimised, and resumes when you return.

## Brightness and contrast

- Set the window and level by hand: hold the right mouse button and drag over the image — sideways for width, up and down for level.
- Apply a preset: [**Bone**, **Lung**, **Soft**, **Brain** or **Default**](locate:Dicom_Preset_Bone,Dicom_Preset_Lung,Dicom_Preset_Soft,Dicom_Preset_Brain,Dicom_Preset_Default), where Default restores the window the image itself declared. They are CT windows in Hounsfield units, and can still be used on any greyscale image. The active preset stops being lit once you drag your own window.
- Show a photographic negative of the frame: [**Invert**](locate:Dicom_Invert). Window, level and invert hold as you scroll a series, and reset when you move to another.

## Measuring

- Pick one of [the measuring tools](locate:Dicom_Tool_Length,Dicom_Tool_Angle,Dicom_Tool_Rect,Dicom_Tool_Ellipse,Dicom_Tool_Probe) and click on the image. A preview follows the cursor until the shape has all its points.
- **Length** takes two clicks — in millimetres when the image records its pixel spacing, and in pixels, labelled as such, when it does not.
- **Angle** takes three clicks, two arms and the vertex, and reads in degrees.
- **Rectangle** and **ellipse** take two opposite corners and give the area, plus the mean and standard deviation inside where the stored pixel values can be read — in Hounsfield units on CT.
- **Probe** is a hover readout rather than an annotation. It reports the stored value and its coordinates, in HU on CT, not the brightness on screen.
- Annotations belong to their frame and are still there when you come back. **Clear** empties only the frame in front of you. Points are held in image coordinates, so an annotation stays on its own pixels however far you zoom.

## Tags and hiding patient details

- Open the tag list for the selected instance: [the Tags drawer](locate:Dicom_TagsToggle,Dicom_TagFilter). Sequences are summarised by their item count, and a tag the dictionary does not know is labelled *Private / Unknown*.
- Filter as you type by tag, name or value: `0010`, `Modality` or a fragment of a value all find their row.
- Copy a value, a name, a tag or a whole row from the right-click menu. A row copies tab-separated.
- Widen the drawer by dragging the splitter. It keeps that width when you close and reopen it.
- Take identifying details off the screen: [Hide patient](locate:Dicom_HidePatient). The tree shows *Patient 1* in place of the name and ID, the patient line leaves the image, and a fixed set of tags — name, ID, birth date, sex, age, address, telephone, accession number, referring and performing physicians, operator, institution and patient comments — reads *••• (hidden)* in the drawer.
- It is a screen mask and only that: the files are untouched, the rest of the header is shown as it stands, and nothing here anonymises a study.

## The assistant

- It knows the modality, series, body part, size, frame and window of the image on screen. It can list the series, open a particular image, step through frames, read the tags, and capture the frame at the window you have set.
- Whatever *Hide patient* is set to, the study the assistant sees is always the masked one: what it is told carries no patient details, and the tags it reads come back with the same identifying set hidden.
- Nothing the assistant does here changes a file.
