import argparse
import json
import shutil
import struct
import zlib
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


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
            shield = 40 <= x < 216 and 40 <= y < 216
            stripe = shield and (
                (112 <= x < 144 and 72 <= y < 184) or (80 <= x < 176 and 96 <= y < 128)
            )
            pixels.extend(
                (239, 244, 250) if stripe else (36, 150, 72) if shield else (17, 24, 39)
            )
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
