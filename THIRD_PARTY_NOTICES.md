# Third-party notices

## LibProsperoPKG
Upstream: SvenGDK / LibProsperoPKG. Retains its original copyright and license.

## UFS2Tool
Upstream: https://github.com/SvenGDK/UFS2Tool
License: BSD 2-Clause.
Used for read/extract support for `.ffpkg` / UFS1/UFS2 images.

## MkPFS / Lazy_MkPFS
Used by the bundled FFPFS/FFPFSC/PFS reader. Original source and license files are preserved under `tools/pfs`.

## exFAT workflow reference
kerrdec97 / ps5-exfat-builder.

## AMPR (LZ4) reference tools
Upstream: drakmor / ampr_emu (tools: `ampr_pack.py`, `ampr_pack_format.py`,
`build_ampr_index.py`). License: GNU GPLv3 — full text vendored under
`mac/ampr/LICENSE.ampr_emu`; the referenced sources ship in `mac/ampr/`.
General pack flow inspired by Nazky / Lazy_AMPR (no Lazy_AMPR code vendored).
Python `lz4` wheel (BSD-licensed, Yann Collet / upstream contributors) is
vendored under `mac/ampr/site-packages/` so the app runs fully offline.

All third-party components retain their respective authorship and license terms.

## PS5 Trophy Unlocker payload component

The Trophy Unlocker page uses the PS5 trophy payload supplied by the user as part
of TrophyUnlocker.exe / PS5-Trophy-Unlocker--BETA2. The payload is embedded as a
build resource so no external ELF is required at runtime. The corresponding
user-supplied GPLv3 source archive and license are included under
`third_party/trophy-unlocker/`.

The Windows Trophy UI, FTP catalogue reader, Trophy2 UCP metadata parser and
TRPTITLE read-only status parser in this package are separate C# integration
code.
