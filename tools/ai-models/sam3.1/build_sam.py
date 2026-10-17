"""
Traces SAM 3.1's still-picture paths into five ONNX graphs and holds them to the makers' own, on the makers' own sample
pictures:

  image-encoder     fp16   image [1,3,1008,1008] (RGB, -1..1) -> embedding [1,256,72,72], high0 [1,32,288,288],
                           high1 [1,64,144,144] (the interactive head), fpn0 [1,256,288,288], fpn1 [1,256,144,144],
                           fpn2 [1,256,72,72] (the sam3 head)
  click-decoder     fp32   embedding, high0, high1, points [1,N,2] (1008-square pixels), labels [1,N] (1 in, 0 out,
                           2 and 3 a box's corners) -> masks [1,4,288,288] logits, ious [1,4], object [1,1]
  text-encoder      fp16   tokens [1,32] -> text [32,1,256], text_pad [1,32]
  exemplar-encoder  fp32   fpn2, boxes [B,1,4] (cx cy w h, 0..1), box_labels [B,1] (1 like, 0 unlike) -> box_tokens [B,1,256]
  finder            fp16   fpn0, fpn1, fpn2, text, text_pad, box_tokens [B,1,256] (B may be 0) -> logits [1,200],
                           presence [1,1], boxes [1,200,4] (cx cy w h, 0..1), masks [1,200,288,288] logits

The graphs that hold most of the weights or the memory are half precision: the trunk's attention over 5184 tokens
peaks at 12.9 GB in fp32 and 2.5 GB in fp16, and the makers themselves run SAM 3.1 in bf16. The image and text encoders
are converted outright; the finder runs under fp16 autocast — the makers' low-precision path — because its decoder
makes fp32 sine embeddings as it goes.

usage: build_sam.py <checkpoint> <pictures folder> <out folder>
"""
import copy, os, sys, time
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
import torch
import torch.nn as nn
import torch.nn.functional as F
from onnx import TensorProto, helper
from PIL import Image

from sam_graphs import CASES, SIZE, ClickDecoder, ExemplarEncoder, Finder, ImageEncoder, Sam, TextEncoder

CUDA = [("CUDAExecutionProvider", {"use_tf32": 0}), "CPUExecutionProvider"]


class Half(nn.Module):
    """A module's parameters and buffers converted to fp16; fp32 in and out. What it holds as plain tensors — the
    trunk's rotary frequencies among them — stays fp32, as their precision needs."""

    def __init__(self, module):
        super().__init__()
        self.m = copy.deepcopy(module).half()

    def forward(self, *xs):
        out = self.m(*(x.half() if x.is_floating_point() else x for x in xs))
        return tuple(t.float() if t.is_floating_point() else t for t in out)


class Autocast(nn.Module):
    """A module under fp16 autocast over its fp32 weights; fp32 out."""

    def __init__(self, module):
        super().__init__()
        self.m = module

    def forward(self, *xs):
        with torch.autocast("cuda", dtype=torch.float16):
            out = self.m(*xs)
        return tuple(t.float() if t.is_floating_point() else t for t in out)


