"""Compare generated catalog against a caller-provided, pinned convar dump; read-only."""
import argparse
import hashlib
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--catalog', type=Path, required=True)
parser.add_argument('--convars', type=Path, required=True)
parser.add_argument('--out', type=Path, required=True)
args = parser.parse_args()
catalog = json.loads(args.catalog.read_text(encoding='utf-8-sig'))
raw = args.convars.read_bytes()
headers = {}
for line in raw.decode('utf-8-sig').splitlines():
    match = re.fullmatch(r'([^\s]+) (.*?) \(([^)]*)\)', line)
    if match:
        headers[match[1]] = (match[2], match[3])
rows = []
for parameter in catalog['Parameters']:
    if not parameter['Editable'] or parameter['Id'].startswith('bind:') or parameter['Key'].startswith('setting.'):
        continue
    declaration = headers.get(parameter['Key'])
    flags = declaration[1] if declaration else ''
    expected_file = 'cs2_user_convars_0_slot0.vcfg' if 'per_user' in flags.split() else 'cs2_machine_convars.vcfg'
    rows.append(dict(id=parameter['Id'], present=declaration is not None,
                     declared_default=declaration[0] if declaration else None,
                     declaration_flags=flags, catalog_file=parameter['File'],
                     archive_scope_matches=declaration is not None and 'archive' in flags.split() and expected_file == parameter['File'],
                     game_status='待实机验证'))
args.out.mkdir(parents=True, exist_ok=True)
(args.out / 'convar-reference-audit.json').write_text(json.dumps(dict(
    scope='Pinned extracted declarations only; not runtime observations',
    source_revision=catalog['SourceRevision'], source_sha256=hashlib.sha256(raw).hexdigest(),
    rows=rows), ensure_ascii=False, indent=2), encoding='utf-8')
print(f'{len(rows)} editable convars; {sum(r["present"] for r in rows)} declarations present; '
      f'{sum(r["archive_scope_matches"] for r in rows)} archive scopes match. No game pass inferred.')
