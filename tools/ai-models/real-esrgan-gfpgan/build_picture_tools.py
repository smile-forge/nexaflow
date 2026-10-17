"""
Builds the picture tools' five ONNX graphs from the upstream PyTorch models, holding each to its original as it goes:

  upscaler-x4/upscaler-x4.onnx      Real-ESRGAN x4plus     rgb [1,3,H,W] 0..1      -> rgb [1,3,4H,4W] 0..1
  upscaler-x2/upscaler-x2.onnx      Real-ESRGAN x2plus     rgb [1,3,H,W] 0..1, H and W even -> rgb [1,3,2H,2W] 0..1
  face-detector/face-detector.onnx  RetinaFace (ResNet-50) rgb [1,3,H,W] 0..1      -> boxes [1,N,4], scores [1,N], landmarks [1,N,10]
  face-restorer/face-restorer.onnx  GFPGAN v1.4            rgb [1,3,512,512] 0..1  -> rgb [1,3,512,512] 0..1
  face-parser/face-parser.onnx      ParseNet               rgb [1,3,512,512] 0..1  -> face [1,512,512] 1 where the face is pasted

Each graph does its model's own normalisation, so a caller hands every one the same thing: planar RGB from 0 to 1. The
detector's outputs are its raw regressions against its priors, decoded by the caller; the parser's is the mask GFPGAN's
paste-back makes from its classes, before the blur.

usage: build_picture_tools.py <weights folder> <upstream sources (shim)> <out folder> <picture>...

The pictures are what each graph is checked on: tiles of them for the upscalers, each whole for the detector, each brought
to 512 pixels for the face models.
"""
import json, sys, types
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch
from torch import nn
from torch.nn import functional as F

WEIGHTS, SHIM, OUT = (Path(a) for a in sys.argv[1:4])
sys.path.insert(0, str(SHIM))
from basicsr.archs.rrdbnet_arch import RRDBNet  # noqa: E402
from gfpgan.archs.gfpganv1_clean_arch import GFPGANv1Clean  # noqa: E402
from facexlib.detection import init_detection_model  # noqa: E402
from facexlib.parsing import init_parsing_model  # noqa: E402

torch.backends.cudnn.allow_tf32 = False
torch.backends.cuda.matmul.allow_tf32 = False

# GFPGAN's paste-back: the parser's classes kept as face (255) or left to the background (0).
FACE_CLASSES = [0, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 255, 0, 0, 0]


class Upscaler(nn.Module):
    def __init__(self, net):
        super().__init__()
        self.net = net

    def forward(self, rgb):
        return self.net(rgb).clamp(0, 1)


def gfpgan_forward(self, x, return_rgb=False, randomize_noise=False, **kwargs):
    """GFPGANv1Clean.forward as upstream writes it, the faces' own picture alone: its encoder's last feature map is
    flattened with reshape where upstream views it, which torch.export cannot do on the strides it traces."""
    conditions, unet_skips = [], []
    feat = F.leaky_relu(self.conv_body_first(x), negative_slope=0.2)
    for i in range(self.log_size - 2):
        feat = self.conv_body_down[i](feat)
        unet_skips.insert(0, feat)
    feat = F.leaky_relu(self.final_conv(feat), negative_slope=0.2)
    style_code = self.final_linear(feat.reshape(feat.size(0), -1))
    if self.different_w:
        style_code = style_code.view(style_code.size(0), -1, self.num_style_feat)
    for i in range(self.log_size - 2):
        feat = feat + unet_skips[i]
        feat = self.conv_body_up[i](feat)
        conditions.append(self.condition_scale[i](feat).clone())
        conditions.append(self.condition_shift[i](feat).clone())
    image, _ = self.stylegan_decoder([style_code], conditions, return_latents=False, input_is_latent=self.input_is_latent,
                                     randomize_noise=randomize_noise)
    return image, []


class FaceRestorer(nn.Module):
    """GFPGAN on an aligned 512x512 face, with the noise it was trained with rather than fresh noise each call."""
    def __init__(self, net):
        super().__init__()
        self.net = net

    def forward(self, rgb):
        out = self.net((rgb - 0.5) / 0.5, return_rgb=False, randomize_noise=False)[0]
        return (out.clamp(-1, 1) + 1) / 2


class FaceDetector(nn.Module):
    """RetinaFace on BGR less its training mean, as facexlib feeds it; the face score alone of its two classes."""
    def __init__(self, net):
        super().__init__()
        self.net = net
        self.register_buffer("mean", torch.tensor([104.0, 117.0, 123.0]).view(1, 3, 1, 1))

    def forward(self, rgb):
        bgr = rgb.flip(1) * 255 - self.mean
        loc, conf, landmarks = self.net(bgr)
        return loc, conf[..., 1], landmarks


class FaceParser(nn.Module):
    def __init__(self, net):
        super().__init__()
        self.net = net
        self.register_buffer("keep", torch.tensor(FACE_CLASSES, dtype=torch.float32) / 255)

    def forward(self, rgb):
        logits = self.net((rgb - 0.5) / 0.5)[0]
        return self.keep[logits.argmax(dim=1)]


def load_rrdb(name, scale):
    net = RRDBNet(num_in_ch=3, num_out_ch=3, num_feat=64, num_block=23, num_grow_ch=32, scale=scale)
    state = torch.load(WEIGHTS / name, map_location="cpu", weights_only=True)
    net.load_state_dict(state["params_ema"], strict=True)
    return Upscaler(net).eval()


