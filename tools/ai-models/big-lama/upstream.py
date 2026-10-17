"""
The upstream code big-lama is built from, fetched at a pinned commit and laid out so it imports without installing it.
LaMa's package reaches its generator through training code that imports PyTorch Lightning, Hydra, Kornia and the
evaluation's segmentation models, so only the generator's own modules are kept, under a package of the same name:

  saicinpainting/training/modules   ffc.py (FFCResNetGenerator, FourierUnit), base.py, squeeze_excitation.py,
                                    depthwise_sep_conv.py, multidilated_conv.py, and a stub of spatial_transform.py:
                                    its wrapper is built only for spatial_transform_layers, which big-lama names none of
  saicinpainting/utils.py           get_shape alone, as upstream writes it

usage: upstream.py <work folder>    prints the shim's folder
"""
import ast, shutil, subprocess, sys
from pathlib import Path

PINNED = ("https://github.com/advimman/lama", "786f5936b27fb3dacd2b1ad799e4de968ea697e7")
MODULES = ("ffc.py", "base.py", "squeeze_excitation.py", "depthwise_sep_conv.py", "multidilated_conv.py")


def fetch(src: Path):
    url, commit = PINNED
    repo = src / "lama"
    if not (repo / ".git").exists():
        repo.mkdir(parents=True, exist_ok=True)
        subprocess.run(["git", "init", "-q", str(repo)], check=True)
        subprocess.run(["git", "-C", str(repo), "remote", "add", "origin", url], check=True)
    subprocess.run(["git", "-C", str(repo), "fetch", "-q", "--depth", "1", "origin", commit], check=True)
    subprocess.run(["git", "-C", str(repo), "checkout", "-q", "--force", commit], check=True)
    return repo


def write(path: Path, text: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def function_source(path: Path, name: str):
    text = path.read_text(encoding="utf-8")
    node = next(n for n in ast.parse(text).body if isinstance(n, ast.FunctionDef) and n.name == name)
    return ast.get_source_segment(text, node)


def main():
    work = Path(sys.argv[1])
    lama = fetch(work / "src") / "saicinpainting"
    shim = work / "shim"
    if shim.exists():
        shutil.rmtree(shim)

    write(shim / "saicinpainting/__init__.py", "")
    write(shim / "saicinpainting/training/__init__.py", "")
    write(shim / "saicinpainting/training/modules/__init__.py", "")
    for name in MODULES:
        shutil.copy(lama / "training/modules" / name, shim / "saicinpainting/training/modules" / name)
    write(shim / "saicinpainting/training/modules/spatial_transform.py",
          "class LearnableSpatialTransformWrapper:\n"
          "    def __init__(self, *args, **kwargs):\n"
          "        raise RuntimeError('big-lama builds no spatial transform layers')\n")
    write(shim / "saicinpainting/utils.py",
          "import numbers\n\nimport torch\n\n\n" + function_source(lama / "utils.py", "get_shape") + "\n")
    print(shim)


if __name__ == "__main__":
    main()
