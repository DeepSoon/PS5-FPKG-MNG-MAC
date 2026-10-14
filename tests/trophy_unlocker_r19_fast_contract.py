from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
ui=(ROOT/'source'/'ExfatIntegration.cs').read_text(encoding='utf-8')
engine=(ROOT/'source'/'TrophyUnlockerEngine.cs').read_text(encoding='utf-8')
for token in ['trophyDisconnect','DisconnectTrophy','Disconnect','TROPHY LIST','Refresh Trophy List']:
    assert token in ui, token
assert 'trophyDisconnect.Enabled = !trophyBusy && trophyConnected' in ui
assert 'trophyDetect.Enabled = !trophyBusy && !trophyConnected' in ui
assert 'trophyIp.ReadOnly = trophyBusy || trophyConnected' in ui
assert 'trophyEngine.ResetSession();' in ui
assert 'WaitForStateFast' in engine and 'WaitForAllUnlockedFast' in engine
assert 'Unlock All complete. Stopping immediately.' in engine
assert 'BuildExactListConfigs' in engine
assert 'for (int attempt = 1; attempt <= 2; attempt++)' in engine
assert 'Connect(host, LoaderPort, 2500)' in engine
assert 'PrepareCommandFilesFast' in engine
assert 'standardPayloadCache' in engine and 'udsPayloadCache' in engine
print('PASS: fast trophy UI/engine foundations retained')
