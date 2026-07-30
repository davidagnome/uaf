#!/usr/bin/env python3
"""Regenerates dotnet/tests/UAF.Media.Tests/Assets/png-oracle.txt.

An independent decode of every PNG in reference/, via Pillow (which wraps libpng), used as the
oracle for UAF.Media's hand-written PngDecoder. Same idea as the C++ oracle used for the
serialization port: decode the real files with an implementation that shares no code with ours,
and diff.

The digest is over RGB bytes, not RGBA, because the engine strips alpha
(cdx/cdximagepng.cpp:105,124). Gamma is pre-applied here when it is not a no-op, since Pillow
ignores gAMA entirely -- see the caveat below.

What this does and does not prove
---------------------------------
It proves the inflate, the five row filters, the palette expansion and the pixel packing, against
1312 real files. It does NOT independently confirm the gamma *convention* -- whether the exponent
is 1/(file_gamma x screen_gamma) or its reciprocal. Both collapse to the identity for 1302 of the
1312 files, so no corpus-wide test can distinguish them; the choice rests on libpng's
png_reciprocal2() and on the physical derivation in PngDecoder's docs. The gamma_table() below is
written independently of the C# only in the sense that the arithmetic is re-derived, so a mismatch
catches a rounding or ordering slip, not a wrong convention.

Run from the repository root:  python3 tools/png-oracle.py
Requires: pip install Pillow
"""

import hashlib
import pathlib
import struct
import sys

from PIL import Image

# cdx/cdximagepng.cpp:53 and :113.
SCREEN_GAMMA = 2.2
DEFAULT_FILE_GAMMA = 0.45455
# libpng's PNG_GAMMA_THRESHOLD.
THRESHOLD = 0.05

OUTPUT = pathlib.Path("dotnet/tests/UAF.Media.Tests/Assets/png-oracle.txt")

HEADER = (
    "# Independent PNG decode digests, generated from Pillow (libpng) by\n"
    "# tools/png-oracle.py. Format: relative-path|width|height|sha256-of-RGB-bytes.\n"
    "# RGB, not RGBA: the engine strips alpha. Gamma is pre-applied where it is not a no-op.\n"
)


def gamma_table(file_gamma):
    """The 256-entry table, or None when the correction is the identity."""
    if file_gamma <= 0:
        file_gamma = DEFAULT_FILE_GAMMA
    product = file_gamma * SCREEN_GAMMA
    if abs(product - 1.0) <= THRESHOLD:
        return None
    exponent = 1.0 / product
    table = bytearray(256)
    for i in range(256):
        # int(v + 0.5) is half-away-from-zero for non-negative v, matching Math.Round's default.
        table[i] = min(255, max(0, int(255.0 * ((i / 255.0) ** exponent) + 0.5)))
    return bytes(table)


def read_gama(data):
    """The gAMA chunk value, or 0.0 when absent. Zero is also what an invalid chunk yields."""
    offset = 8
    while offset + 8 <= len(data):
        length, tag = struct.unpack(">I4s", data[offset:offset + 8])
        if tag == b"gAMA":
            return struct.unpack(">I", data[offset + 8:offset + 12])[0] / 100000.0
        if tag == b"IEND":
            break
        offset += 12 + length
    return 0.0


def main():
    root = pathlib.Path("reference")
    if not root.is_dir():
        sys.exit("run from the repository root; reference/ not found")

    rows, skipped = [], []
    for path in sorted(root.rglob("*")):
        if path.suffix.lower() != ".png" or not path.is_file():
            continue
        data = path.read_bytes()
        if data[:8] != b"\x89PNG\r\n\x1a\n":
            continue
        if data[28] != 0:
            # Interlaced. PngDecoder rejects these outright; none exist today.
            skipped.append(f"{path} (interlaced)")
            continue
        try:
            image = Image.open(path).convert("RGB")
        except Exception as error:                       # noqa: BLE001 - report and continue
            skipped.append(f"{path} ({error})")
            continue

        raw = image.tobytes()
        table = gamma_table(read_gama(data))
        if table is not None:
            raw = bytes(table[b] for b in raw)

        rel = str(path.relative_to(root)).replace("\\", "/")
        rows.append(f"{rel}|{image.width}|{image.height}|{hashlib.sha256(raw).hexdigest()}")

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(HEADER + "\n".join(rows) + "\n")
    print(f"wrote {len(rows)} digests to {OUTPUT}")
    for entry in skipped:
        print(f"  skipped {entry}")


if __name__ == "__main__":
    main()
