#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Mac AMPR (LZ4) driver for PS5 FPKG Builder 1.0.0-NG. Made by Leite.

Compresses a game folder (or a raw exFAT dump) into seekable AMPR LZ4 packs
and extracts them back, using the reference tools from drakmor/ampr_emu
(GPLv3, vendored here with their license):
  - build_ampr_index.py  -> AMPRIDX3 file index
  - ampr_pack.py         -> pack / unpack / verify / list (AMPRPAK4)

Packing here mirrors the general flow popularized by Nazky's Lazy_AMPR
(folder/exFAT in, LZ4 level 0-12, .pak volumes + index out), but every line
in this driver and its TOML generator is original to this project.

Level mapping (ampr pack config only accepts 1-12):
  0        -> fast mode, level 1 (fastest, least compression)
  1..4     -> fast mode, given level
  5..12    -> hc (high-compression) mode, given level

Usage:
  macampr.py pack <folder|.exfat> <out-dir> [--level 0-12] [--workers N]
  macampr.py unpack <pack-dir> <out-dir>
  macampr.py list <pack-dir>

Progress protocol (stdout, tab-separated):
  FPKG_PROGRESS<TAB>stage<TAB>done<TAB>total<TAB>detail
Final line on success:
  OK<TAB><path>
Errors go to stderr with exit code 1.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
# vendored wheels first (offline bundle), then sibling exFAT reader sources
sys.path.insert(0, str(HERE / "site-packages"))
sys.path.insert(0, str(HERE.parent / "exfat"))

try:
    import lz4.block  # noqa: F401  (proved present; ampr tools import it lazily)
except ImportError:
    print("ERROR: vendored lz4 is missing from ampr/site-packages.", file=sys.stderr)
    sys.exit(1)

PROGRESS_RE = re.compile(r"\[(\w+)\s+(\d+)%\]")

# Never compress these: executables, system metadata, already-compressed
# media/archives, and AMPR's own files. Everything else is packed.
LOOSE_EXCLUDE = [
    "eboot.bin", "**/eboot.bin",
    "*.elf", "**/*.elf",
    "*.self", "**/*.self",
    "*.prx", "**/*.prx",
    "*.sprx", "**/*.sprx",
    "sce_sys/**", "**/sce_sys/**",
    "sce_module/**", "**/sce_module/**",
    "system/**", "**/system/**",
    "trophy*/**", "**/trophy*/**",
    "ampr_emu.index", "**/ampr_emu.index",
    "ampr_assets.index*", "**/ampr_assets.index*",
    "ampr_assets-*.pak", "**/ampr_assets-*.pak",
    "*.png", "**/*.png",
    "*.jpg", "**/*.jpeg", "**/*.jpg",
    "*.webp", "**/*.webp",
    "*.gif", "**/*.gif",
    "*.dds", "**/*.dds",
    "*.mp4", "**/*.mp4",
    "*.usm", "**/*.usm",
    "*.bnk", "**/*.bnk",
    "*.wem", "**/*.wem",
    "*.mp3", "**/*.mp3",
    "*.ogg", "**/*.ogg",
    "*.zip", "**/*.zip",
    "*.7z", "**/*.7z",
    "*.rar", "**/*.rar",
    "*.gz", "**/*.gz",
    "*.xz", "**/*.xz",
    "*.zst", "**/*.zst",
    "*.lz4", "**/*.lz4",
    "*.pfs", "**/*.pfs",
    "*.pkg", "**/*.pkg",
]


def clean(text: object) -> str:
    return str(text).replace("\t", " ").replace("\r", " ").replace("\n", " ")


def report(stage: str, done: int, total: int, detail: str) -> None:
    print(f"FPKG_PROGRESS\t{clean(stage)}\t{done}\t{total}\t{clean(detail)}", flush=True)


def fail(msg: str) -> int:
    print("ERROR: " + msg, file=sys.stderr, flush=True)
    return 1


def is_exfat_image(path: Path) -> bool:
    try:
        with path.open("rb") as fh:
            return fh.read(16)[3:11] == b"EXFAT   "
    except OSError:
        return False


def folder_bytes(root: Path) -> int:
    total = 0
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in filenames:
            try:
                total += (Path(dirpath) / name).stat().st_size
            except OSError:
                pass
    return total


