"""Read-only supplemental audit. Reports locations/rules, never matched secret values.

Use together with Gitleaks on all Git refs. Does not prove absence of secrets.
Only public source candidates and every reachable Git blob are inspected here;
ignored build artifacts are audited separately with Gitleaks.
"""
import argparse
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RULES = {
    'private-key': re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----'),
    'jwt-literal': re.compile(rb'eyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}'),
    'steam-token-literal': re.compile(rb'(?i)(?:refresh_token|access_token|eya_token|shared_secret|identity_secret)\s*[=:]\s*[\"\x27]([A-Za-z0-9_+/.=-]{25,})'),
    'personal-windows-path': re.compile(rb'(?i)[a-z]:[\\/]+Users[\\/]+[^\\/\s\"<>]+'),
}
DATA_NAMES = re.compile(r'(?i)(^|/)(manager\.json|history\.json|loginusers\.vdf|ssfn\d+|.*\.(?:db|sqlite3?|pfx|p12|key|pem)|\.env(?:\..*)?)$')


def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args])


def inspect(path, data, origin):
    findings = []
    if DATA_NAMES.search(path):
        findings.append({'rule': 'user-data-or-key-file', 'path': path, 'origin': origin})
    # Binary blobs are inventoried; textual literal rules do not inspect binary noise.
    if b'\0' in data[:8192]:
        return findings, False
    for rule, pattern in RULES.items():
        for match in pattern.finditer(data):
            findings.append({'rule': rule, 'path': path, 'origin': origin,
                             'line': data.count(b'\n', 0, match.start()) + 1})
    return findings, True


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--report', required=True)
    args = parser.parse_args()
    findings = []
    paths = git('-c', 'core.quotepath=false', 'ls-files', '-z', '--cached', '--others', '--exclude-standard').decode('utf-8').split('\0')
    source_count = text_count = 0
    for path in sorted(set(filter(None, paths))):
        file = ROOT / path
        if file.is_file():
            found, text = inspect(path, file.read_bytes(), 'worktree')
            findings.extend(found)
            source_count += 1
            text_count += int(text)
    # cat-file --batch covers all refs, including historical removed files.
    objects = git('rev-list', '--objects', '--all').decode('utf-8').splitlines()
    batch = subprocess.Popen(['git', '-C', str(ROOT), 'cat-file', '--batch'], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    blob_count = history_text = 0
    try:
        for item in objects:
            oid, _, path = item.partition(' ')
            batch.stdin.write((oid + '\n').encode('ascii'))
            batch.stdin.flush()
            head = batch.stdout.readline().decode('ascii').strip().split()
            if len(head) != 3:
                raise RuntimeError('Unable to inspect Git object')
            size = int(head[2])
            data = batch.stdout.read(size)
            if len(data) != size or batch.stdout.read(1) != b'\n':
                raise RuntimeError('Truncated Git object')
            if head[1] == 'blob':
                blob_count += 1
                found, text = inspect(path, data, oid)
                findings.extend(found)
                history_text += int(text)
    finally:
        batch.stdin.close()
        batch.wait()
    result = {'commitsAllRefs': int(git('rev-list', '--all', '--count')), 'worktreeFiles': source_count,
              'worktreeTextFiles': text_count, 'historyUniqueBlobs': blob_count,
              'historyTextBlobs': history_text, 'findings': findings,
              'scope': 'Public source candidates and all reachable Git objects; use Gitleaks separately.'}
    report = Path(args.report)
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k != 'findings'}, ensure_ascii=False))
    print('Findings by rule:', {rule: sum(x['rule'] == rule for x in findings) for rule in sorted(set(x['rule'] for x in findings))})
    return 1 if any(x['rule'] != 'personal-windows-path' for x in findings) else 0


if __name__ == '__main__':
    raise SystemExit(main())
