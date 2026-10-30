"""Renders ModShelf's icons from src/ModShelf.Desktop/Assets/modshelf.svg.

Writes the Windows icon (modshelf.ico, beside the SVG) and the macOS one (build/macos/modshelf.icns), each with every
size rendered from the SVG rather than scaled from one picture.

Needs Python 3 with cairosvg and Pillow (pip install cairosvg pillow), and the Cairo library. Run it from anywhere:
    python build/render-icons.py
"""

import io
import pathlib

import cairosvg
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "ModShelf.Desktop" / "Assets"
SVG = (ASSETS / "modshelf.svg").read_bytes()


def render(size: int) -> Image.Image:
    png = cairosvg.svg2png(bytestring=SVG, output_width=size, output_height=size)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def save_all(path: pathlib.Path, pixels: list[int], **options) -> None:
    images = [render(size) for size in pixels]
    images[-1].save(path, append_images=images[:-1], **options)
    print(f"Wrote {path.relative_to(ROOT)}")


# Windows: the sizes Explorer, the taskbar, the Start menu and the toasts ask for.
ico_sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
save_all(ASSETS / "modshelf.ico", ico_sizes, format="ICO", sizes=[(size, size) for size in ico_sizes])

# macOS: up to 1024 pixels, for the Retina Dock and Finder.
save_all(ROOT / "build" / "macos" / "modshelf.icns", [16, 32, 64, 128, 256, 512, 1024], format="ICNS")