def expand_attention_masks(model: onnx.ModelProto) -> int:
    """Attention (opset 23) takes a mask that broadcasts over the query axis; onnxruntime's attention wants that axis
    spelled out. Each such mask is expanded to its Q's query length (Q's second-to-last axis of four, the middle of
    three: rank - 2)."""
    out, count = [], 0
    for n in list(model.graph.node):
        if n.op_type == "Attention" and n.domain in ("", "ai.onnx") and len(n.input) > 3 and n.input[3]:
            q, mask, p = n.input[0], n.input[3], f"{n.name}_maskq"
            const = lambda name, values: helper.make_node(
                "Constant", [], [p + name], value=helper.make_tensor(p + name + "_t", TensorProto.INT64, [len(values)], values))
            out += [
                const("_two", [2]), const("_axes0", [0]), const("_zero", [0]), const("_neg2", [-2]), const("_neg1", [-1]),
                const("_end", [2 ** 62]),
                helper.make_node("Shape", [q], [p + "_qshape"], name=p + "_qshape"),
                helper.make_node("Shape", [mask], [p + "_mshape"], name=p + "_mshape"),
                helper.make_node("Shape", [p + "_qshape"], [p + "_qrank"], name=p + "_qrank"),
                helper.make_node("Sub", [p + "_qrank", p + "_two"], [p + "_qaxis"], name=p + "_qaxis"),
                helper.make_node("Gather", [p + "_qshape", p + "_qaxis"], [p + "_qlen"], name=p + "_qlen", axis=0),
                helper.make_node("Slice", [p + "_mshape", p + "_zero", p + "_neg2"], [p + "_lead"], name=p + "_lead"),
                helper.make_node("Slice", [p + "_mshape", p + "_neg1", p + "_end"], [p + "_kv"], name=p + "_kv"),
                helper.make_node("Concat", [p + "_lead", p + "_qlen", p + "_kv"], [p + "_target"], name=p + "_target", axis=0),
                helper.make_node("Expand", [mask, p + "_target"], [p + "_mask"], name=p + "_expand"),
            ]
            n.input[3] = p + "_mask"
            count += 1
        out.append(n)
    del model.graph.node[:]
    model.graph.node.extend(out)
    return count


def rel(a, b):
    a, b = np.asarray(a, np.float32), np.asarray(b, np.float32)
    return float(np.abs(a - b).max() / (np.abs(b).max() + 1e-9)) if b.size else 0.0


def np_(t):
    return (t.detach().float() if t.is_floating_point() else t.detach()).cpu().numpy()


def iou(a, b):
    return float((a & b).sum() / max(1, (a | b).sum()))


