"""
SAM 3.1 as the makers build it, and the networks each graph is traced from.

The makers' still-picture paths, run through their own modules and functions:

  clicks  the tri-neck's interactive head -> the interactive decoder's conv_s0 / conv_s1 -> `_get_interactive_pix_mem`
          (+ the no-memory embedding) -> `_forward_sam_heads` (prompt encoder + interactive mask decoder; three masks
          for one point, the one for several; the best by predicted IoU; the object score) -> bilinear to the picture,
          as a first conditioning frame with clicks goes in their video predictor
  text    `Sam3Processor`: `forward_image` -> `forward_text` -> `forward_grounding` -> scores joined with the presence
          score, kept over 0.5, masks bilinear to the picture

Both take the picture as SAM 3.1's predictor prepares a frame: one encoder pass serves both.

`load()` builds the makers' SAM 3.1 predictor from the checkpoint, then undoes what it does for speed on the makers'
own GPUs, so a reference is the network's own fp32 arithmetic: it leaves the bf16 autocast the predictor and its
tracker each enter for the whole process (in the reverse of the order they entered — each exit restores the state its
enter found), unfuses the trunk's fc1 + GELU (which the makers always run in bf16), and turns TF32 off.
"""
import torch
import torch.nn as nn
import torch.nn.functional as F
import torchvision.transforms.functional as TF

SIZE = 1008


class Sam:
    """The makers' SAM 3.1 pieces a still picture goes through."""

    def __init__(self, checkpoint: str):
        from sam3.model_builder import build_sam3_multiplex_video_predictor
        import sam3.model.vitdet as vitdet

        self.predictor = build_sam3_multiplex_video_predictor(checkpoint_path=checkpoint, use_fa3=False, use_rope_real=True,
                                                              async_loading_frames=False)
        self.predictor.bf16_context.__exit__(None, None, None)
        self.predictor.model.tracker.bf16_context.__exit__(None, None, None)
        assert not torch.is_autocast_enabled("cuda")
        vitdet.addmm_act = lambda act, linear, x: act()(linear(x))
        torch.backends.cuda.matmul.allow_tf32 = False
        torch.backends.cudnn.allow_tf32 = False

        self.detector = self.predictor.model.detector
        self.tracker = self.predictor.model.tracker.model
        self.neck = self.detector.backbone.vision_backbone
        if not hasattr(self.detector, "inst_interactive_predictor"):
            self.detector.inst_interactive_predictor = None  # what Sam3Processor asks of an image model
        from sam3.model.sam3_image_processor import Sam3Processor
        self.processor = Sam3Processor(self.detector, confidence_threshold=0.5)
        self.tokenizer = self.detector.backbone.language_backbone.tokenizer

    # ── the makers' paths ───────────────────────────────────────────────────────────────────────────────────────────

    @staticmethod
    def normalised(picture):
        """A picture as SAM 3.1's own predictor prepares a frame for both its paths: squashed to 1008 square by PIL's
        bilinear resize (its loader's `TF.resize` of a PIL picture), RGB from -1 to 1."""
        x = TF.to_tensor(TF.resize(picture.convert("RGB"), size=(SIZE, SIZE)))
        return ((x - 0.5) / 0.5).unsqueeze(0).cuda()

    @torch.inference_mode()
    def click_features(self, picture):
        out = self.detector.backbone.forward_image(self._nested(self.normalised(picture)), need_sam3_out=False,
                                                   need_interactive_out=True, need_propagation_out=False)["interactive"]
        dec = self.tracker.interactive_sam_mask_decoder
        fpn = [f.tensors for f in out["backbone_fpn"]]
        fpn = [dec.conv_s0(fpn[0]), dec.conv_s1(fpn[1]), fpn[2]]
        feats = [f.flatten(2).permute(2, 0, 1) for f in fpn]
        sizes = [(f.shape[-2], f.shape[-1]) for f in fpn]
        high = [f.permute(1, 2, 0).view(f.size(1), f.size(2), *s) for f, s in zip(feats[:-1], sizes[:-1])]
        return self.tracker._get_interactive_pix_mem(feats, sizes), high

    @torch.inference_mode()
    def click(self, features, points, labels, width, height):
        """The makers' mask for clicks at picture pixels: logits at the picture's size, the IoUs, the object score."""
        pix, high = features
        pts = torch.tensor([[[x * SIZE / width, y * SIZE / height] for x, y in points]], dtype=torch.float32, device="cuda")
        point_inputs = {"point_coords": pts, "point_labels": torch.tensor([labels], dtype=torch.int32, device="cuda")}
        state = self.tracker.multiplex_controller.get_state(num_valid_entries=1, device="cuda", dtype=torch.float32,
                                                            random=False, object_ids=[1])
        out = self.tracker._forward_sam_heads(backbone_features=pix, point_inputs=point_inputs, mask_inputs=None,
                                              interactive_high_res_features=high,
                                              multimask_output=self.tracker._use_multimask(True, point_inputs),
                                              objects_to_interact=[0], multiplex_state=state)
        mask = F.interpolate(out["high_res_masks"].float(), size=(height, width), mode="bilinear", align_corners=False)
        return mask[0, 0], out["ious"][0].float(), float(out["object_score_logits"])

    @torch.inference_mode()
    def find(self, picture, phrase, boxes=(), labels=()):
        """The makers' Sam3Processor: scores, boxes (x0 y0 x1 y1 in picture pixels) and masks of what is found — the
        picture prepared as SAM 3.1's predictor prepares a frame (`normalised`), where the processor would resize an
        8-bit tensor with torchvision instead, which moves scores by up to 0.17."""
        state = {"original_width": picture.width, "original_height": picture.height,
                 "backbone_out": self.detector.backbone.forward_image(self.normalised(picture))}
        found = self.processor.set_text_prompt(phrase, state)
        for box, label in zip(boxes, labels):
            found = self.processor.add_geometric_prompt(list(box), bool(label), state)
        return found["scores"].float(), found["boxes"].float(), found["masks"][:, 0]

    def tokens(self, phrase):
        return self.tokenizer([phrase], context_length=32)

    @staticmethod
    def _nested(x):
        from sam3.model.data_misc import NestedTensor
        return NestedTensor(tensors=x, mask=None)


