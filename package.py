import argparse
import json
import math
import shutil
import struct
import zlib
from itertools import pairwise
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

PROFIT_LINE = [(52, 170), (92, 146), (124, 160), (164, 100), (204, 70)]
LOSS_LINE = [(52, 170), (96, 184), (140, 176), (204, 206)]


def segment_distance(x: float, y: float, line: list[tuple[int, int]]) -> float:
    nearest = float("inf")
    for (ax, ay), (bx, by) in pairwise(line):
        dx, dy = bx - ax, by - ay
        along = max(
            0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy))
        )
        nearest = min(nearest, math.hypot(x - ax - along * dx, y - ay - along * dy))
    return nearest


def icon_pixel(x: float, y: float) -> tuple[int, int, int]:
    corner_x = max(44.0, min(212.0, x))
    corner_y = max(44.0, min(212.0, y))
    panel_distance = math.hypot(x - corner_x, y - corner_y)
    if panel_distance > 32:
        return (10, 10, 10)
    if panel_distance > 26:
        return (240, 240, 240)
    for line, color in ((PROFIT_LINE, (61, 237, 97)), (LOSS_LINE, (255, 59, 92))):
        end_x, end_y = line[-1]
        if segment_distance(x, y, line) <= 5 or math.hypot(x - end_x, y - end_y) <= 12:
            return color
    if abs(y - 170) <= 1 and 52 <= x <= 204:
        return (90, 90, 90)
    return (33, 33, 33)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--prepare",
        action="store_true",
        help="Refresh the release DLL from the local Release build",
    )
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    manifest = json.loads((root / "package/manifest.json").read_text())
    assert len(manifest["description"]) <= 250
    assembly = root / "package/CasinoLedger.dll"
    if args.prepare:
        shutil.copyfile(root / "bin/Release/net6.0/CasinoLedger.dll", assembly)
    if not assembly.is_file():
        raise FileNotFoundError("Run a Release build, then python package.py --prepare")
    pixels = bytearray()
    for y in range(256):
        pixels.append(0)
        for x in range(256):
            pixels.extend(icon_pixel(x + 0.5, y + 0.5))
    icon = b"\x89PNG\r\n\x1a\n"
    for kind, data in (
        (b"IHDR", struct.pack(">2I5B", 256, 256, 8, 2, 0, 0, 0)),
        (b"IDAT", zlib.compress(bytes(pixels))),
        (b"IEND", b""),
    ):
        icon += (
            struct.pack(">I", len(data))
            + kind
            + data
            + struct.pack(">I", zlib.crc32(kind + data))
        )
    target = root / "dist" / f"CasinoLedger-{manifest['version_number']}.zip"
    target.parent.mkdir(exist_ok=True)
    with ZipFile(target, "w", ZIP_DEFLATED) as archive:
        archive.write(root / "package/manifest.json", "manifest.json")
        archive.write(root / "README.md", "README.md")
        archive.write(root / "LICENSE", "LICENSE")
        archive.writestr("icon.png", icon)
        archive.write(assembly, "Mods/CasinoLedger.dll")
    with ZipFile(target) as archive:
        assert archive.testzip() is None
        assert set(archive.namelist()) == {
            "manifest.json",
            "README.md",
            "LICENSE",
            "icon.png",
            "Mods/CasinoLedger.dll",
        }
    print(target)


if __name__ == "__main__":
    main()