def load_gfpgan():
    net = GFPGANv1Clean(out_size=512, num_style_feat=512, channel_multiplier=2, decoder_load_path=None, fix_decoder=False,
                        num_mlp=8, input_is_latent=True, different_w=True, narrow=1, sft_half=True)
    state = torch.load(WEIGHTS / "GFPGANv1.4.pth", map_location="cpu", weights_only=True)
    net.load_state_dict(state["params_ema"], strict=True)
    net.forward = types.MethodType(gfpgan_forward, net)
    return FaceRestorer(net).eval()


def load_detector():
    return FaceDetector(init_detection_model("retinaface_resnet50", half=False, device="cpu", model_rootpath=str(WEIGHTS))).eval()


def load_parser():
    return FaceParser(init_parsing_model("parsenet", device="cpu", model_rootpath=str(WEIGHTS))).eval()


def export(module, sample, path, inputs, outputs, dynamic):
    path.parent.mkdir(parents=True, exist_ok=True)
    with torch.no_grad():
        program = torch.onnx.export(module, (sample,), dynamo=True, input_names=inputs, output_names=outputs,
                                    dynamic_shapes=dynamic, optimize=True)
    program.save(str(path), external_data=False)
    return path


def session(path, provider):
    """CUDA without TF32, so the check measures the graph and not the GPU's shortened multiplies."""
    opts = ort.SessionOptions()
    opts.log_severity_level = 3
    options = {"use_tf32": "0"} if provider == "CUDAExecutionProvider" else {}
    return ort.InferenceSession(str(path), opts, providers=[(provider, options)])


def hold(name, module, path, samples, tolerance):
    """Each sample through the original in fp32 and the graph on CPU and CUDA: the largest difference in each output, as a
    share of the largest value that output holds."""
    report = {}
    for provider in ("CPUExecutionProvider", "CUDAExecutionProvider"):
        s = session(path, provider)
        worst = 0.0
        for sample in samples:
            with torch.no_grad():
                theirs = module(sample)
            theirs = theirs if isinstance(theirs, tuple) else (theirs,)
            ours = s.run(None, {s.get_inputs()[0].name: sample.numpy()})
            for t, o in zip(theirs, ours):
                t = t.numpy()
                worst = max(worst, float(np.abs(t - o).max() / max(np.abs(t).max(), 1e-6)))
        report[provider] = worst
        print(f"{name} on {provider}: max difference {worst:.3g} of the output's largest value", flush=True)
        if worst > tolerance:
            raise SystemExit(f"{name}'s graph does not match its original on {provider}")
    return report


def picture(path, size=None):
    import cv2
    img = cv2.imread(str(path), cv2.IMREAD_COLOR)
    if size is not None:
        img = cv2.resize(img, size, interpolation=cv2.INTER_AREA)
    return torch.from_numpy(img[:, :, ::-1].copy()).permute(2, 0, 1).float().unsqueeze(0) / 255


def main():
    photos = [Path(a) for a in sys.argv[4:]]
    if not photos:
        raise SystemExit("name the pictures to check the graphs on")
    report = {}
    h, w = torch.export.Dim("h", min=16, max=4096), torch.export.Dim("w", min=16, max=4096)
    half_h, half_w = torch.export.Dim("half_h", min=8, max=2048), torch.export.Dim("half_w", min=8, max=2048)

    tiles = [picture(p)[:, :, 40:136, 50:178] for p in photos]

    up4 = load_rrdb("RealESRGAN_x4plus.pth", 4)
    p = export(up4, tiles[0], OUT / "upscaler-x4" / "upscaler-x4.onnx", ["rgb"], ["rgb_out"], {"rgb": {2: h, 3: w}})
    report["upscaler-x4"] = hold("upscaler-x4", up4, p, tiles + [tiles[0][:, :, :50, :70]], 1e-4)

    up2 = load_rrdb("RealESRGAN_x2plus.pth", 2)
    p = export(up2, tiles[0], OUT / "upscaler-x2" / "upscaler-x2.onnx", ["rgb"], ["rgb_out"], {"rgb": {2: 2 * half_h, 3: 2 * half_w}})
    report["upscaler-x2"] = hold("upscaler-x2", up2, p, tiles + [tiles[0][:, :, :50, :70]], 1e-4)

    detector = load_detector()
    scenes = [picture(q) for q in photos]
    p = export(detector, scenes[0], OUT / "face-detector" / "face-detector.onnx", ["rgb"], ["boxes", "scores", "landmarks"],
               {"rgb": {2: h, 3: w}})
    report["face-detector"] = hold("face-detector", detector, p, scenes, 1e-3)

    faces = [picture(q, (512, 512)) for q in photos]
    restorer = load_gfpgan()
    p = export(restorer, faces[0], OUT / "face-restorer" / "face-restorer.onnx", ["rgb"], ["rgb_out"], None)
    report["face-restorer"] = hold("face-restorer", restorer, p, faces, 1e-3)

    parser = load_parser()
    p = export(parser, faces[0], OUT / "face-parser" / "face-parser.onnx", ["rgb"], ["face"], None)
    # The mask is a class choice: a pixel on the boundary between two classes may fall either way, so count those.
    s = session(p, "CPUExecutionProvider")
    flips = 0
    for f in faces:
        with torch.no_grad():
            theirs = parser(f).numpy()
        flips = max(flips, int((np.abs(theirs - s.run(None, {"rgb": f.numpy()})[0]) > 0.5).sum()))
    print(f"face-parser: at most {flips} of 262144 pixels choose differently", flush=True)
    if flips > 64:
        raise SystemExit("the face parser's graph does not match its original")
    report["face-parser"] = {"pixels_differing": flips}
    (OUT / "build-report.json").write_text(json.dumps(report, indent=1))


if __name__ == "__main__":
    main()
