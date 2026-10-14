from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
ui=(ROOT/'source'/'ExfatIntegration.cs').read_text(encoding='utf-8')
engine=(ROOT/'source'/'TrophyUnlockerEngine.cs').read_text(encoding='utf-8')
ftp=(ROOT/'source'/'TrophyFtp.cs').read_text(encoding='utf-8')
models=(ROOT/'source'/'TrophyModels.cs').read_text(encoding='utf-8')
catalog=(ROOT/'source'/'TrophyCatalogReader.cs').read_text(encoding='utf-8')
assert 'actionGrid.Controls.Add(trophyGetList, 0, 1)' in ui, 'Refresh must be a visible large ACTIONS button'
assert 'CompleteTrophyCommitIfNeeded' in ui and 'Trophy2 commit required' in ui
assert 'Close the game on the PS5' in ui
assert 'mode=list\\nids=' in engine, 'Unlock All must target real catalog IDs'
assert 'GetLockedNonPlatinum' in engine and 'GetLockedPlatinum' in engine
assert 'Handling Platinum last' in engine
assert 'CompletePendingCommit' in engine and 'IsTitleMounted' in engine
assert 'LogPayloadDiagnostic' in engine and 'trophy_unlocker_result.txt' in engine
assert 'TryDelete' in ftp and 'TryDownloadText' in ftp
assert 'UsedUdsRoute' in models and 'CommitPending' in models
assert 'result.UsedUdsRoute = catalog.UsedUdsRoute' in catalog
assert 'result.CommitPending = catalog.CommitPending' in catalog
print('PASS: r22 visible Refresh, exact-ID Unlock All, Platinum-last and UDS commit verification contract')
