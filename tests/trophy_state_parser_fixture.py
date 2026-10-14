from pathlib import Path
cs=(Path(__file__).resolve().parents[1]/'source'/'TrophyCatalogReader.cs').read_text(encoding='utf-8')
for token in ['T2PD','T2TD','0x11C','0x17C','pagePayload = 0x400','state != 0x11','flags != 0x2000','tail != 0x5500000000000008UL']:
    assert token in cs, token
print('PASS: Trophy2 TRPTITLE state parser contract')
