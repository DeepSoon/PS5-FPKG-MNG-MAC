# PS5-FPKG-MNG-MAC — 1.0.0-NG

**Made by Leite.**

Native macOS port of **PS5 FPKG Builder** (Windows release `1.4.5-pkg-file-editor-r30` included in this repo): inspect, browse, extract and **build** PS5 debug packages, plus a Trophy Unlocker — with a 100% native SwiftUI interface in PT-BR and English.

> The original tool is a Windows WinForms (.NET 9) application. This port reuses its portable `LibProsperoPkg` core and replaces every Windows-only piece (WinForms GUI, OSFMount, Sony SDK helpers) with native macOS equivalents. No Wine, no Rosetta, no virtual machines.

---

## What works on Mac

| Feature | Status |
|---|---|
| Reader (prepared folder + readable debug `.pkg`) | ✅ native |
| PKG file browser (list inner files without full extraction) | ✅ native |
| Extract one file / everything | ✅ native |
| **Build PKG from folder or raw exFAT dump** (FullDebug FIH, managed BuiltIn Kraken) | ✅ native |
| PKG → folder extractor | ✅ native |
| Trophy Unlocker (FTP :2121 + loader :9021, embedded payload) | ✅ native |
| Live progress: 0–100% bar, MB/s, MB done / remaining | ✅ native |

Still Windows-only (needs Sony `prospero-pub-cmd.exe` / 64-bit Windows drivers):

- Delta / backport update packages
- Zero-copy exFAT mount (macOS cannot mount raw dumps, so exFAT is staged through a temporary copy — the image is never modified)
- FFPKG / UFS2 tools

## Requirements

To **run** the app: macOS 14+ and nothing else. The `.app` bundles a self-contained arm64 .NET backend, `LibProsperoPkg`, and a stdlib-only Python exFAT reader (uses the system `python3`, no pip packages).

To **build** from source: Xcode Command Line Tools (`swiftc`) and the .NET 10 SDK.

## Quick start (from source)

```bash
bash mac/build-mac-app.sh
open "mac/PS5 FPKG Builder.app"
```

If macOS blocks the first launch: right-click → Open → Open.

## Backend CLI

The GUI drives `FpkgMac` (self-contained, `mac/FpkgMac/`). It also works standalone:

```bash
FpkgMac version
FpkgMac inspect-folder <game-folder>
FpkgMac inspect-pkg <file.pkg> [32-char-passcode]
FpkgMac list-pkg <file.pkg> [passcode] [--limit N]
FpkgMac extract-file <file.pkg> <path-in-pkg> <dest> [passcode]
FpkgMac extract-all <file.pkg> <new-folder> [passcode]
FpkgMac extract-pkg-folder <file.pkg> <new-folder> <temp> [passcode]
FpkgMac build-folder <game-folder> <out.pkg> <temp> [passcode] [--sdk 9.00] [--mode Application]
FpkgMac build-exfat <image.exfat> <out.pkg> <temp> [passcode] [--sdk 9.00] [--mode Application]
FpkgMac detect-image <image>
FpkgMac trophy-detect <ps5-ip>
FpkgMac trophy-list <ps5-ip> <PPSAxxxxx>
FpkgMac trophy-unlock <ps5-ip> <PPSAxxxxx> <trophy-id>
FpkgMac trophy-unlock-all <ps5-ip> <PPSAxxxxx>
```

Long operations stream `PROGRESS<TAB>stage<TAB>done-bytes<TAB>total-bytes<TAB>detail` lines on stdout, so any frontend can render live progress.

## How exFAT → PKG works on Mac

1. The raw exFAT dump is read with the bundled pure-Python reader (`mac/exfat/`, stdlib only) into a temporary folder (with SHA-256 verification).
2. `param.json` metadata seeds `ProsperoBuildOptions` (Content ID, Title, version, SDK override, `PlaintextNoAuth`, `KrakenBackend=BuiltIn`).
3. `LibProsperoPkg` builds a FullDebug FIH package; the output is type-checked, SHA-256 logged, and moved to the chosen `.pkg` path. Temp files are removed.

Why `BuiltIn`: the default `Automatic` backend falls back, off-Windows, to spans that even the bundled reader cannot decode (verified by round-trip tests). The fully managed `BuiltIn` backend round-trips byte-identically. The native Oodle path (`libScePubTools`, 64-bit Windows only) is unavailable on macOS by design.

## Repository layout

```text
mac/
  swift/            SwiftUI interface (I18n, Runner, Theme, App, PagesA/B)
  FpkgMac/          portable .NET backend (MacBridge/MacBuild/MacProgram + shared sources)
  exfat/            stdlib-only exFAT reader (macexfat.py + vendored mkpfs modules)
  build-mac-app.sh  one-shot script: publish backend + compile UI + assemble .app
  README / notes
source/             shared C# sources (used by both Windows and Mac builds)
assets/             bundled payloads + icon
tools/              Windows readers (PFS/UFS2) and the Python exFAT sources
tests/ verification/  contracts and checklists from the upstream release
THIRD_PARTY_NOTICES.md
```

Regenerable outputs (`mac/publish-arm64/`, `mac/FpkgMac/bin|obj`, `*.app`, `__pycache__`, `.DS_Store`) are git-ignored — build them with the script instead of committing them.

## Versioning and languages

- Mac port version: **1.0.0-NG** (About panel, sidebar, backend `version`).
- Interface languages: **PT-BR** (default) and **English**, switchable live; light/dark themes.

## Credits

Upstream tool by **Zer0Day** — X: https://x.com/_zer0day__ — Ko-fi: https://ko-fi.com/zzer0dayyy. Foundations: **Drakmor** / ShadowMountPlus; **svenGDK** (LibProsperoPKG, UFS2Tool); **EchoStretch** / **cragson** (A53); **kerrdec97** (ps5-exfat-builder); **MkPFS** / Lazy_MkPFS. See `THIRD_PARTY_NOTICES.md`. This Mac port, the SwiftUI interface and the portable build pipeline: **Made by Leite**.
