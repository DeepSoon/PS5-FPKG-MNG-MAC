# PS5 FPKG Builder 1.4.5

## r30 — Extract file from PKG

- Lists inner files directly from supported debug FIH/PPR/NAPS packages, with bounded block decoding and a small cache.
- Right-click **Extract to...** exports the selected files; import replacements or add files/folders to a staged edit session.
- Save only the edits as a small project, rebuild a new full PKG, or create an SDK update from the base package without an existing game folder.
- PKG creation and SDK updates still require reading the full game; only browsing, selected extraction and saving the edit project avoid that full read.
- Fixes backport inspection counting a large supplement as CNT metadata. The base is opened read-only; completed outputs use separate names.
- Internal revision: `1.4.4-pkg-file-editor-r30`. Italian steps: `GUIDA-PKG-EDITOR-ITA.txt`.
- C# compilation and IO regression tests pass. Windows UI, real PKG and Sony SDK execution have not been tested in this environment.

## r29 — Backport Update editor

- Added a **Backport Update** section for choosing a base `.pkg`, its matching full game folder, and the small backport folder.
- View the backport file list, select files, replace them, add files/folders, extract selected files, or save only the selected overlay.
- Enter a dotted version such as `01.001.005`; the tool checks it against the base version and game folder.
- Builds a separate delta PKG with Sony `prospero-pub-cmd.exe`; the selected base PKG is kept read-only and never overwritten.
- The final Sony SDK step can still read the full game folder and use substantial temporary space. The no-copy behavior applies to staging the selected backport files.
- Internal revision: `1.4.4-backport-editor-r29`.

See [GUIDA-BACKPORT-ITA.txt](GUIDA-BACKPORT-ITA.txt) for the short Italian steps.

## r28 — PKG Extractor to Folder

- Added a dedicated **PKG Extractor to Folder** section in the left sidebar.
- Select or drag a `.pkg`, choose a new destination folder, enter the 32-character passcode when required, and extract readable package contents without rebuilding the PKG.
- Extraction reuses the same package archive path used by PKG → PKG, restores available `sce_sys` CNT metadata, writes through a staged folder, and can verify copied files with SHA-256.
- The original PKG is never modified. Protected/retail package content still requires supported readable credentials.
- Internal revision: `1.4.4-pkg-extractor-r28`.


## r17 — auto-build direct source

- **PKG Builder:** choosing a valid prepared folder immediately starts `Building PKG`; there is no separate Build click and no full dump copy.
- **exFAT → PKG:** choosing an `.exfat` immediately starts the real build path; the old preview mount/copy pass is skipped. The image is mounted once, read-only, and LibProsperoPkg builds directly from the mounted game folder.
- If Output/Temporary are empty, Output defaults to the source parent/directory and Temporary defaults to `%TEMP%\PS5-FPKG-Builder`. User-entered paths are preserved.
- Drag-and-drop follows the same automatic behavior for prepared folders and exFAT images.
- PFS/FFPFS/FFPFSC, FFPKG/UFS2 and PKG → PKG keep their existing workflows.
- Public version remains 1.4.3; internal revision is `1.4.3-auto-build-direct-r17`.

## 1.4.3 direct-source r16

- **PKG Builder:** a prepared source folder is passed directly to LibProsperoPkg; there is no full `Copying dump...` stage.
- **exFAT → PKG:** the selected image is mounted read-only with OSFMount and the mounted game folder is passed directly to LibProsperoPkg. The dump is **not** cloned to the temporary folder.
- The exFAT mount remains attached until PKG build + verification complete, then it is released automatically.
- SDK retargeting on the read-only exFAT path is performed through LibProsperoPkg's `SdkVersionOverride`, so the mounted image is never modified.
- FFPFS / FFPFSC and FFPKG continue to use their extraction workflows because those formats are not already exposed as a normal mounted source folder.
- Public version remains 1.4.3; internal revision is `1.4.3-direct-source-r16`.

## 1.4.3 - PKG → PKG rebuild

A new **PKG → PKG** section rebuilds readable debug FPKG packages into a new PKG. The original package is never overwritten. The workflow extracts the package source to a temporary workspace, restores `sce_sys` metadata, applies the selected SDK/package declaration, builds a new package, verifies it, and then removes the temporary files.

- Dedicated `PKG → PKG` sidebar section
- Masked 32-character package passcode field
- Cover/background and metadata preview before rebuild
- New output uses the `-rebuilt.pkg` suffix
- Official/retail protected PKGs are intentionally not supported by the rebuild path


## 1.4.3 UX 2.0

- Completely redesigned interface with a custom left navigation rail instead of classic WinForms tabs.
- Nine sections are available: PKG Builder, exFAT → PKG, FFPFS / FFPFSC → PKG, FFPKG → PKG, PKG → PKG, PKG Extractor to Folder, Reader, Advanced and Credits.
- New dashboard-style build pages with source card, rich artwork preview, metadata, build settings and live activity.
- Drag-and-drop source import on build pages.
- Reader redesigned around cover/background artwork, metadata and a masked in-memory passcode.
- First-paint hard-fix retained; the initial page is attached before the window becomes visible.
- Light/Dark themes restyled with rounded cards, accent navigation and higher contrast.