def export(sam: Sam, pictures: Path, out: Path):
    picture = Image.open(pictures / "truck.jpg")
    x = Sam.normalised(picture)
    image, clicks, text, exemplar, finder = (ImageEncoder(sam).eval(), ClickDecoder(sam).eval(), TextEncoder(sam).eval(),
                                             ExemplarEncoder(sam).eval(), Finder(sam).eval())
    # no_grad, not inference_mode: these are traced from, and a trace cannot take inference tensors
    with torch.no_grad():
        feats = image(x)
        points = torch.tensor([[[371.0, 315.0], [630.0, 525.0], [322.0, 630.0]]], device="cuda")
        labels = torch.tensor([[1.0, 1.0, 0.0]], device="cuda")
        tokens = sam.tokens("window").cuda()
        text_out = text(tokens)
        boxes = torch.tensor([[[0.28, 0.33, 0.14, 0.12]], [[0.7, 0.5, 0.1, 0.1]]], device="cuda")
        box_labels = torch.tensor([[1], [0]], dtype=torch.long, device="cuda")
        box_tokens = exemplar(feats[5], boxes, box_labels)
    paths = {name: out / name / f"{name}.onnx" for name in ("image-encoder", "click-decoder", "text-encoder", "exemplar-encoder", "finder")}
    for p in paths.values():
        p.parent.mkdir(parents=True, exist_ok=True)

    t = time.time()
    program = torch.onnx.export(Half(image).eval(), (x,), dynamo=True, opset_version=23, input_names=["image"],
                                output_names=["embedding", "high0", "high1", "fpn0", "fpn1", "fpn2"], optimize=True)
    model = program.model_proto
    expand_attention_masks(model)
    # onnx appends external weights to a file already there, so a rebuild starts from none
    paths["image-encoder"].with_suffix(".onnx.data").unlink(missing_ok=True)
    onnx.save(model, paths["image-encoder"], save_as_external_data=True, location="image-encoder.onnx.data")
    print(f"image-encoder: {time.time() - t:.0f}s", flush=True)

    # traced on the CPU: the mask decoder's repeat_interleave takes a CPU count the CUDA trace cannot index with
    torch.onnx.export(copy.deepcopy(clicks).cpu(), tuple(t.cpu() for t in (*feats[:3], points, labels)),
                      paths["click-decoder"], dynamo=False, opset_version=17,
                      input_names=["embedding", "high0", "high1", "points", "labels"], output_names=["masks", "ious", "object"],
                      dynamic_axes={"points": {1: "n"}, "labels": {1: "n"}})
    torch.onnx.export(Half(text).eval(), (tokens,), paths["text-encoder"], dynamo=False, opset_version=17,
                      input_names=["tokens"], output_names=["text", "text_pad"])
    torch.onnx.export(exemplar, (feats[5], boxes, box_labels), paths["exemplar-encoder"], dynamo=False, opset_version=17,
                      input_names=["fpn2", "boxes", "box_labels"], output_names=["box_tokens"],
                      dynamic_axes={"boxes": {0: "b"}, "box_labels": {0: "b"}, "box_tokens": {0: "b"}})

    # The decoder's box bias reads its feature size back from a tensor to choose a cached grid, which export cannot
    # branch on; every picture is 1008 square, so the size is always the 72 x 72 it holds.
    decoder = sam.detector.transformer.decoder
    rpb = decoder._get_rpb_matrix
    decoder._get_rpb_matrix = lambda reference_boxes, feat_size: rpb(reference_boxes, decoder.compilable_stored_size)
    try:
        t = time.time()
        b = torch.export.Dim("b", min=0, max=64)
        program = torch.onnx.export(Autocast(finder).eval(), (*feats[3:], *text_out, box_tokens), dynamo=True,
                                    opset_version=23, input_names=["fpn0", "fpn1", "fpn2", "text", "text_pad", "box_tokens"],
                                    output_names=["logits", "presence", "boxes", "masks"],
                                    dynamic_shapes={"xs": (None, None, None, None, None, {0: b})}, optimize=True)
    finally:
        decoder._get_rpb_matrix = rpb
    model = program.model_proto
    print(f"finder: {time.time() - t:.0f}s, {expand_attention_masks(model)} attention masks expanded", flush=True)
    onnx.save(model, paths["finder"])

    # each graph against the network it was traced from, at fp32
    s = {name: ort.InferenceSession(str(p), providers=CUDA) for name, p in paths.items()}
    feeds = lambda names, ts: dict(zip(names, (np_(t) for t in ts)))
    o = s["image-encoder"].run(None, {"image": np_(x)})
    print("image-encoder (fp16) against fp32: " + ", ".join(f"{n} {rel(a, np_(r)):.1e}" for n, a, r in
          zip(["embedding", "high0", "high1", "fpn0", "fpn1", "fpn2"], o, feats)))
    for n in (1, 2, 3):
        with torch.no_grad():
            r = clicks(*feats[:3], points[:, :n], labels[:, :n])
        o = s["click-decoder"].run(None, feeds(["embedding", "high0", "high1", "points", "labels"], (*feats[:3], points[:, :n], labels[:, :n])))
        worst = max(rel(a, np_(b)) for a, b in zip(o, r))
        print(f"click-decoder, {n} point(s): {worst:.1e}")
        assert worst < 1e-4, "the click decoder is not its network"
    o = s["text-encoder"].run(None, {"tokens": np_(tokens)})
    print(f"text-encoder (fp16) against fp32: {rel(o[0], np_(text_out[0])):.1e}")
    assert (o[1] == np_(text_out[1])).all()
    for n in (1, 2):
        with torch.no_grad():
            r = exemplar(feats[5], boxes[:n], box_labels[:n])
        o = s["exemplar-encoder"].run(None, feeds(["fpn2", "boxes", "box_labels"], (feats[5], boxes[:n], box_labels[:n])))
        print(f"exemplar-encoder, {n} box(es): {rel(o[0], np_(r)):.1e}")
        assert rel(o[0], np_(r)) < 1e-4, "the exemplar encoder is not its network"
    return paths


