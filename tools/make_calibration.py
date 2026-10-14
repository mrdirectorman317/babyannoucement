#!/usr/bin/env python3
"""Regenerate tests/fixtures/calibration.jpg: a 640x360 synthetic orientation target.

Each corner is labeled and colored so a flipped/mirrored panel texture is obvious
on the headset: TOP-LEFT red, TOP-RIGHT green, BOTTOM-LEFT blue, BOTTOM-RIGHT yellow,
plus a center crosshair and an arrow that points up-and-right.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

W, H = 640, 360
OUT = Path(__file__).resolve().parents[1] / "tests/fixtures/calibration.jpg"


def main() -> None:
    img = Image.new("RGB", (W, H), (24, 26, 32))
    d = ImageDraw.Draw(img)
    big = ImageFont.load_default(size=30)
    small = ImageFont.load_default(size=16)
    for x in range(0, W, 40):
        d.line([(x, 0), (x, H)], fill=(44, 48, 58))
    for y in range(0, H, 40):
        d.line([(0, y), (W, y)], fill=(44, 48, 58))
    s = 110
    corners = [
        ("TOP-LEFT", (0, 0), (220, 60, 60), (8, 8)),
        ("TOP-RIGHT", (W - s, 0), (60, 200, 90), (W - 150, 8)),
        ("BOTTOM-LEFT", (0, H - s), (70, 110, 230), (8, H - 40)),
        ("BOTTOM-RIGHT", (W - s, H - s), (235, 200, 60), (W - 215, H - 40)),
    ]
    for label, (x, y), color, (tx, ty) in corners:
        d.rectangle([x, y, x + s - 1, y + s - 1], fill=color)
        d.text((tx, ty), label, fill=(0, 0, 0), font=small)
    cx, cy = W // 2, H // 2
    d.line([(cx - 40, cy), (cx + 40, cy)], fill=(255, 255, 255), width=3)
    d.line([(cx, cy - 40), (cx, cy + 40)], fill=(255, 255, 255), width=3)
    d.text((cx - 70, cy + 48), "SpatialMac 640x360", fill=(230, 230, 240), font=small)
    d.polygon([(cx + 90, cy - 70), (cx + 140, cy - 70), (cx + 140, cy - 20)], fill=(255, 255, 255))
    d.text((cx - 150, cy - 90), "UP / RIGHT ->", fill=(255, 255, 255), font=big)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.save(OUT, "JPEG", quality=88)
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
