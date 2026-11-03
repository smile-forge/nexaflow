"""
The DPT head's last resize, to the input's own size whatever it is.

The makers' DPTHead.forward brings its output to the input's size with
F.interpolate(out, (int(patch_h * 14), int(patch_w * 14)), ...). Traced with a symbolic size, int() fixes each side to the
sample's, and the exported graph would answer every picture at the sample's size. Here the forward is the makers' own,
line for line, but for that size, left symbolic — the same resize, at the size of the picture it is given.
"""
import torch
import torch.nn.functional as F


def dpt_head_forward(self, out_features, patch_h, patch_w):
    out = []
    for i, x in enumerate(out_features):
        if self.use_clstoken:
            x, cls_token = x[0], x[1]
            readout = cls_token.unsqueeze(1).expand_as(x)
            x = self.readout_projects[i](torch.cat((x, readout), -1))
        else:
            x = x[0]

        x = x.permute(0, 2, 1).reshape((x.shape[0], x.shape[-1], patch_h, patch_w))

        x = self.projects[i](x)
        x = self.resize_layers[i](x)

        out.append(x)

    layer_1, layer_2, layer_3, layer_4 = out

    layer_1_rn = self.scratch.layer1_rn(layer_1)
    layer_2_rn = self.scratch.layer2_rn(layer_2)
    layer_3_rn = self.scratch.layer3_rn(layer_3)
    layer_4_rn = self.scratch.layer4_rn(layer_4)

    path_4 = self.scratch.refinenet4(layer_4_rn, size=layer_3_rn.shape[2:])
    path_3 = self.scratch.refinenet3(path_4, layer_3_rn, size=layer_2_rn.shape[2:])
    path_2 = self.scratch.refinenet2(path_3, layer_2_rn, size=layer_1_rn.shape[2:])
    path_1 = self.scratch.refinenet1(path_2, layer_1_rn)

    out = self.scratch.output_conv1(path_1)
    out = F.interpolate(out, (patch_h * 14, patch_w * 14), mode="bilinear", align_corners=True)
    out = self.scratch.output_conv2(out)

    return out