def map_level(level: int) -> tuple[str, int]:
    if level <= 0:
        return ("fast", 1)
    if level <= 4:
        return ("fast", level)
    return ("hc", min(level, 12))


def write_toml(path: Path, mode: str, level: int, workers: int) -> None:
    excludes = "\n".join(f'  "{p}",' for p in LOOSE_EXCLUDE)
    path.write_text(
        "[pack]\n"
        'index_name = "ampr_assets.index"\n'
        'pack_pattern = "ampr_assets-{group}-lane{lane:02d}-vol{volume:02d}-{id:03d}.pak"\n'
        'default_action = "loose"\n'
        'default_block_size = "64KiB"\n'
        f"workers = {workers}\n"
        f'compression_mode = "{mode}"\n'
        f"compression_level = {level}\n"
        "deduplicate = true\n"
        "preserve_mtime = true\n"
        "\n"
        "[groups.assets]\n"
        "pack_count = 4\n"
        'assignment = "balanced"\n'
        'max_pack_size = "8GiB"\n'
        "\n"
        "[[rule]]\n"
        'action = "compress"\n'
        'include = ["**"]\n'
        "exclude = [\n"
        f"{excludes}\n"
        "]\n"
        'block_size = "64KiB"\n'
        'group = "assets"\n'
        'layout = "mixed"\n',
        encoding="utf-8",
    )


def child_env() -> dict:
    """Environment for ampr tool children: vendored wheels first."""
    env = dict(os.environ)
    sp = str(HERE / "site-packages")
    prev = env.get("PYTHONPATH", "")
    env["PYTHONPATH"] = sp + (os.pathsep + prev if prev else "")
    return env


