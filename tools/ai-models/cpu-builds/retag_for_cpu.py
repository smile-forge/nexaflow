"""Writes a published graph's CPU build beside it: the same graph, its MatMulNBits products reading 8-bit inputs.

A graph quantised for the GPU reads its 8-bit weights with float inputs (MatMulNBits accuracy_level 0): on the CPU each product
unpacks its weights to float every time, fifty times slower than reading them with 8-bit inputs (accuracy_level 4), MLAS's
integer kernels. The CPU build is the graph with that one attribute changed, its weights left where they are, so it is a file
of a few megabytes beside the graph that shares its .onnx.data:

    <stem>.cpu.onnx

usage: retag_for_cpu.py <graph.onnx>
"""
import os
import sys

import onnx
from onnx import helper

ACCURACY_LEVEL = 4   # 8-bit inputs


def main(source):
    folder, name = os.path.split(os.path.abspath(source))
    model = onnx.load(source, load_external_data=False)
    count = 0
    for node in model.graph.node:
        if node.op_type != "MatMulNBits":
            continue
        kept = [a for a in node.attribute if a.name != "accuracy_level"]
        del node.attribute[:]
        node.attribute.extend(kept)
        node.attribute.append(helper.make_attribute("accuracy_level", ACCURACY_LEVEL))
        count += 1
    if count == 0:
        raise SystemExit(f"{name} has no MatMulNBits product to read with 8-bit inputs")
    target = os.path.join(folder, name[:-len(".onnx")] + ".cpu.onnx")
    onnx.save(model, target)
    print(f"{count} products read with 8-bit inputs: {os.path.basename(target)}")


if __name__ == "__main__":
    main(sys.argv[1])
