"""Cuts a published ONNX graph into groups the AI host streams to a GPU a group at a time (StreamedGraph).

The graph is cut where the fewest tensors cross from one group to the next — between a transformer's blocks — into groups each
holding about the given megabytes of the weights its products read (MatMulNBits, MatMul, Gemm). In each group those weights
become graph inputs, read by the host
from the graph's own external-data file at the offsets the manifest gives; every other weight stays an initializer in that
same file, so nothing is written twice. The groups and the manifest are written beside the graph:

    <stem>.group00.onnx ... <stem>.groupNN.onnx
    <stem>.stream.json      {data, outputs, groups: [{file, inputs, outputs, streamed: [{name, type, shape, offset, length}]}]}

usage: split_graph.py <graph.onnx> <megabytes per group>
"""
import json
import os
import sys

import onnx
from onnx import helper
from onnxruntime.tools.symbolic_shape_infer import SymbolicShapeInference

# The weights streamed: what each kind of product reads as its weights — MatMulNBits its packed B, scales and zero points, a
# plain MatMul or Gemm its B — where it is at least a mebibyte; smaller ones, norms and biases, stay resident.
STREAMED_INPUTS = {"MatMulNBits": (1, 2, 3), "MatMul": (1,), "Gemm": (1,)}
SMALLEST = 1 << 20
SEARCH = 150                  # nodes past the target a cut is looked for in


def external(tensor):
    fields = {e.key: e.value for e in tensor.external_data}
    return fields.get("location"), int(fields.get("offset", 0)), int(fields.get("length", 0))


def main(source, megabytes):
    folder, name = os.path.split(os.path.abspath(source))
    stem = name[:-len(".onnx")]
    model = onnx.load(source, load_external_data=False)
    # Types for the tensors that cross between groups: ONNX Runtime's inference knows the com.microsoft ops. It reads a few
    # small constants from the weights file, which it finds relative to the working folder.
    os.chdir(folder)
    typed = SymbolicShapeInference.infer_shapes(model, auto_merge=True, guess_output_rank=True)
    graph = typed.graph
    inits = {t.name: t for t in model.graph.initializer}
    infos = {vi.name: vi for vi in list(graph.value_info) + list(graph.input) + list(graph.output)}
    graph_inputs = [i.name for i in model.graph.input]
    graph_outputs = [o.name for o in model.graph.output]
    nodes = list(model.graph.node)
    n = len(nodes)

    produced = {x: -1 for x in graph_inputs}
    last_use = {}
    for i, node in enumerate(nodes):
        for x in node.input:
            if x and x not in inits:
                last_use[x] = i
        for y in node.output:
            produced[y] = i
    for y in graph_outputs:
        last_use[y] = n

    def streams(node, k, x):
        return k in STREAMED_INPUTS.get(node.op_type, ()) and x in inits and external(inits[x])[0] is not None \
            and external(inits[x])[2] >= (0 if node.op_type == "MatMulNBits" else SMALLEST)

    streamed_at = [0] * n
    for i, node in enumerate(nodes):
        streamed_at[i] = sum(external(inits[x])[2] for k, x in enumerate(node.input) if streams(node, k, x))

    # How many tensors cross after each node.
    events = sorted([(p, 1) for x, p in produced.items() if last_use.get(x, -2) > p]
                    + [(last_use[x], -1) for x, p in produced.items() if last_use.get(x, -2) > p])
    live, count, e = [0] * n, 0, 0
    for i in range(n):
        while e < len(events) and events[e][0] <= i:
            count += events[e][1]
            e += 1
        live[i] = count

    # Each group once it holds its share, at the narrowest point soon after.
    target, cuts, held, i = megabytes * (1 << 20), [], 0, 0
    while i < n:
        held += streamed_at[i]
        if held >= target:
            cut = min(range(i, min(n - 1, i + SEARCH)), key=lambda k: (live[k], k))
            cuts.append(cut)
            held, i = 0, cut + 1
            continue
        i += 1
    while cuts and n - 1 - cuts[-1] < 50:
        cuts.pop()                            # a last group of a few nodes joins the one before
    bounds = [-1] + cuts + [n - 1]

    def crossing(after):
        return [x for x, p in produced.items() if p <= after and last_use.get(x, -2) > after]

    def info(x):
        if x not in infos or infos[x].type.tensor_type.elem_type == 0:
            raise SystemExit(f"{x} crosses between groups and its type is not known")
        return infos[x]

    data_files, groups = set(), []
    for g in range(len(bounds) - 1):
        lo, hi = bounds[g] + 1, bounds[g + 1]
        part = nodes[lo:hi + 1]
        used = {x for node in part for x in node.input}
        made = {y for node in part for y in node.output}
        inputs = [x for x in (crossing(lo - 1) if lo > 0 else graph_inputs) if x in used]
        outputs = [x for x in (crossing(hi) if hi < n - 1 else graph_outputs) if x in made]

        streamed, resident = [], []
        for node in part:
            for k, x in enumerate(node.input):
                if x not in inits or x in resident or any(s["name"] == x for s in streamed):
                    continue
                if streams(node, k, x):
                    location, offset, length = external(inits[x])
                    data_files.add(location)
                    streamed.append({"name": x, "type": inits[x].data_type, "shape": list(inits[x].dims), "offset": offset, "length": length})
                else:
                    resident.append(x)

        sub = helper.make_graph(
            part, f"{stem}-group{g:02d}",
            [info(x) for x in inputs] + [helper.make_tensor_value_info(s["name"], s["type"], s["shape"]) for s in streamed],
            [info(x) for x in outputs],
            initializer=[inits[x] for x in resident])
        file = f"{stem}.group{g:02d}.onnx"
        onnx.save(helper.make_model(sub, opset_imports=model.opset_import, ir_version=model.ir_version), os.path.join(folder, file))
        groups.append({"file": file, "inputs": inputs, "outputs": outputs, "streamed": streamed})
        print(f"{file}: nodes {lo}-{hi}, {len(inputs)} in, {len(outputs)} out, "
              f"{sum(s['length'] for s in streamed) / 1e6:.1f} MB streamed, {sum(external(inits[x])[2] for x in resident) / 1e6:.1f} MB resident")

    if len(data_files) != 1:
        raise SystemExit(f"the streamed weights lie in {sorted(data_files)}, not one file")
    manifest = {"data": data_files.pop(), "outputs": graph_outputs, "groups": groups}
    with open(os.path.join(folder, f"{stem}.stream.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=1)
    print(f"{sum(streamed_at) / 1e9:.2f} GB streamed in {len(groups)} groups")


if __name__ == "__main__":
    main(sys.argv[1], float(sys.argv[2]))