class Graphs:
    """The five graphs, run as a port runs them: the clicks' and the processor's own post-processing after them."""

    def __init__(self, paths, providers):
        self.s = {name: ort.InferenceSession(str(p), providers=providers) for name, p in paths.items()}

    def encode(self, image):
        return self.s["image-encoder"].run(None, {"image": np_(image)})

    def click(self, feats, points, labels, width, height):
        pts = np.array([[[x * SIZE / width, y * SIZE / height] for x, y in points]], np.float32)
        masks, ious, obj = self.s["click-decoder"].run(None, {"embedding": feats[0], "high0": feats[1], "high1": feats[2],
                                                               "points": pts, "labels": np.array([labels], np.float32)})
        # one point: the best of the three by IoU; several: the one; no object: nothing
        pick = 1 + int(np.argmax(ious[0, 1:])) if len(points) <= 1 else 0
        low = torch.from_numpy(masks[:, pick:pick + 1])
        if obj[0, 0] <= 0:
            low = torch.full_like(low, -1024.0)
        high = F.interpolate(low, size=(SIZE, SIZE), mode="bilinear", align_corners=False)
        return F.interpolate(high, size=(height, width), mode="bilinear", align_corners=False)[0, 0].numpy()

    def find(self, feats, tokens, boxes, labels, width, height):
        text, pad = self.s["text-encoder"].run(None, {"tokens": tokens})
        if boxes:
            box_tokens = self.s["exemplar-encoder"].run(None, {"fpn2": feats[5], "boxes": np.array(boxes, np.float32).reshape(-1, 1, 4),
                                                               "box_labels": np.array(labels, np.int64).reshape(-1, 1)})[0]
        else:
            box_tokens = np.zeros((0, 1, 256), np.float32)
        logits, presence, boxes_out, masks = self.s["finder"].run(None, {"fpn0": feats[3], "fpn1": feats[4], "fpn2": feats[5], "text": text,
                                                                        "text_pad": pad, "box_tokens": box_tokens})
        scores = 1 / (1 + np.exp(-logits[0])) / (1 + np.exp(-presence[0, 0]))
        keep = scores > 0.5
        m = F.interpolate(torch.from_numpy(masks[0][keep][:, None]), (height, width), mode="bilinear", align_corners=False)
        return scores[keep], (m[:, 0] > 0).numpy()


def check(sam: Sam, pictures: Path, paths, providers, label):
    """The graphs end to end against the makers' paths on every case: each click's mask, and for each phrase the same
    things found, scored alike, masked alike."""
    graphs = Graphs(paths, providers)
    click_ious, found_ious = [], []
    for name, (click_cases, find_cases) in CASES.items():
        picture = Image.open(pictures / name).convert("RGB")
        w, h = picture.size
        feats = graphs.encode(Sam.normalised(picture))
        theirs_feats = sam.click_features(picture)
        for case, points, labels in click_cases:
            ours = graphs.click(feats, points, labels, w, h) > 0
            theirs = np_(sam.click(theirs_feats, points, labels, w, h)[0]) > 0
            click_ious.append(iou(ours, theirs))
            print(f"  {label} {name} click {case}: IoU {click_ious[-1]:.4f}")
        for case, phrase, boxes, labels in find_cases:
            scores, masks = graphs.find(feats, np_(sam.tokens(phrase)), boxes, labels, w, h)
            their_scores, _, their_masks = sam.find(picture, phrase,
                                                    [(cx, cy, bw, bh) for cx, cy, bw, bh in boxes], labels)
            their_scores, their_masks = np_(their_scores), np_(their_masks)
            assert len(scores) == len(their_scores), f"{name} '{phrase}': {len(scores)} found, the makers find {len(their_scores)}"
            assert np.allclose(np.sort(scores), np.sort(their_scores), atol=0.01), f"{name} '{phrase}': scores differ"
            row = [max((iou(t, o) for o in masks), default=0.0) for t in their_masks]
            found_ious += row
            print(f"  {label} {name} '{phrase}' + {len(boxes)} box(es): {len(scores)} found, IoU {np.round(row, 4)}")
    print(f"{label}: clicks mean IoU {np.mean(click_ious):.4f} (least {np.min(click_ious):.4f}); "
          f"found {len(found_ious)} masks, mean IoU {np.mean(found_ious):.4f} (least {np.min(found_ious):.4f})", flush=True)
    assert np.mean(click_ious) > 0.99 and np.min(click_ious) > 0.95, "the click path is not the makers'"
    assert np.mean(found_ious) > 0.99 and np.min(found_ious) > 0.95, "the text path is not the makers'"


def main():
    checkpoint, pictures, out = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
    sam = Sam(checkpoint)
    paths = export(sam, pictures, out)
    check(sam, pictures, paths, CUDA, "CUDA")
    check(sam, pictures, paths, ["CPUExecutionProvider"], "CPU")


if __name__ == "__main__":
    main()
