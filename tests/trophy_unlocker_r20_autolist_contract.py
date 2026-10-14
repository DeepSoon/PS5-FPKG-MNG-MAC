from pathlib import Path
import re
ROOT=Path(__file__).resolve().parents[1]
ui=(ROOT/'source'/'ExfatIntegration.cs').read_text(encoding='utf-8')

m=re.search(r'private async Task DetectTrophyGame\(\)(.*?)\n\s*private void DisconnectTrophy\(', ui, re.S)
assert m, 'DetectTrophyGame method not found'
body=m.group(1)
assert 'trophyEngine.DetectActiveGame' in body
assert 'trophyEngine.GetTrophyList' in body, 'Detect must automatically load trophy list'
assert 'ApplyTrophyList' in body, 'Detected trophy list must be applied to UI'
assert 'Trophy list loaded automatically' in body
assert 'click Get Trophy List' not in body
assert 'Text = "Refresh Trophy List"' in ui, 'manual list button should clearly be Refresh after auto-load'
assert 'trophyGetList.Enabled = !trophyBusy && trophyConnected' in ui
print('PASS: r20 Detect Active Game automatically loads trophy list and manual action is Refresh')
