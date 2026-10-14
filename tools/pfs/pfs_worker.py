"""Streaming FFPFSC/PFS extraction bridge for Zer0Day's PKG GUI.

MkPFS sources next to this file are preserved from the supplied Image Builder.
This bridge confines output paths, checks lengths and copied hashes, reports
progress, and follows nested exFAT/PFS images before handing a dump to the GUI.
"""
from __future__ import annotations

import hashlib
import os
from pathlib import Path
import shutil
import struct
import sys
import time
import xml.etree.ElementTree as ET

from mkpfs import pfs
from mkpfs.exfat import ExfatReader

VERSION = "1.4.3"
RESERVE = 64 * 1024 * 1024
CHUNK = 4 * 1024 * 1024
WINDOWS_RESERVED = {"con", "prn", "aux", "nul", "conin$", "conout$"} | {
    prefix + str(i) for prefix in ("com", "lpt") for i in range(1, 10)
}


def clean(text: object) -> str:
    return str(text).replace("\t", " ").replace("\r", " ").replace("\n", " ")


def report(stage: str, done: int, total: int, detail: str) -> None:
    print(f"FPKG_PROGRESS\t{clean(stage)}\t{done}\t{total}\t{clean(detail)}", flush=True)


def safe_relative(raw: str) -> str:
    # Validate using Windows rules even when the regression suite runs on Linux.
    parts = raw.replace("\\", "/").split("/")
    if not raw or any(not part or part in (".", "..") for part in parts):
        raise ValueError(f"Unsafe image path: {raw!r}")
    for part in parts:
        if part.endswith((".", " ")) or any(ord(c) < 32 or c in '<>:"|?*' for c in part):
            raise ValueError(f"Invalid Windows image path: {raw!r}")
        if part.split(".", 1)[0].casefold() in WINDOWS_RESERVED:
            raise ValueError(f"Reserved Windows image path: {raw!r}")
    return "/".join(parts)


def validate_paths(directories: list[str], files: list[tuple[str, int, object]]) -> None:
    seen: set[str] = set()
    file_names: set[str] = set()
    for raw in directories + [f[0] for f in files]:
        key = safe_relative(raw).casefold()
        if key in seen:
            raise ValueError(f"Duplicate or case-colliding image path: {raw}")
        seen.add(key)
    for raw, size, _ in files:
        if size < 0:
            raise ValueError(f"Negative file size: {raw}")
        file_names.add(safe_relative(raw).casefold())
    for key in seen:
        parts = key.split("/")
        for index in range(1, len(parts)):
            if "/".join(parts[:index]) in file_names:
                raise ValueError(f"An image file is also used as a directory: {key}")


def detect(handle) -> str:
    handle.seek(0)
    header = handle.read(16)
    if len(header) >= 11 and header[3:11] == b"EXFAT   ":
        return "exfat"
    if len(header) == 16 and struct.unpack("<qq", header)[1] == pfs.consts.PFS_MAGIC:
        return "pfs"
    return "unknown"


def length(handle) -> int:
    old = handle.tell()
    handle.seek(0, 2)
    size = handle.tell()
    handle.seek(old)
    return size


def validate_pfs_header(image: Path) -> None:
    with image.open("rb") as fh:
        header = pfs.parse_image_header(fh)
    if header.version not in (1, 2) or header.magic != pfs.consts.PFS_MAGIC:
        raise ValueError("Unsupported PFS version or magic.")
    size = header.block_size
    if not 4096 <= size <= 1048576 or size & (size - 1):
        raise ValueError("Invalid PFS block size.")
    if header.nblock != 1 or header.ndblock <= 0 or header.ndblock * size > image.stat().st_size:
        raise ValueError("Truncated PFS image: the declared blocks exceed the file.")
    if not 0 < header.dinode_count <= 2_000_000 or not 0 < header.dinode_block_count < header.ndblock:
        raise ValueError("Invalid or unsupported PFS inode table size.")
    if header.mode & pfs.consts.PFS_MODE_ENCRYPTED:
        raise ValueError("Encrypted PFS images are not supported. Use a plaintext MkPFS image from your dump.")


