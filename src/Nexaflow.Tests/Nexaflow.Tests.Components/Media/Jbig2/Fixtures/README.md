# JBIG2 test files

JBIG2 files made by [SerenityOS](https://github.com/SerenityOS/serenity)'s own encoder, from
`Tests/LibGfx/test-inputs/jbig2/` at commit 6dfb6940a682df4cc734ee3ef7f3be9b030ab3b0, each exercising one coding path.
Every one codes the same 399 x 400 picture, SerenityOS's `Tests/LibGfx/test-inputs/bmp/bitmap.bmp`, which
`expected.pbm` holds as a PBM, 1 for black. `Jbig2DecoderTests` decodes each and holds it to that picture.

Three of SerenityOS's files are not here: `annex-h.jbig2`, the ITU's own example, whose licence allows only
non-commercial use; and `bitmap-symbol-manysyms.jbig2` and `bitmap-symbol-symhuff-texthuff-manysyms.jbig2`, which are
large and cover nothing the others do not.

BSD 2-Clause: see [LICENSE](LICENSE).