def run_tool(args: list[str], stage: str, total: int) -> int:
    """Run an ampr tool, translating [Stage NN%] lines into progress."""
    proc = subprocess.Popen(
        [sys.executable, *args],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,
        env=child_env(),
    )
    assert proc.stdout is not None
    last_pct = -1
    for line in proc.stdout:
        line = line.rstrip("\n")
        m = PROGRESS_RE.search(line)
        if m and total > 0:
            pct = max(0, min(100, int(m.group(2))))
            if pct != last_pct:
                last_pct = pct
                report(stage, pct * total // 100, total, m.group(1))
        elif line.strip():
            print(line, flush=True)
    rc = proc.wait()
    if rc != 0:
        return fail(f"{Path(args[0]).name} exited with code {rc}.")
    return 0


def extract_exfat(image: Path, dest: Path) -> Path | None:
    """Stage a raw exFAT dump with the bundled pure-Python reader."""
    from mkpfs.exfat import ExfatReader

    with image.open("rb") as fh:
        reader = ExfatReader(fh)
        entries = list(reader.iter_files())
        total = sum(e.length for e in entries)
        dest.mkdir(parents=True)
        done = 0
        report("Reading exFAT", 0, total, f"{len(entries)} files")
        for e in entries:
            rel = e.rel_path.replace("\\", "/")
            parts = rel.split("/")
            if not rel or any(not p or p in (".", "..") for p in parts):
                print(f"ERROR: unsafe image path: {e.rel_path!r}", file=sys.stderr, flush=True)
                return None
            target = dest.joinpath(*parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            written = 0
            with target.open("wb") as out:
                for chunk in reader.read_file(e, 8 * 1024 * 1024):
                    out.write(chunk)
                    written += len(chunk)
                    done += len(chunk)
            if written != e.length:
                print(f"ERROR: size mismatch for {rel}", file=sys.stderr, flush=True)
                return None
            report("Reading exFAT", done, total, rel)
    direct = dest / "sce_sys" / "param.json"
    if direct.is_file():
        return dest
    cands = [d for d in dest.iterdir()
             if d.is_dir() and (d / "sce_sys" / "param.json").is_file()]
    if len(cands) == 1:
        return cands[0]
    print("ERROR: game root (sce_sys/param.json) not found in image.", file=sys.stderr, flush=True)
    return None


def cmd_pack(src: str, out: str, level: int, workers: int) -> int:
    if not 0 <= level <= 12:
        return fail("Level must be between 0 and 12.")
    mode, cfg_level = map_level(level)
    src_p, out_p = Path(src), Path(out)
    if out_p.exists():
        return fail(f"Output already exists: {out}")
    work: Path | None = None
    if src_p.is_dir():
        game_root = src_p
    elif src_p.is_file() and is_exfat_image(src_p):
        work = out_p.parent / (out_p.name + ".stage")
        if work.exists():
            return fail(f"Staging folder already exists: {work}")
        game_root = extract_exfat(src_p, work)
        if game_root is None:
            return 1
    else:
        return fail(f"Source must be a game folder or a raw .exfat image: {src}")
    try:
        total = folder_bytes(game_root)
        report("Preparing", 0, total, f"LZ4 {mode}/{cfg_level}")
        index = out_p.parent / (out_p.name + ".ampr_emu.index")
        rc = run_tool([str(HERE / "build_ampr_index.py"), str(game_root), "-o", str(index)],
                      "Indexing", total)
        if rc != 0:
            return rc
        toml_path = out_p.parent / (out_p.name + ".pack.toml")
        write_toml(toml_path, mode, cfg_level, workers)
        out_p.mkdir(parents=True)
        rc = run_tool([str(HERE / "ampr_pack.py"), "pack",
                       "--root", str(game_root),
                       "--ampr-index", str(index),
                       "--output", str(out_p),
                       "--config", str(toml_path),
                       "--workers", str(workers)], "Packing", total)
        if rc != 0:
            return rc
        print(f"Index: {index}", flush=True)
        rc = run_tool([str(HERE / "ampr_pack.py"), "verify",
                       "--index", str(out_p / "ampr_assets.index"),
                       "--root", str(game_root)], "Verifying", total)
        if rc != 0:
            return rc
        report("Packing", total, total, "done")
        print(f"OK\t{out_p}", flush=True)
        return 0
    finally:
        # staging is transient; the pack output + index next to it are the product
        if work is not None:
            import shutil
            shutil.rmtree(work, ignore_errors=True)


def find_index(packdir: Path) -> Path | None:
    direct = packdir / "ampr_assets.index"
    if direct.is_file():
        return direct
    found = sorted(packdir.glob("*.index"))
    return found[0] if found else None


def cmd_unpack(packdir: str, out: str) -> int:
    pack_p, out_p = Path(packdir), Path(out)
    index = find_index(pack_p)
    if index is None:
        return fail(f"No pack index (*.index) in: {packdir}")
    if out_p.exists():
        return fail(f"Output already exists: {out}")
    out_p.mkdir(parents=True)
    rc = run_tool([str(HERE / "ampr_pack.py"), "unpack",
                   "--index", str(index),
                   "--output", str(out_p),
                   "--overwrite"], "Extracting", 0)
    if rc != 0:
        return rc
    print(f"OK\t{out_p}", flush=True)
    return 0


def cmd_list(packdir: str) -> int:
    pack_p = Path(packdir)
    index = find_index(pack_p)
    if index is None:
        return fail(f"No pack index (*.index) in: {packdir}")
    proc = subprocess.run([sys.executable, str(HERE / "ampr_pack.py"), "list",
                           "--index", str(index)],
                          capture_output=True, text=True, env=child_env())
    print(proc.stdout, end="")
    if proc.returncode != 0:
        print(proc.stdout + proc.stderr, file=sys.stderr)
    return proc.returncode


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2
    cmd = argv[1]
    if cmd == "pack" and len(argv) >= 4:
        level, workers = 9, min(8, os.cpu_count() or 4)
        rest = argv[4:]
        i = 0
        while i < len(rest):
            a = rest[i]
            def val(j):
                s = a.split("=", 1)[1] if "=" in a else (rest[j + 1] if j + 1 < len(rest) else "")
                return s
            if a == "--level" or a.startswith("--level="):
                level = int(val(i)); i += 1 if "=" in a else 2; continue
            if a == "--workers" or a.startswith("--workers="):
                workers = int(val(i)); i += 1 if "=" in a else 2; continue
            i += 1
        return cmd_pack(argv[2], argv[3], level, workers)
    if cmd == "unpack" and len(argv) >= 4:
        return cmd_unpack(argv[2], argv[3])
    if cmd == "list" and len(argv) >= 3:
        return cmd_list(argv[2])
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