def validate_exfat(handle) -> None:
    handle.seek(0)
    h = handle.read(512)
    if len(h) != 512 or h[3:11] != b"EXFAT   " or h[510:512] != b"\x55\xaa":
        raise ValueError("Invalid inner exFAT boot sector.")
    sector_shift, cluster_shift = h[108], h[109]
    if not 9 <= sector_shift <= 12 or sector_shift + cluster_shift > 25 or h[110] != 1:
        raise ValueError("Unsupported inner exFAT geometry.")
    volume = struct.unpack_from("<Q", h, 72)[0]
    fat, fat_size, heap, clusters, root = struct.unpack_from("<IIIII", h, 80)
    if not volume or volume * (1 << sector_shift) > length(handle):
        raise ValueError("Truncated inner exFAT image.")
    if (fat < 24 or not fat_size or fat + fat_size > heap or not clusters or
            not 2 <= root < clusters + 2 or heap + (clusters << cluster_shift) > volume):
        raise ValueError("Invalid inner exFAT cluster layout.")


class CheckedLogicalView(pfs._LogicalFileView):
    """Bound the on-demand compressed view to its validated source inode."""

    def __init__(self, fh, header, inode):
        stored_end = inode.db[0] * header.block_size + inode.stored_size
        if inode.db[0] < 0 or stored_end > length(fh):
            raise ValueError("Inner image extent exceeds its PFS container.")
        if inode.is_compressed:
            fh.seek(inode.db[0] * header.block_size)
            lbs, count, table, data_start, logical = pfs._parse_pfsc_header(fh.read(48))
            if (table + (count + 1) * 8 > inode.stored_size or (count + 1) * 8 > 256 * 1024 * 1024 or
                    data_start > inode.stored_size or not inode.logical_size <= logical < inode.logical_size + lbs):
                raise ValueError("Invalid inner PFSC offset table or logical size.")
        super().__init__(fh, header, inode)
        if inode.is_compressed:
            if self._offsets[0] < data_start or self._offsets[-1] > inode.stored_size:
                raise ValueError("Inner PFSC data offsets exceed the inode.")
            if any(not 0 < b - a <= self._lbs for a, b in zip(self._offsets, self._offsets[1:])):
                raise ValueError("Invalid inner PFSC compressed block length.")

    def _decode_block(self, index: int) -> bytes:
        block = super()._decode_block(index)
        if len(block) != self._lbs:
            raise ValueError(f"Truncated inner PFSC block {index}.")
        return block

    def seek(self, offset: int, whence: int = 0) -> int:
        if whence not in (0, 1, 2):
            raise ValueError("Invalid seek origin.")
        target = offset + (self._pos if whence == 1 else self._size if whence == 2 else 0)
        if not 0 <= target <= self._size:
            raise ValueError("Read outside the inner image.")
        return super().seek(offset, whence)


def write_tree(destination: Path, directories, files, chunks, verify: bool) -> None:
    validate_paths(directories, files)
    total = sum(size for _, size, _ in files)
    if shutil.disk_usage(destination.parent).free < total + RESERVE:
        raise OSError(f"Not enough temporary space: extraction needs {total + RESERVE:,} bytes.")
    destination.mkdir(exist_ok=False)
    for relative in sorted(directories, key=lambda value: value.count("/")):
        (destination / safe_relative(relative)).mkdir(parents=True, exist_ok=True)
    done = 0
    last = 0.0
    print(f"Extracting {len(files)} files ({total:,} logical bytes).", flush=True)
    for relative, expected, entry in files:
        target = destination / safe_relative(relative)
        target.parent.mkdir(parents=True, exist_ok=True)
        digest = hashlib.sha256() if verify else None
        count = 0
        report("Extracting FFPFSC / PFS", done, total, relative)
        with target.open("xb") as output:
            for chunk in chunks(entry):
                count += len(chunk)
                if count > expected:
                    raise ValueError(f"Decoded file exceeds its declared size: {relative}")
                output.write(chunk)
                if digest is not None:
                    digest.update(chunk)
                done += len(chunk)
                if time.monotonic() - last >= 0.2:
                    report("Extracting FFPFSC / PFS", done, total, relative)
                    last = time.monotonic()
        if count != expected:
            raise ValueError(f"Incomplete file: {relative}; expected {expected}, got {count} bytes.")
        if verify:
            actual = hashlib.sha256()
            verified = 0
            with target.open("rb") as written:
                while True:
                    chunk = written.read(CHUNK)
                    if not chunk:
                        break
                    actual.update(chunk)
                    verified += len(chunk)
                    if time.monotonic() - last >= 0.2:
                        report("Verifying extracted file", verified, expected, relative)
                        last = time.monotonic()
            if digest.digest() != actual.digest():
                raise ValueError(f"SHA-256 copy verification failed: {relative}")
    report("Extraction verified" if verify else "Extraction complete", done, total, str(destination))


