"""
DINOv2's positional embedding brought to a picture's grid of patches as matrix products, so the graph takes any size.

The makers' interpolate_pos_encoding resizes the 37 × 37 grid it was trained on by PyTorch's bicubic interpolation at a
scale factor of (n + 0.1) / 37 for an n-patch side — and leaves it as it is for a square 518-pixel picture. Traced with a
symbolic size, that scale factor is a float made from the input's shape, which the exporter cannot carry into ONNX's
Resize. Here the same sums are written as products with weight matrices built from the size in the graph: for each of
the n output positions, PyTorch's source position scale × (d + 0.5) − 0.5 with scale the reciprocal of the factor, its four
neighbours clamped to the grid, weighed by the cubic convolution PyTorch uses (A = −0.75). The square 518-pixel picture's
grid is the trained one exactly: its scale is 1, every weight 0 but its own.
"""
import torch

A = -0.75


def _convolution1(x):
    return ((A + 2) * x - (A + 3)) * x * x + 1


def _convolution2(x):
    return ((A * x - 5 * A) * x + 8 * A) * x - 4 * A


def cubic_weights(scale, count, size: int):
    """[count, size]: row d holds the weights the bicubic resize gives each of the size source positions for output d,
    at the source step scale (a 0-d tensor)."""
    d = torch.arange(count, device=scale.device, dtype=torch.float32)
    real = scale * (d + 0.5) - 0.5
    index = torch.floor(real)
    t = real - index
    taps = torch.stack([_convolution2(t + 1), _convolution1(t), _convolution1(1 - t), _convolution2(2 - t)], dim=1)
    source = (index.long()[:, None] + torch.arange(-1, 3, device=scale.device)[None, :]).clamp(0, size - 1)
    weights = torch.zeros(count, size, device=scale.device, dtype=torch.float32)
    return weights.scatter_add(1, source, taps)


def interpolate_pos_encoding(self, x, w, h):
    """The makers' interpolate_pos_encoding, as products. As there, w and h are the input's third and fourth sizes —
    its height and width."""
    previous_dtype = x.dtype
    pos_embed = self.pos_embed.float()
    class_pos_embed = pos_embed[:, 0]
    patch_pos_embed = pos_embed[:, 1:]
    dim = x.shape[-1]
    side = int(round((pos_embed.shape[1] - 1) ** 0.5))
    rows, cols = w // self.patch_size, h // self.patch_size

    # The patch counts as values in the graph, so the square 518-pixel case is chosen by a value, not by tracing it.
    n_rows = (torch.arange(rows, device=x.device)[-1:] + 1).float()
    n_cols = (torch.arange(cols, device=x.device)[-1:] + 1).float()
    square = (n_rows == side) & (n_cols == side)
    one = torch.ones_like(n_rows)
    row_scale = torch.where(square, one, side / (n_rows + self.interpolate_offset))[0]
    col_scale = torch.where(square, one, side / (n_cols + self.interpolate_offset))[0]

    grid = patch_pos_embed.reshape(side, side, dim)
    down = cubic_weights(row_scale, rows, side)
    across = cubic_weights(col_scale, cols, side)
    resized = torch.einsum("rs,std,ct->rcd", down, grid, across)
    return torch.cat((class_pos_embed.unsqueeze(0), resized.reshape(1, -1, dim)), dim=1).to(previous_dtype)
