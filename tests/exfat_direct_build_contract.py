from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
job = (root / 'source' / 'ConversionJob.cs').read_text(encoding='utf-8')
ui = (root / 'source' / 'ExfatIntegration.cs').read_text(encoding='utf-8')
xml = (root / 'LibProsperoPkg.xml').read_text(encoding='utf-8', errors='replace')

# The exFAT branch must build from the OSFMount path instead of copying the dump.
branch_start = job.index('report(new ConversionProgress("Mounting exFAT"')
branch_end = job.index('// Extracted PFS/UFS2 sources are writable', branch_start)
branch = job[branch_start:branch_end]
assert 'dump = source;' in branch, 'exFAT source is not passed directly to the builder'
assert 'FileWorkspace.Scan(source' not in branch, 'exFAT branch still scans the full dump for copying'
assert 'FileWorkspace.Copy(' not in branch, 'exFAT branch still copies the dump'
assert 'mount.Dispose()' not in branch, 'exFAT mount is detached before the builder can read it'
assert 'Dump copy skipped' in branch, 'direct-build log marker missing'

# Read-only exFAT must not receive the param.json rewrite. The build override is documented
# by the bundled library as updating generated param.json and executable SDK metadata.
assert 'if (request.DetectedFormat != ImageFormat.Exfat)' in job
assert 'BuilderBridge.Set(options, "SdkVersionOverride", request.TargetSdk);' in job
assert 'ProsperoBuildOptions.SdkVersionOverride' in xml
assert 'sce_sys/param.json' in xml

# Prepared folders already use the source directly and must remain copy-free.
folder_start = ui.index('private string BuildFolder(')
folder_end = ui.index('private async Task ImportPkgReader()', folder_start)
folder = ui[folder_start:folder_end]
assert 'BuilderBridge.PrepareOptions(form,source,' in folder
assert 'FileWorkspace.Copy(' not in folder
assert 'Copying dump' not in folder

print('PASS: prepared folders and mounted exFAT both go directly to PKG building without a full dump copy.')
