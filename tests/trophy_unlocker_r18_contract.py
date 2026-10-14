from pathlib import Path
import re, hashlib
ROOT=Path(__file__).resolve().parents[1]
ui=(ROOT/'source'/'ExfatIntegration.cs').read_text(encoding='utf-8')
engine=(ROOT/'source'/'TrophyUnlockerEngine.cs').read_text(encoding='utf-8')
catalog=(ROOT/'source'/'TrophyCatalogReader.cs').read_text(encoding='utf-8')
build=(ROOT/'BUILD.cmd').read_text(encoding='utf-8')
csproj=(ROOT/'source'/'FpkgExfat.csproj').read_text(encoding='utf-8')
payload=ROOT/'assets'/'trophy-unlocker-ps5-r10-stable.elf'
for token in ['Trophy Unlocker','PS5 IP Address','Detect Active Game','Disconnect','Refresh Trophy List','TROPHY LIST','Unlock All','trophyUnlock','trophyUnlockAll','trophyGrid']:
    assert token in ui, token
assert 'trophyGetList.Enabled = !trophyBusy && trophyConnected' in ui
assert 'selected != null && !selected.Unlocked' in ui
nav=re.search(r'navigationButtons\s*=\s*new\s+Button\[\]\s*\{([^}]+)\}',ui,re.S)
pages=re.search(r'Control\[\]\s+pages\s*=\s*new\s+Control\[\]\s*\{([^}]+)\}',ui,re.S)
assert nav and pages
nav_items=[x.strip() for x in nav.group(1).split(',') if x.strip()]
page_items=[x.strip() for x in pages.group(1).split(',') if x.strip()]
assert len(nav_items)==len(page_items)
assert nav_items.index('navTrophy')==page_items.index('trophyPage')
for token in ['DetectActiveGame','GetTrophyList','UnlockSingle','UnlockAll','mode=id\\nid=','mode=list\\nids=','9021']:
    assert token in engine, token
assert 'BuildExactListConfigs' in engine
assert 'attempt <= 2' in engine and 'Connect(host, LoaderPort, 2500)' in engine
assert 'ResetSession' in engine and 'standardPayloadCache' in engine and 'udsPayloadCache' in engine
for token in ['TROPHY.UCP','trophy00.ucp','TRPTITLE.DAT','tropconf.json','tropmeta_','0x11','0x2000','0x5500000000000008UL']:
    assert token in catalog, token
assert 'FpkgExfat.TrophyUnlocker.Ps5R10' in build and 'FpkgExfat.TrophyUnlocker.Ps5R10' in csproj
blob=payload.read_bytes(); assert blob[:4]==b'\x7fELF'
assert hashlib.sha256(blob).hexdigest()=='f03a01ad203afcbb3e69e46ef32e4523562425dcf06633619e2273ba677a0206'
print('PASS: Trophy Unlocker core UI, exact-ID engine, embedded payload and navigation contract')