# ── what each graph is traced from ──────────────────────────────────────────────────────────────────────────────────

class ImageEncoder(nn.Module):
    """The picture once, through the trunk: the interactive head (for clicks) and the sam3 head (for text), each as its
    path reads it — the interactive head through the interactive decoder's high-resolution convs and with the
    no-memory embedding added, the sam3 head as its three feature maps."""

    def __init__(self, sam: Sam):
        super().__init__()
        self.neck = sam.neck
        self.conv_s0 = sam.tracker.interactive_sam_mask_decoder.conv_s0
        self.conv_s1 = sam.tracker.interactive_sam_mask_decoder.conv_s1
        self.no_mem = sam.tracker.interactivity_no_mem_embed

    def forward(self, image):
        s3, _, inter, _, _, _ = self.neck(image, need_sam3_out=True, need_interactive_out=True, need_propagation_out=False)
        f = [x.tensors for x in inter]
        g = [x.tensors for x in s3]
        return f[2] + self.no_mem.reshape(1, -1, 1, 1), self.conv_s0(f[0]), self.conv_s1(f[1]), g[0], g[1], g[2]


class ClickDecoder(nn.Module):
    """The interactive prompt encoder and mask decoder: all four masks (0 for several points, 1-3 for one), their IoUs
    and the object score — choosing among them is the caller's, as `_forward_sam_heads` chooses."""

    def __init__(self, sam: Sam):
        super().__init__()
        self.pe = sam.tracker.interactive_sam_prompt_encoder
        self.dec = sam.tracker.interactive_sam_mask_decoder

    def forward(self, embedding, high0, high1, points, labels):
        sparse, dense = self.pe(points=(points, labels), boxes=None, masks=None)
        masks, ious, _, obj = self.dec.predict_masks(image_embeddings=embedding, image_pe=self.pe.get_dense_pe(),
                                                     sparse_prompt_embeddings=sparse, dense_prompt_embeddings=dense,
                                                     repeat_image=False, high_res_features=[high0, high1])
        return masks, ious, obj


class TextEncoder(nn.Module):
    """The language backbone: a phrase's tokens to the features the finder reads, and which of them are padding."""

    def __init__(self, sam: Sam):
        super().__init__()
        self.te = sam.detector.backbone.language_backbone

    def forward(self, tokens):
        _, memory = self.te.encoder(tokens)
        return self.te.resizer(memory.transpose(0, 1)), tokens == 0


class ExemplarEncoder(nn.Module):
    """Example boxes — "like this" or "not like this" — as the geometry encoder's box tokens: their own projection,
    the picture pooled under each, their position and their label (`_encode_boxes`)."""

    def __init__(self, sam: Sam):
        super().__init__()
        self.geo = sam.detector.geometry_encoder

    def forward(self, fpn2, boxes, box_labels):
        n, c, h, w = fpn2.shape
        feats = self.geo.img_pre_norm(fpn2.flatten(2).permute(2, 0, 1)).permute(1, 2, 0).reshape(n, c, h, w)
        pad = torch.zeros(1, boxes.shape[0], dtype=torch.bool, device=boxes.device)
        tokens, _ = self.geo._encode_boxes(boxes=boxes, boxes_mask=pad, boxes_labels=box_labels, img_feats=feats)
        return tokens


