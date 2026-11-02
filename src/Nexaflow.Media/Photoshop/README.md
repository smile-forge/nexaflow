# Photoshop

Nexaflow's own reader and writer for Photoshop documents — PSD, and PSB, the large document format — written from Adobe's
*Photoshop File Formats Specification*. No code is carried from elsewhere, so no licence beyond the repository's applies.

It reads and writes structure only: the header, colour-mode data, image resources, layer records with their masks and
tagged blocks, the file's own tagged blocks, and the merged image. Channel samples stay in the file until they are read,
row by row, through `PhotoshopChannels`, which handles raw, RLE (PackBits), ZIP and ZIP-with-prediction data at 8, 16 and
32 bits a channel. What a layer means — its pixels as tiles, its blend, its group, its clipping — is the Images feature's
`PhotoshopFormat`.
