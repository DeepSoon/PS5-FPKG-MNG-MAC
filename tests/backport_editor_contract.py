from pathlib import Path

root = Path(__file__).resolve().parents[1]
ui = (root / "source" / "ExfatIntegration.cs").read_text(encoding="utf-8-sig")
job = (root / "source" / "BackportUpdate.cs").read_text(encoding="utf-8-sig")
tests = (root / "source" / "BackportUpdateTests.cs").read_text(encoding="utf-8-sig")
start = (root / "START.cmd").read_text(encoding="utf-8-sig")
build = (root / "BUILD.cmd").read_text(encoding="utf-8-sig")
revision = "1.4.4-pkg-file-editor-r30"

for text in [
    'CreatePagePanel("Backport Update")',
    'ConfigureNavButton(navBackport, "↻", "Backport Update")',
    'BuildBackportPage();',
    'backportAddFiles.Click',
    'backportReplace.Click',
    'backportExtract.Click',
    'backportSaveSelection.Click',
    'backportBuild.Click',
    'Scegli il PKG base',
    'Scegli la cartella che contiene i file backport',
]:
    assert text in ui, text

for text in [
    'Seleziona almeno un file da salvare.',
    'Directory.Move(staged, destination)',
    'ValidateGameFolderMatch(snapshot, gameParam)',
    'BackportVersion.Compare(folderVersion, snapshot.ContentVersion) != 0',
    '"--ref_pkg_path"',
    'request.BasePackage, gp5, partial',
    'FileAccess.Read, FileShare.Read',
    'Mappature GP5:',
]:
    assert text in job, text

assert job.index('if (BackportPath.SkipReason(normalized) != null) files.RemoveAt(i);') < job.index('Dictionary<string, int> byDestination')
assert 'Reject a game folder from a different base version' in tests
assert 'GP5 mappings remain correct after a reserved file is removed' in tests
assert 'Refuse to save an overlay with no selected files' in tests
assert revision in start and revision in build
assert (root / "BUILD_REVISION.txt").read_text(encoding="utf-8-sig").strip() == revision
assert (root / ".build-revision").read_text(encoding="utf-8-sig").strip() != revision
assert ui.count("{") == ui.count("}")
assert job.count("{") == job.count("}")
print("PASS backport editor UI, version checks, GP5 mapping regression guard, and first-launch rebuild contract")
