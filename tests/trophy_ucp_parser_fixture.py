# Structural fixture for the UCP range layout used by the C# catalog reader.
from pathlib import Path
import json, struct

root=Path(__file__).resolve().parents[1]
cs=(root/'source'/'TrophyCatalogReader.cs').read_text(encoding='utf-8')
assert 'entry + 0x20' in cs
assert 'ReadBe64(data, metadata)' in cs
assert 'ReadBe64(data, metadata + 8)' in cs
assert 'tropmeta_' in cs and 'tropconf.json' in cs
assert 'trophyMetadata' in cs and 'groupMetadata' in cs
print('PASS: Trophy2 UCP parser contract')