class Finder(nn.Module):
    """`forward_grounding` from the sam3 head, a phrase's features and the example boxes' tokens: every query's score
    (joined with the presence score as the makers join it), its box and its mask.

    The geometry encoder's own forward builds its sequence by concatenating padded, possibly empty, sequences of points
    and boxes; with no points and nothing padded that is a plain concatenation, so this runs the rest of it — the CLS
    token, the projection and norm, the layers over the picture — on the box tokens as they come."""

    def __init__(self, sam: Sam):
        super().__init__()
        self.det = sam.detector
        self.geo = sam.detector.geometry_encoder
        self.pos = sam.neck.position_encoding
        self.find_stage = sam.processor.find_stage

    def geometry(self, box_tokens, img_seq, img_pos):
        x = torch.cat([box_tokens, self.geo.cls_embed.weight.view(1, 1, -1).to(box_tokens.dtype)], dim=0)
        pad = torch.zeros(1, x.shape[0], dtype=torch.bool, device=x.device)
        x = self.geo.norm(self.geo.final_proj(x))
        for layer in self.geo.encode:
            x = layer(tgt=x, memory=img_seq, tgt_key_padding_mask=pad, pos=img_pos)
        return self.geo.encode_norm(x), pad

    def forward(self, fpn0, fpn1, fpn2, text, text_pad, box_tokens):
        from sam3.model.data_misc import NestedTensor
        fpn = [fpn0, fpn1, fpn2]
        pos = [self.pos(x).to(x.dtype) for x in fpn]
        backbone_out = {"backbone_fpn": [NestedTensor(x, None) for x in fpn], "vision_pos_enc": pos,
                        "vision_features": fpn2, "language_features": text, "language_mask": text_pad}
        geo, geo_pad = self.geometry(box_tokens, fpn2.flatten(2).permute(2, 0, 1), pos[2].flatten(2).permute(2, 0, 1))
        prompt = torch.cat([text, geo], dim=0)
        prompt_mask = torch.cat([text_pad, geo_pad], dim=1)
        backbone_out, encoder_out, _ = self.det._run_encoder(backbone_out, self.find_stage, prompt, prompt_mask)
        out = {"encoder_hidden_states": encoder_out["encoder_hidden_states"]}
        out, hs = self.det._run_decoder(memory=out["encoder_hidden_states"], pos_embed=encoder_out["pos_embed"],
                                        src_mask=encoder_out["padding_mask"], out=out, prompt=prompt,
                                        prompt_mask=prompt_mask, encoder_out=encoder_out)
        self.det._run_segmentation_heads(out=out, backbone_out=backbone_out, img_ids=self.find_stage.img_ids,
                                         vis_feat_sizes=encoder_out["vis_feat_sizes"],
                                         encoder_hidden_states=out["encoder_hidden_states"], prompt=prompt,
                                         prompt_mask=prompt_mask, hs=hs)
        return out["pred_logits"][..., 0], out["presence_logit_dec"], out["pred_boxes"], out["pred_masks"]


# ── the cases both the build's check and the reference run ──────────────────────────────────────────────────────────

# (picture, [(name, points, labels)], [(name, phrase, example boxes as cx cy w h of 0..1, their labels)])
CASES = {
    "truck.jpg": (
        [("window", [(520, 375)], [1]), ("two", [(500, 375), (1125, 625)], [1, 1]),
         ("minus", [(500, 375), (1125, 625), (575, 750)], [1, 1, 0]), ("box", [(75, 275), (1725, 850)], [2, 3]),
         ("wheel", [(1200, 600)], [1])],
        [("truck", "truck", [], []), ("wheel", "wheel", [], []), ("window", "window", [], []),
         ("handle", "door handle", [], []), ("window-like", "window", [(0.28, 0.33, 0.14, 0.12)], [1])]),
    "groceries.jpg": (
        [("bag", [(430, 250)], [1]), ("left", [(250, 270)], [1]), ("light", [(660, 120)], [1])],
        [("bag", "paper bag", [], []), ("light", "tail light", [], []), ("car", "car", [], []),
         ("bag-unlike", "paper bag", [(0.55, 0.45, 0.1, 0.25), (0.7, 0.45, 0.08, 0.25)], [1, 0])]),
    "test_image.jpg": (
        [("child", [(420, 420)], [1]), ("hoop", [(275, 175)], [1]), ("vest", [(1040, 420), (1030, 520)], [1, 0])],
        [("red", "child in a red vest", [], []), ("shoe", "shoe", [], []), ("hoop", "basketball hoop", [], []),
         ("person", "person", [], [])]),
}

# Phrases whose tokens the reference keeps, to hold a port of the makers' tokenizer to: its cleaning (ftfy, HTML
# unescaped twice, whitespace collapsed, lower case) as much as its byte-pair merges.
PHRASES = ["wheel", "a red-haired woman in a yellow raincoat", "Door Handle", "the man’s hat!", "café crème",
           "  lots   of   spaces ", "HTML &amp; things", "&amp;amp; twice", "2 dogs, 3 cats", "Wally", "emoji 🙂 here",
           "naïve résumé", "ＦＵＬＬwidth", "“quoted” — dash", "ﬁne ligature", "tab\tand\nnewline",
           "a very long phrase that goes on and on past the thirty two tokens the text encoder reads at most, "
           "so that it is cut and ends on the end token"]
