from pathlib import Path

root = Path(__file__).resolve().parents[1]
ui = (root / 'source' / 'ExfatIntegration.cs').read_text(encoding='utf-8')
job = (root / 'source' / 'ConversionJob.cs').read_text(encoding='utf-8')

assert 'build.Click += async delegate { await StartBuild(); };' in ui
assert 'importFolder.Click += async delegate { await ImportFolder(); };' in ui

folder = ui[ui.index('private async Task ImportFolder()'):ui.index('private async Task ImportFile()')]
assert 'PrepareAutomaticBuildPaths' not in folder, 'folder import must not auto-fill output/temp paths'
assert 'await StartBuild();' not in folder, 'folder import must wait for an explicit Build PKG click'
assert 'click Build PKG' in folder

imp = ui[ui.index('private async Task ImportFile()'):ui.index('private SourceInfo CreateDirectImageSource')]
assert 'if (sourceMode == SourceMode.Exfat)' in imp
assert 'LoadSource(CreateDirectImageSource(dialog.FileName, "exFAT"));' in imp
exfat_branch = imp[imp.index('if (sourceMode == SourceMode.Exfat)'):imp.index('SourceInfo info = await Task.Run', imp.index('if (sourceMode == SourceMode.Exfat)'))]
assert 'await StartBuild();' not in exfat_branch, 'exFAT import must wait for an explicit Build PKG click'
assert 'PreviewImage(' not in exfat_branch, 'exFAT must not do a preview mount/copy before the build'
assert 'click Build PKG' in exfat_branch
assert 'PrepareAutomaticBuildPaths' not in exfat_branch, 'exFAT import must not auto-fill output/temp paths'

# Drag-and-drop follows the same manual-build rule.
drop = ui[ui.index('private async Task BuildDragDrop'):ui.index('private void AttachWorkspace')]
assert 'await StartBuild();' not in drop, 'drag/drop source loading must not auto-start a build'

# Direct build invariants remain intact after Build PKG is clicked.
folder_build = ui[ui.index('private string BuildFolder('):ui.index('private async Task ImportPkgReader()')]
assert 'FileWorkspace.Copy(' not in folder_build
assert 'Copying dump' not in folder_build
branch_start = job.index('report(new ConversionProgress("Mounting exFAT"')
branch_end = job.index('// Extracted PFS/UFS2 sources are writable', branch_start)
exfat_job = job[branch_start:branch_end]
assert 'dump = source;' in exfat_job
assert 'FileWorkspace.Copy(' not in exfat_job
assert 'Copying dump' not in exfat_job

print('PASS: Folder/exFAT import only loads the source; Build PKG explicitly starts the existing direct copy-free build.')