Windows GUI for preparing, inspecting and building PS5 PKG/FPKG files from prepared source folders and supported image/container formats.

## 1.4.3 hard-fix

- Fixed the first-launch rendering bug where the initial build tab could open as a blank page until another tab was selected.
- `START.cmd` is now the one-click launcher. It compiles only when this release has not already been built, then opens the application automatically.
- `AVVIA.cmd` remains only as an alias to `START.cmd`.
- Reader passcodes are masked and are never written to settings or logs.
- Reader artwork support was added for embedded `icon0` and `pic0/pic1/pic2` assets, including DDS-to-PNG preview through the bundled Magick.NET runtime when available.
- Reader now shows cover, background and package metadata in the same visual preview used by the build workflow.
- The interface was redesigned with a modern card layout, larger navigation tabs, accent actions, improved dark/light palettes and clearer metadata sections.
- Dedicated workflow tabs are retained:
  1. **PKG Builder**
  2. **exFAT → PKG**
  3. **FFPFS / FFPFSC → PKG**
  4. **FFPKG → PKG**
  5. **PKG → PKG**
  6. **Reader**
  7. **Advanced**
  8. **Credits**

## Supported build inputs

- Prepared source folder containing `sce_sys/param.json`
- `.exfat`
- `.ffpkg` (UFS2)
- `.ffpfs`
- `.ffpfsc`
- `.pfs`
- readable debug `.pkg` / FPKG

## Start

1. Extract the archive completely.
2. Double-click **`START.cmd`**.
3. Accept the Windows administrator prompt.
4. The first launch compiles the small GUI integration module and opens the application automatically.
5. Future launches of the same version open directly without rebuilding.

You no longer need to run a separate setup step and then `AVVIA.cmd`.

## Build workflow

Choose the matching tab:

- **PKG Builder** — prepared source folder
- **exFAT → PKG** — `.exfat`
- **FFPFS / FFPFSC → PKG** — `.ffpfs`, `.ffpfsc`, `.pfs`
- **FFPKG → PKG** — `.ffpkg` / UFS2
- **PKG → PKG** — readable debug `.pkg` / FPKG rebuild
- **PKG Extractor to Folder** — extract readable package contents to a normal folder without rebuilding

Then import the source, review the preview, select output and temporary folders, select the firmware / SDK preset and start the build.

Changing declared SDK metadata does not itself backport executable code or guarantee compatibility with an older firmware.


## Automatic unmount / eject cleanup

- **exFAT → PKG:** the temporary OSFMount drive created by the tool is automatically detached as soon as the source copy is finished, before the PKG build starts.
- Automatic detach also runs after preview, cancellation, errors and when the application closes.
- If Windows temporarily keeps the virtual drive busy, the tool retries the detach and verifies that the drive letter has disappeared.
- If the program or PC is interrupted while a tool-owned mount is active, the next launch safely attempts to clean it up only when the recorded drive still maps to the exact same image.
- **FFPFS / FFPFSC / PFS** and **FFPKG / UFS2** are read/extracted directly in this release and do not create a Windows drive letter, so there is nothing to eject manually.

The cleanup routine never detaches unrelated drives: an OSFMount mapping must match the exact image and drive recorded by this application.

## Reader

The Reader uses a large artwork-focused layout with prominent cover and background previews while keeping metadata in a separate details panel.


Open **Reader** and choose a `.pkg` file.

The Reader attempts to display:

- cover
- background
- Content ID
- Title ID
- region
- version
- PKG type
- SDK
- minimum system version
- file size
- entry count

The 32-character package passcode field is masked by default and is used only in memory while reading metadata that requires it. It is not saved to the application's settings.

## Required payload order

Before installing the generated PKG, run the payloads in Payload Manager in this exact order:

1. `kstuff-1.12-fpkg-dr-test5.elf`
2. `shadowmountplus.elf`
3. `a53_ppr_install_fast.elf`

## Single EXE package

Run `MAKE-SINGLE-EXE.cmd` on Windows to create:

`PS5-FPKG-Builder-1.4.3.exe`

The wrapper keeps the helper runtimes intact while providing a single distributable launcher. The extracted application is cached under the user's LocalAppData folder, so repeated launches do not need to rebuild the GUI module.

## Credits

**Tool developed by Zer0Day.**

- X: https://x.com/_zer0day__
- Ko-fi: https://ko-fi.com/zzer0dayyy

Thanks to **Drakmor** for the original foundations / ShadowMountPlus.

Thanks to **svenGDK** for **LibProsperoPKG** and **UFS2Tool**.

Thanks to **EchoStretch** and **cragson** for the A53 exploit.

exFAT workflow reference: **kerrdec97 / ps5-exfat-builder**.

FFPFS / FFPFSC / PFS reader: **MkPFS / Lazy_MkPFS**.

