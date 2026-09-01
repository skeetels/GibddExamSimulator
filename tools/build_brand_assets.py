#!/usr/bin/env python3
"""Build deterministic Windows and PWA icon variants from the master logo."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from PIL import Image


WINDOWS_ICON_SIZES = (16, 24, 32, 48, 64, 128, 256)


def save_png(source: Image.Image, size: int, destination: Path) -> None:
    resized = source.resize((size, size), Image.Resampling.LANCZOS)
    resized.save(destination, format="PNG", optimize=True)


def build(root: Path) -> list[Path]:
    master_path = root / "assets" / "branding" / "logo-master.png"
    branding = master_path.parent
    web_root = root / "src" / "GibddExamSimulator.Web" / "wwwroot"
    source = Image.open(master_path).convert("RGBA")
    if source.width != source.height or source.width < 1024:
        raise ValueError("The master logo must be square and at least 1024 px.")

    generated: list[Path] = []
    for size, destination in (
        (1024, branding / "app-icon-1024.png"),
        (256, branding / "windows-icon-256.png"),
        (512, web_root / "icon-512.png"),
        (192, web_root / "icon-192.png"),
        (64, web_root / "favicon.png"),
    ):
        save_png(source, size, destination)
        generated.append(destination)

    maskable = Image.new("RGBA", (512, 512), "#0F3452")
    safe_logo = source.resize((410, 410), Image.Resampling.LANCZOS)
    maskable.alpha_composite(safe_logo, ((512 - 410) // 2, (512 - 410) // 2))
    maskable_path = web_root / "icon-maskable-512.png"
    maskable.save(maskable_path, format="PNG", optimize=True)
    generated.append(maskable_path)

    ico_path = branding / "windows-app.ico"
    source.save(ico_path, format="ICO", sizes=[(size, size) for size in WINDOWS_ICON_SIZES])
    generated.append(ico_path)
    return generated


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.root.resolve()
    for path in build(root):
        digest = hashlib.sha256(path.read_bytes()).hexdigest().upper()
        print(f"{path.relative_to(root)}  SHA256={digest}")


if __name__ == "__main__":
    main()
