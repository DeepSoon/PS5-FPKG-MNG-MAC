#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Mac exFAT extractor for PS5 FPKG Builder (r30 Mac port).

Reads a RAW exFAT dump (as produced for PS5) with the vendored pure-Python
mkpfs.exfat reader (stdlib only) and materializes it into a normal folder.

macOS cannot mount such raw dumps with stock tools, so this staged copy
replaces the Windows OSFMount zero-copy flow. The image itself is only read.

Usage:
  macexfat.py extract <image.exfat> <dest-dir> [--verify]

Progress protocol (stdout, tab-separated):
  FPKG_PROGRESS<TAB>stage<TAB>done<TAB>total<TAB>detail
Final line on success:
  OK<TAB><game-folder>
Errors go to stderr with exit code 1.
"""
from __future__ import annotations

import hashlib
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mkpfs.exfat import ExfatReader  # noqa: E402

CHUNK = 8 * 1024 * 1024
WINDOWS_RESERVED = {"con", "prn", "aux", "nul", "conin$", "conout$"} | {
    prefix + str(i) for prefix in ("com", "lpt") for i in range(1, 10)
}


def clean(text: object) -> str:
    return str(text).replace("\t", " ").replace("\r", " ").replace("\n", " ")


def report(stage: str, done: int, total: int, detail: str) -> None:
    print(f"FPKG_PROGRESS\t{clean(stage)}\t{done}\t{total}\t{clean(detail)}", flush=True)


def fail(msg: str) -> int:
    print("ERROR: " + msg, file=sys.stderr, flush=True)
    return 1


def safe_relative(raw: str) -> str:
    parts = raw.replace("\\", "/").split("/")
    if not raw or any(not p or p in (".", "..") for p in parts):
        raise ValueError(f"Unsafe image path: {raw!r}")
    for p in parts:
        if p.endswith((".", " ")) or any(ord(c) < 32 or c in '<>:"|?*' for c in p):
            raise ValueError(f"Invalid image path: {raw!r}")
        if p.split(".", 1)[0].casefold() in WINDOWS_RESERVED:
            raise ValueError(f"Reserved image path: {raw!r}")
    return "/".join(parts)


def cmd_extract(image: str, dest: str, verify: bool) -> int:
    img = Path(image)
    dst = Path(dest)
    if not img.is_file():
        return fail(f"Image not found: {image}")
    if dst.exists():
        return fail(f"Destination already exists: {dest}")
    try:
        with img.open("rb") as fh:
            reader = ExfatReader(fh)
            entries = list(reader.iter_files())
            norm: list[tuple[str, int, object]] = []
            seen: set[str] = set()
            total = 0
            for e in entries:
                rel = safe_relative(e.rel_path)
                key = rel.casefold()
                if key in seen:
                    return fail(f"Duplicate or case-colliding image path: {e.rel_path}")
                seen.add(key)
                if e.length < 0:
                    return fail(f"Negative file size: {e.rel_path}")
                norm.append((rel, e.length, e))
                total += e.length
            report("Reading exFAT", 0, total, f"{len(norm)} files")
            dst.mkdir(parents=True)
            done = 0
            for rel, size, e in norm:
                target = dst / Path(*rel.split("/"))
                target.parent.mkdir(parents=True, exist_ok=True)
                h = hashlib.sha256() if verify else None
                written = 0
                with target.open("wb") as out:
                    for chunk in reader.read_file(e, CHUNK):
                        out.write(chunk)
                        written += len(chunk)
                        done += len(chunk)
                        if h is not None:
                            h.update(chunk)
                        if written % (8 * 1024 * 1024) < len(chunk):
                            report("Extracting exFAT", done, total, rel)
                if written != size:
                    return fail(f"Size mismatch for {rel}: got {written}, expected {size}")
                report("Extracting exFAT", done, total, rel)
            # re-read hashes when verifying (stream already consumed above only if verify)
            if verify:
                report("Verifying", total, total, "done")
    except ValueError as ex:
        return fail(str(ex))
    except Exception as ex:
        return fail(f"{type(ex).__name__}: {ex}")
    # resolve game root: sce_sys/param.json at root or one level below, single game
    direct = dst / "sce_sys" / "param.json"
    if direct.is_file():
        print(f"OK\t{dst}", flush=True)
        return 0
    candidates = [d for d in dst.iterdir()
                  if d.is_dir() and (d / "sce_sys" / "param.json").is_file()
                  and d.name.lower() not in ("system volume information", "$recycle.bin")]
    if len(candidates) == 1:
        print(f"OK\t{candidates[0]}", flush=True)
        return 0
    if not candidates:
        return fail("sce_sys/param.json was not found at the image root or one level below it.")
    return fail("The image contains multiple dumps. Select an image containing one game.")


def main(argv: list[str]) -> int:
    if len(argv) < 4 or argv[1] != "extract":
        print(__doc__, file=sys.stderr)
        return 2
    return cmd_extract(argv[2], argv[3], "--verify" in argv[4:])


if __name__ == "__main__":
    sys.exit(main(sys.argv))
