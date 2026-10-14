from pathlib import Path

root = Path(__file__).resolve().parents[1]
ui = (root / "source" / "ExfatIntegration.cs").read_text(encoding="utf-8-sig")
job = (root / "source" / "PkgExtractionJob.cs").read_text(encoding="utf-8-sig")
rebuild = (root / "source" / "PkgRebuildJob.cs").read_text(encoding="utf-8-sig")

required_ui = [
    'CreatePagePanel("PKG Extractor to Folder")',
    'ConfigureNavButton(navExtractor, "⇩", "PKG Extractor to Folder")',
    'BuildExtractorPage();',
    'await StartExtraction()',
    'PkgExtractionJob.Run',
    'Directory.Exists(lastExtractorOutput)',
]
for text in required_ui:
    assert text in ui, text

required_job = [
    'PkgRebuildJob.ExtractSource',
    'FileWorkspace.Scan',
    'FileWorkspace.Copy',
    'Directory.Move(staged, request.Destination)',
    'The original PKG is opened read-only and is never modified.',
]
for text in required_job:
    assert text in job, text

assert 'internal static string ExtractSource' in rebuild
assert ui.count('{') == ui.count('}')
assert job.count('{') == job.count('}')
print('PASS pkg extractor r28 contract')

start = (root / "START.cmd").read_text(encoding="utf-8-sig")
build = (root / "BUILD.cmd").read_text(encoding="utf-8-sig")
assert '1.4.4-pkg-file-editor-r30' in start
assert '1.4.4-pkg-file-editor-r30' in build
marker = (root / '.build-revision').read_text(encoding='utf-8-sig').strip()
assert marker != '1.4.4-pkg-file-editor-r30', 'first launch must rebuild the integration DLL'
print('PASS r28 first-launch rebuild contract')
