"""What HiDream-O1's own sampler does on small made-up tensors, so the host's port can be checked against it: its
timesteps and noise levels for fifty steps at shift 3, and the samples a made-up model's guided predictions step to.

usage: python -I unipc_reference.py <hidream-o1-image code> src/Nexaflow.Tests/Nexaflow.Tests.AiHost/Drawing/hidream-unipc.json
"""
import json
import sys

import torch


def main():
    sys.path.insert(0, sys.argv[1])
    from models.pipeline import build_scheduler

    steps, shift, guidance = 50, 3.0, 5.0
    sched = build_scheduler(steps, None, shift, "cpu", "default")
    record = {
        "steps": steps, "shift": shift, "guidance": guidance,
        "timesteps": [int(t) for t in sched.timesteps],
        "sigmas": [float(s) for s in sched.sigmas],
    }

    # A made-up "model": predictions that depend on the sample and the step, as the pipeline feeds them in.
    g = torch.Generator().manual_seed(7)
    z = 8.0 * torch.randn(1, 4, 6, generator=g)
    record["start"] = z.flatten().tolist()
    cond_bias = torch.randn(1, 4, 6, generator=g)
    uncond_bias = torch.randn(1, 4, 6, generator=g)
    trace = []
    for i, step_t in enumerate(sched.timesteps):
        sigma = (step_t.float() / 1000.0).clamp_min(0.001)
        x_cond = torch.tanh(0.1 * z + cond_bias * (i + 1) / steps)
        x_uncond = torch.tanh(0.1 * z + uncond_bias * (i + 1) / steps)
        v_cond = (x_cond - z) / sigma
        v_uncond = (x_uncond - z) / sigma
        v = v_uncond + guidance * (v_cond - v_uncond)
        z = sched.step((-v).float(), step_t.to(torch.float32), z.float(), return_dict=False)[0]
        if i in (0, 1, 2, 24, 48, 49):
            trace.append({"step": i, "sample": z.flatten().tolist()})
    record["cond_bias"] = cond_bias.flatten().tolist()
    record["uncond_bias"] = uncond_bias.flatten().tolist()
    record["trace"] = trace
    with open(sys.argv[2], "w") as f:
        json.dump(record, f)
    print("timesteps", record["timesteps"][:5], "...", record["timesteps"][-3:])


if __name__ == "__main__":
    main()
