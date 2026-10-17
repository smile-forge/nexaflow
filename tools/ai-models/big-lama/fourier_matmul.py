"""
LaMa's Fourier unit with its 2-D real FFT written as matrix products — the same sums, the same 'ortho' scale — so the
graph needs no DFT operator: ONNX Runtime's CUDA provider has none, and its CPU one is an O(n^2) loop off powers of two
(134 s for a 1800x1200 picture). As products the unit runs as any convolution layer does, on every provider.

The unit's transforms run at an eighth of the picture's size (after the generator's three downsamplings), so a side of
up to 4096 pixels gives matrices of at most 512 by 512.
"""
import math

import torch


def angles(rows, cols, size, device):
    """2 pi (i j mod size) / size for i < rows, j < cols. i j stays below 2^24 for sides up to 4096 at an eighth, so it is
    exact in float32; a floor that rounds the wrong way moves the angle by a whole turn, which cos and sin never see."""
    i = torch.arange(rows, device=device, dtype=torch.float32)[:, None]
    j = torch.arange(cols, device=device, dtype=torch.float32)[None, :]
    p = i * j
    return (p - size * torch.floor(p / size)) * (2 * math.pi / size)


def rfft2(x):
    """x [..., H, W] real -> (re, im) [..., H, W // 2 + 1]: torch.fft.rfftn(x, dim=(-2, -1), norm='ortho')."""
    h, w = x.shape[-2], x.shape[-1]
    aw = angles(w, w // 2 + 1, w, x.device)
    a, b = x @ torch.cos(aw).to(x.dtype), -(x @ torch.sin(aw).to(x.dtype))  # along the width, one-sided
    ah = angles(h, h, h, x.device)
    ch, sh = torch.cos(ah).to(x.dtype), torch.sin(ah).to(x.dtype)
    scale = torch.rsqrt(torch.tensor(h * w, dtype=x.dtype, device=x.device))
    return (ch @ a + sh @ b) * scale, (ch @ b - sh @ a) * scale


def irfft2(re, im, h, w):
    """(re, im) [..., H, W // 2 + 1] -> x [..., H, W]: torch.fft.irfftn(re + i im, s=(H, W), dim=(-2, -1), norm='ortho').
    As irfftn does, the imaginary parts of the zero and (for an even width) the last column are not read."""
    half = re.shape[-1]
    ah = angles(h, h, h, re.device)
    ch, sh = torch.cos(ah).to(re.dtype), torch.sin(ah).to(re.dtype)
    yr, yi = ch @ re - sh @ im, sh @ re + ch @ im  # along the height, both ways
    aw = angles(half, w, w, re.device)
    col = torch.arange(half, device=re.device)
    # 2 col == w holds only for an even width's last column; as a tensor comparison the width stays the graph's, where
    # a test of w's parity in Python would be settled once, on the size the graph was traced at
    twice = torch.where((col == 0) | (col * 2 == w), 1.0, 2.0).to(re.dtype)[:, None]
    scale = torch.rsqrt(torch.tensor(h * w, dtype=re.dtype, device=re.device))
    return (yr @ (torch.cos(aw).to(re.dtype) * twice) - yi @ (torch.sin(aw).to(re.dtype) * twice)) * scale


def fourier_unit_forward(self, x):
    """FourierUnit.forward as upstream writes it for what big-lama builds — two dimensions, no spatial scaling, no
    spectral position encoding, no squeeze-excitation — with rfft2 and irfft2 for torch.fft's."""
    assert self.fft_norm == "ortho" and not self.ffc3d and self.spatial_scale_factor is None
    assert not self.spectral_pos_encoding and not self.use_se
    batch, h, w = x.shape[0], x.shape[-2], x.shape[-1]
    re, im = rfft2(x)
    ffted = torch.stack((re, im), dim=2)  # (batch, c, 2, h, w/2+1), as upstream lays it out
    ffted = ffted.reshape((batch, -1) + tuple(ffted.shape[3:]))
    ffted = self.relu(self.bn(self.conv_layer(ffted)))
    ffted = ffted.reshape((batch, -1, 2) + tuple(ffted.shape[2:]))
    return irfft2(ffted[:, :, 0], ffted[:, :, 1], h, w)
