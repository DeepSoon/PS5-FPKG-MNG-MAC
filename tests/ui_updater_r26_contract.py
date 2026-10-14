from pathlib import Path

root = Path(__file__).resolve().parents[1]
ui = (root / 'source' / 'ExfatIntegration.cs').read_text(encoding='utf-8')
up = (root / 'source' / 'UpdateManager.cs').read_text(encoding='utf-8')

# Output/temp stay user-controlled: importing a source must never populate either field.
assert 'private void PrepareAutomaticBuildPaths' not in ui
assert 'outputFolder.Text =' not in ui
assert 'temporary.Text =' not in ui
assert 'Choose both Output Folder and Temporary Folder before building.' in ui

# Import only prepares source; Build PKG remains the explicit start action.
folder = ui[ui.index('private async Task ImportFolder()'):ui.index('private async Task ImportFile()')]
assert 'await StartBuild();' not in folder
imp = ui[ui.index('private async Task ImportFile()'):ui.index('private SourceInfo CreateDirectImageSource')]
assert 'await StartBuild();' not in imp
assert 'build.Click += async delegate { await StartBuild(); };' in ui

# Settings card must have enough vertical space for target SDK and the option below it.
assert 'right.RowStyles.Add(new RowStyle(SizeType.Absolute, 356));' in ui
assert 'options.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));' in ui
assert 'options.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));' in ui

# Updater must resolve the real ZIP asset from GitHub release JSON, not assume a fixed filename.
assert 'LatestZip' not in up
assert 'browser_download_url' in up
assert 'SelectZipAssetUrl(json)' in up
assert 'client.DownloadFile(release.AssetUrl, zip);' in up
assert 'does not contain a ZIP asset for automatic update' in up

print('PASS: r26 manual paths, target-SDK layout and dynamic GitHub release updater contract.')