def extract_exfat(handle, destination: Path, verify: bool) -> None:
    validate_exfat(handle)
    reader = ExfatReader(handle)
    dirs, entries = pfs._flatten_exfat_entries(reader.root_entries())
    directories = [entry.rel_path for entry in dirs]
    files = [(entry.rel_path, entry.length, entry) for entry in entries]
    write_tree(destination, directories, files, reader.read_file, verify)


def unpack_layer(image: Path, destination: Path, verify: bool) -> None:
    with image.open("rb") as fh:
        kind = detect(fh)
        if kind == "exfat":
            print("Reading the nested exFAT image without mounting.", flush=True)
            extract_exfat(fh, destination, verify)
            return
    if kind != "pfs":
        raise ValueError("The nested image is not PFS or exFAT. UFS2/FFPKG wrappers are not supported by this release.")
    validate_pfs_header(image)
    report("Inspecting PFS", 0, 0, image.name)
    inspection = pfs.inspect_pfs_image(image, verify_payloads=False)
    if inspection.errors or inspection.header is None:
        raise ValueError("Invalid PFS structure: " + "; ".join(inspection.errors[:12]))
    for warning in inspection.warnings[:12]:
        print("PFS: " + clean(warning), flush=True)
    # The Image Builder's single-file packer wraps exFAT in one contiguous inode.
    # Decode only clusters being read; do not materialize a huge inner disk image.
    if len(inspection.file_inodes) == 1:
        inode = inspection.inodes[next(iter(inspection.file_inodes.values()))]
        if not inode.db_sig and not inode.ib_sig and inode.blocks > 0 and inode.logical_size > 0:
            with image.open("rb") as fh:
                view = CheckedLogicalView(fh, inspection.header, inode)
                if detect(view) == "exfat":
                    print("FFPFSC contains exFAT: streaming game files directly from compressed blocks.", flush=True)
                    extract_exfat(view, destination, verify)
                    return
    directories = [name for name in inspection.dir_inodes if name]
    files = [(name, inspection.inodes[index].logical_size, inspection.inodes[index])
             for name, index in sorted(inspection.file_inodes.items())]
    with image.open("rb") as fh:
        write_tree(destination, directories, files,
                   lambda inode: pfs.iter_inode_logical_blocks(fh, inspection.header, inode), verify)


def game_root(root: Path) -> Path | None:
    if (root / "sce_sys" / "param.json").is_file():
        return root
    candidates = [child for child in root.iterdir() if child.is_dir() and
                  (child / "sce_sys" / "param.json").is_file()]
    if len(candidates) > 1:
        raise ValueError("The image contains multiple games. Select an image containing one game.")
    return candidates[0] if candidates else None


def extract(image: Path, destination: Path, result_file: Path, verify: bool) -> Path:
    image = image.resolve(strict=True)
    destination = destination.absolute()
    if destination.exists() or result_file.exists():
        raise FileExistsError("The extraction destination or result file already exists.")
    destination.mkdir()
    current = image
    previous_layer = None
    for depth in range(4):
        layer = destination / f"layer-{depth}"
        unpack_layer(current, layer, verify)
        if previous_layer is not None:
            shutil.rmtree(previous_layer)
        found = game_root(layer)
        if found is not None:
            document = ET.Element("extraction", version=VERSION)
            ET.SubElement(document, "dump").text = str(found.resolve())
            with result_file.open("xb") as result:
                ET.ElementTree(document).write(result, encoding="utf-8", xml_declaration=True)
            print("Game dump ready for PKG creation.", flush=True)
            return found
        candidates = []
        for file in layer.rglob("*"):
            if file.is_file():
                with file.open("rb") as fh:
                    if detect(fh) in ("pfs", "exfat"):
                        candidates.append(file)
        if len(candidates) != 1:
            raise ValueError("No unique game dump was found (sce_sys/param.json is missing). "
                             "Supported: PFS game files and FFPFSC containing exFAT/PFS. "
                             "UFS2/FFPKG wrappers require a separate extraction.")
        current = candidates[0]
        previous_layer = layer
        print("Following nested image: " + current.name, flush=True)
    raise ValueError("Too many nested image layers.")



