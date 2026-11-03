"""
The upstream code Depth Anything V2 is built from, fetched at a pinned commit. Its package, depth_anything_v2 — DINOv2's
ViT and the DPT head — imports as it is, from the repository's root; nothing of it is changed or left out.

usage: upstream.py <work folder>    prints the folder depth_anything_v2 imports from
"""
import subprocess, sys
from pathlib import Path

PINNED = ("https://github.com/DepthAnything/Depth-Anything-V2", "a561b849ebae10a6f5ef49e26c83cbbcd36c71bf")


def fetch(src: Path):
    url, commit = PINNED
    repo = src / "depth-anything-v2"
    if not (repo / ".git").exists():
        repo.mkdir(parents=True, exist_ok=True)
        subprocess.run(["git", "init", "-q", str(repo)], check=True)
        subprocess.run(["git", "-C", str(repo), "remote", "add", "origin", url], check=True)
    subprocess.run(["git", "-C", str(repo), "fetch", "-q", "--depth", "1", "origin", commit], check=True)
    subprocess.run(["git", "-C", str(repo), "checkout", "-q", "--force", commit], check=True)
    return repo


def main():
    print(fetch(Path(sys.argv[1]) / "src"))


if __name__ == "__main__":
    main()