Original third-party components remain subject to their original licenses. See `THIRD_PARTY_NOTICES.md`.

## Notes

- exFAT mounting requires administrator privileges and OSFMount.
- UFS2Tool is used for `.ffpkg` extraction/preview.
- Large images require sufficient temporary and output disk space.
- Source images are opened read-only by the conversion workflow.
- Run `VERIFY.cmd` when you want the slower self-test/verification flow; normal users only need `START.cmd`.

## Automatic updates

The application checks the official GitHub Releases page in the background after startup.

A **Check updates** button is also available in the left sidebar for a manual update check at any time. If the installed build is already current, the app confirms that you are up to date.

When a newer release is available, a popup shows the new version and release notes. `UPDATE NOW` now reads the actual ZIP assets exposed by the GitHub Release API, selects the FPKG Builder archive when present, downloads that real asset URL, closes the current app, replaces the program files and restarts `START.cmd`.

The updater no longer assumes a fixed `PS5-FPKG-Builder.zip` filename. For automatic updates, publish a GitHub **Release** (not only a tag) and attach a valid `.zip` archive; an asset name containing `PS5-FPKG-Builder` / `FPKG-Builder` is preferred, otherwise the first ZIP asset is used.

The local version is read from `VERSION.txt`.

### FFPFS / FFPFSC metadata compatibility hotfix

Some dumps serialize normally textual `param.json` fields as JSON numbers. The importer now accepts scalar string/number/bool metadata without throwing a `JsonNode.GetValue<string>()` conversion error. Packed SDK values serialized as decimal numbers are also decoded for preview.


## PKG → PKG (Auto-detect)

The PKG → PKG page accepts any `.pkg` file as input and auto-detects the package profile.
If the package content is readable/extractable with the available package credentials, the tool reconstructs the source, applies the selected SDK metadata, builds a new PKG, verifies it, and leaves the original package untouched.
Protected/encrypted package content that cannot be read is reported clearly instead of being modified.

## Trophy Unlocker — r18

The Trophy Unlocker workflow is now:

`PS5 IP -> Detect Active Game -> Trophy List loads automatically -> Unlock / Unlock All`

- `Detect Active Game` now automatically loads the Trophy2 list immediately after the active `PPSA` is found.
- `Refresh Trophy List` stays disabled until the console connection succeeds and can be used to reload trophy state manually.
- The list is read from the active game's Trophy2 catalogue over FTP and shows ID, name, grade, group and Locked/Unlocked state when `TRPTITLE.DAT` is available.
- `Unlock` is enabled only for the selected locked trophy.
- `Unlock All` first sends the complete set request, refreshes the real Trophy2 state, then retries any listed trophies that remain locked one by one, with Platinum handled last.
- No external trophy ELF is required; the supplied PS5 payload is embedded in the build.

Required console services for this page: FTP on port `2121` and a payload loader on port `9021`.

## Trophy Unlocker r19

Trophy Unlocker now includes **Disconnect**, automatic trophy-list loading after **Detect Active Game**, an explicit **TROPHY LIST / Refresh Trophy List** control, and a faster unlock path. Single trophy verification is bounded to a short fast window. Unlock All stops as soon as the complete list is reported unlocked and no longer fans out into one command per remaining trophy; one bounded all-pass retry is allowed only when the first pass still reports locked trophies. The embedded payload and static command context are cached for lower latency.


## 1.4.3 r20 - Trophy Auto List

`Detect Active Game` now performs the full connection workflow: it detects the active PPSA and immediately loads the game Trophy2 catalog/state into the grid. The manual list action is now labeled `Refresh Trophy List` and remains available after connection for explicit state refreshes. If automatic catalog loading fails, the PS5/game connection is preserved so Refresh can retry without reconnecting.

### Trophy Unlocker r21

Detect Active Game still loads the game Trophy2 catalog automatically. `Refresh Trophy List` now performs a fast state refresh when possible. The log panel is explicitly labeled `TROPHY LOG` and includes `Clear Log`. Unlock uses a standard Trophy2 payload first and automatically falls back to the bundled UDS V12 route when necessary. Unlock All processes the actual catalog state, stops immediately when complete, and targets remaining locked IDs with Platinum last.

## Trophy Unlocker r22

- `Detect Active Game` detects the PPSA and loads the Trophy2 catalog automatically.
- `Refresh Trophy List` is a dedicated large button in the ACTIONS panel.
- `Unlock` targets the selected catalog ID.
- `Unlock All` sends the exact locked catalog IDs, non-Platinum first and Platinum last.
- If UDS V12 is required and the title only commits Trophy2 on game exit, the tool asks the user to close the game and then verifies `TRPTITLE.DAT` automatically.
- The lower `TROPHY LOG` panel records route and payload diagnostics.

## r25 manual build workflow
- Import Folder/exFAT now loads the source only.
- The user can change build options before starting.
- PKG creation starts only after clicking **Build PKG**.
- Folder and mounted exFAT builds remain direct and copy-free.
