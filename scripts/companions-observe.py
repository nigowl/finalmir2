"""通过受保护 API 只读采样陪玩；不打开数据库、不创建角色、不改变启停或倍率。"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import time
import urllib.request

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--version', type=Path, required=True, help='完整版本目录')
parser.add_argument('--server', default='http://127.0.0.1:5080')
parser.add_argument('--seconds', type=int, default=60)
args = parser.parse_args()
if not 0 <= args.seconds <= 3600:
    parser.error('采样时长必须为 0–3600 秒')
version = args.version.resolve()
headers = {'Authorization': 'Bearer ' + (version / 'runtime/admin.key').read_text().strip(),
           'X-Mir2-Version': hashlib.sha256(str(version).encode()).hexdigest().upper()}


def get(path):
    request = urllib.request.Request(args.server.rstrip('/') + path, headers=headers)
    with urllib.request.urlopen(request, timeout=20) as response:
        return json.load(response)


def save(path, value):
    temporary = path.with_suffix('.tmp')
    with open(temporary, 'w', opener=lambda name, flags: os.open(name, flags, 0o600)) as output:
        json.dump(value, output, ensure_ascii=False, indent=2)
    temporary.replace(path)


logs = version / 'runtime/logs'
logs.mkdir(exist_ok=True)
report = get('/management/companions/world-checks')
save(logs / 'companion-world-checks.json', report)
print(json.dumps({'quests': report['questCount'], 'needsReview': sum(bool(q['warnings']) for q in report['quests'])}, ensure_ascii=False), flush=True)
deadline = time.monotonic() + args.seconds
samples = []
while True:
    status = get('/management/status')
    rows = []
    for row in get('/management/companions')['items']:
        ident = row['profile']['id']
        detail = get('/management/companions/' + ident)
        character = get('/management/admin/characters/' + str(row['characterId'])) if row['characterId'] else {}
        rows.append({'id': ident, 'name': row['profile']['name'], 'level': row['level'], 'map': row['map'],
                     'x': row['x'], 'y': row['y'], 'state': row['state'], 'reason': row['reason'], 'error': row['lastError'],
                     'runtime': detail.get('runtime'), 'events': detail.get('events', []), 'completed': character.get('completedQuests', []),
                     'inventory': character.get('inventory', []), 'spells': character.get('spells', [])})
    sample = {'utc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), 'loopMs': status['loopMilliseconds'], 'rows': rows}
    samples.append(sample)
    save(logs / 'companion-observation-latest.json', samples)
    print(json.dumps({'utc': sample['utc'], 'loopMs': sample['loopMs'], 'rows': [
        {key: row[key] for key in ['name', 'level', 'map', 'state', 'reason', 'error', 'completed']} for row in rows]}, ensure_ascii=False), flush=True)
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        break
    time.sleep(min(30, remaining))
