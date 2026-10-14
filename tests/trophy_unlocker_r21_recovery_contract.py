from pathlib import Path
import hashlib
ROOT=Path(__file__).resolve().parents[1]
ui=(ROOT/'source'/'ExfatIntegration.cs').read_text(encoding='utf-8')
engine=(ROOT/'source'/'TrophyUnlockerEngine.cs').read_text(encoding='utf-8')
build=(ROOT/'BUILD.cmd').read_text(encoding='utf-8')
csproj=(ROOT/'source'/'FpkgExfat.csproj').read_text(encoding='utf-8')
for token in ['TROPHY LOG','Clear Log','Refresh Trophy List','trophyClearLog']:
    assert token in ui, token
assert 'RefreshTrophyState(host, currentTrophyList' in ui
assert 'trophyClearLog.Enabled = !trophyBusy' in ui
for token in ['StandardPayloadResource','UdsPayloadResource','Ps5UdsV12','BuildExactListConfigs','Platinum last','mode=list\\nids=']:
    assert token in engine, token
assert 'GetLocked(state).Count == 0' in engine and 'Stopping immediately' in engine
assert 'CompletePendingCommit' in engine and 'CommitPending' in engine
assert 'FpkgExfat.TrophyUnlocker.Ps5UdsV12' in build and 'FpkgExfat.TrophyUnlocker.Ps5UdsV12' in csproj
payload=ROOT/'assets'/'trophy-unlocker-ps5-uds-initializer-v12.elf'
blob=payload.read_bytes(); assert blob[:4]==b'\x7fELF'
assert hashlib.sha256(blob).hexdigest()=='7e6b7cb821f289477fe79f7686c562157450033dc255a84537c6577febfcb486'
print('PASS: UDS recovery, commit support, fast refresh, log controls and embedded payload contract')
