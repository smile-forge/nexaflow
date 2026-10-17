"""
The upstream code the picture tools are built from, fetched at pinned commits and laid out so it imports without
installing it: Real-ESRGAN, GFPGAN and facexlib reach their networks through BasicSR, whose package imports every
dataset, loss and CUDA extension it has — and whose setup.py does not run on Python 3.13 — so only the network
definitions and the helpers they call are kept, under a package of the same names:

  basicsr     archs/rrdbnet_arch.py, archs/arch_util.py (distutils' LooseVersion read from packaging), utils/registry.py,
              utils/img_util.py, and stubs for the deformable convolution and the downloader, which no picture tool reaches
  realesrgan  archs/srvgg_arch.py, utils.py (RealESRGANer, the reference's tiling)
  gfpgan      archs/gfpganv1_clean_arch.py, archs/stylegan2_clean_arch.py, utils.py (GFPGANer, the reference pipeline)
              without the original and bilinear archs, which need NVIDIA's non-commercial CUDA ops
  facexlib    detection, parsing, utils — RetinaFace, ParseNet and the face helper — without tracking and recognition

usage: upstream.py <work folder>    prints the shim's folder
"""
import shutil, subprocess, sys
from pathlib import Path

PINNED = {
    "Real-ESRGAN": ("https://github.com/xinntao/Real-ESRGAN", "a4abfb2979a7bbff3f69f58f58ae324608821e27"),
    "GFPGAN": ("https://github.com/TencentARC/GFPGAN", "7552a7791caad982045a7bbe5634bbf1cd5c8679"),
    "facexlib": ("https://github.com/xinntao/facexlib", "260620ae93990a300f4b16448df9bb459f1caba9"),
    "BasicSR": ("https://github.com/XPixelGroup/BasicSR", "8d56e3a045f9fb3e1d8872f92ee4a4f07f886b0a"),
}


def fetch(src: Path, name: str):
    url, commit = PINNED[name]
    repo = src / name
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


def main():
    work = Path(sys.argv[1])
    src = work / "src"
    repos = {name: fetch(src, name) for name in PINNED}
    shim = work / "shim"
    if shim.exists():
        shutil.rmtree(shim)

    basicsr = repos["BasicSR"] / "basicsr"
    write(shim / "basicsr/__init__.py", "")
    write(shim / "basicsr/utils/__init__.py",
          "import logging\n\n\ndef get_root_logger(*args, **kwargs):\n    return logging.getLogger('basicsr')\n\n\n"
          "from .img_util import img2tensor, tensor2img\n")
    shutil.copy(basicsr / "utils/registry.py", shim / "basicsr/utils/registry.py")
    shutil.copy(basicsr / "utils/img_util.py", shim / "basicsr/utils/img_util.py")
    write(shim / "basicsr/utils/download_util.py",
          "def load_file_from_url(*args, **kwargs):\n    raise RuntimeError('the weights are passed in; nothing is downloaded')\n")
    write(shim / "basicsr/ops/__init__.py", "")
    write(shim / "basicsr/ops/dcn/__init__.py",
          "# Deformable convolution is used only by video archs; no picture tool reaches it.\n"
          "ModulatedDeformConvPack = object\nmodulated_deform_conv = None\n")
    write(shim / "basicsr/archs/__init__.py", "")
    util = (basicsr / "archs/arch_util.py").read_text(encoding="utf-8")
    old = "from distutils.version import LooseVersion"
    assert old in util, "BasicSR's arch_util no longer imports LooseVersion from distutils"
    write(shim / "basicsr/archs/arch_util.py", util.replace(old, "from packaging.version import Version as LooseVersion"))
    shutil.copy(basicsr / "archs/rrdbnet_arch.py", shim / "basicsr/archs/rrdbnet_arch.py")

    realesrgan = repos["Real-ESRGAN"] / "realesrgan"
    write(shim / "realesrgan/__init__.py", "")
    write(shim / "realesrgan/archs/__init__.py", "")
    shutil.copy(realesrgan / "archs/srvgg_arch.py", shim / "realesrgan/archs/srvgg_arch.py")
    shutil.copy(realesrgan / "utils.py", shim / "realesrgan/utils.py")

    gfpgan = repos["GFPGAN"] / "gfpgan"
    write(shim / "gfpgan/__init__.py", "")
    write(shim / "gfpgan/archs/__init__.py", "")
    for arch in ("gfpganv1_clean_arch.py", "stylegan2_clean_arch.py"):
        shutil.copy(gfpgan / "archs" / arch, shim / "gfpgan/archs" / arch)
    utils = [line for line in (gfpgan / "utils.py").read_text(encoding="utf-8").splitlines(keepends=True)
             if "gfpgan_bilinear_arch" not in line and "gfpganv1_arch import" not in line]
    write(shim / "gfpgan/utils.py", "".join(utils))

    facexlib = repos["facexlib"] / "facexlib"
    write(shim / "facexlib/__init__.py", "")
    for part in ("detection", "parsing", "utils"):
        shutil.copytree(facexlib / part, shim / "facexlib" / part)
    print(shim)


if __name__ == "__main__":
    main()