def _write_selected(destination: Path, selected, chunks) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    for relative, expected, entry in selected:
        target = destination / safe_relative(relative)
        target.parent.mkdir(parents=True, exist_ok=True)
        count = 0
        with target.open("wb") as output:
            for chunk in chunks(entry):
                if count + len(chunk) > expected:
                    chunk = chunk[: expected - count]
                output.write(chunk)
                count += len(chunk)
                if count >= expected:
                    break
        if count != expected:
            raise ValueError(f"Incomplete preview file: {relative}")

def _sce_sys_selection(paths):
    normalized = [(p.replace("\\", "/").lstrip("/"), size, entry) for p, size, entry in paths]
    params = [p for p, _, _ in normalized if p.lower().endswith("sce_sys/param.json") or p.lower() == "sce_sys/param.json"]
    if len(params) != 1:
        raise ValueError("Preview requires exactly one sce_sys/param.json.")
    param = params[0]
    prefix = param[:-len("sce_sys/param.json")]
    wanted = {"param.json", "icon0.png", "pic0.png", "pic1.png", "pic2.png"}
    result=[]
    for p,size,entry in normalized:
        low=p.lower()
        marker=(prefix+"sce_sys/").lower()
        if low.startswith(marker) and low[len(marker):] in wanted:
            rel="sce_sys/"+p[len(prefix+"sce_sys/"): ]
            result.append((rel,size,entry))
    return result

def preview(image: Path, destination: Path) -> Path:
    image = image.resolve(strict=True)
    with image.open("rb") as fh:
        kind = detect(fh)
        if kind == "exfat":
            validate_exfat(fh)
            reader = ExfatReader(fh)
            _, entries = pfs._flatten_exfat_entries(reader.root_entries())
            selected = _sce_sys_selection([(e.rel_path, e.length, e) for e in entries])
            _write_selected(destination, selected, reader.read_file)
            return destination
    if kind != "pfs":
        raise ValueError("Preview source is not PFS/exFAT.")
    validate_pfs_header(image)
    inspection = pfs.inspect_pfs_image(image, verify_payloads=False)
    if inspection.errors or inspection.header is None:
        raise ValueError("Invalid PFS structure: " + "; ".join(inspection.errors[:12]))
    if len(inspection.file_inodes) == 1:
        inode = inspection.inodes[next(iter(inspection.file_inodes.values()))]
        if not inode.db_sig and not inode.ib_sig and inode.blocks > 0 and inode.logical_size > 0:
            with image.open("rb") as fh:
                view = CheckedLogicalView(fh, inspection.header, inode)
                if detect(view) == "exfat":
                    validate_exfat(view)
                    reader = ExfatReader(view)
                    _, entries = pfs._flatten_exfat_entries(reader.root_entries())
                    selected = _sce_sys_selection([(e.rel_path, e.length, e) for e in entries])
                    _write_selected(destination, selected, reader.read_file)
                    return destination
    files=[(name, inspection.inodes[index].logical_size, inspection.inodes[index]) for name,index in sorted(inspection.file_inodes.items())]
    selected=_sce_sys_selection(files)
    with image.open("rb") as fh:
        _write_selected(destination, selected, lambda inode: pfs.iter_inode_logical_blocks(fh, inspection.header, inode))
    return destination

def main(argv: list[str]) -> int:
    if argv == ["--probe"]:
        print("FPKG_PFS_READY " + VERSION, flush=True)
        return 0
    if len(argv) == 3 and argv[0] == "preview":
        try:
            preview(Path(argv[1]), Path(argv[2]))
            print("FPKG_PREVIEW_READY", flush=True)
            return 0
        except Exception as error:
            print("PFS ERROR: " + clean(error), flush=True)
            return 1
    if len(argv) != 5 or argv[0] != "extract" or argv[4] not in ("0", "1"):
        print("Usage: pfs_worker.py extract IMAGE NEW_DIRECTORY RESULT_XML VERIFY_0_OR_1 | preview IMAGE NEW_DIRECTORY", flush=True)
        return 2
    try:
        extract(Path(argv[1]), Path(argv[2]), Path(argv[3]), argv[4] == "1")
        return 0
    except Exception as error:
        print("PFS ERROR: " + clean(error), flush=True)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
