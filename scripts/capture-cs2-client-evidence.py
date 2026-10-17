"""Read-only evidence extraction from an installed CS2 client's VPK index.

Uses no Steam credentials, game injection or third-party packages. Extracts only
fixed game-owned configuration/menu resources, checks VPK entry CRC, and keeps
raw resources local under artifacts. It does not establish in-game effectiveness.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import struct
import zlib


class Vpk:
    def __init__(self, directory):
        self.path = Path(directory)
        with self.path.open('rb') as stream:
            signature, version, size = struct.unpack('<III', stream.read(12))
            if signature != 0x55AA1234 or version not in (1, 2) or size > 64 * 1024 * 1024:
                raise ValueError('Unsupported VPK header')
            self.header = 28 if version == 2 else 12
            stream.seek(self.header)
            self.tree = stream.read(size)
        self.data_base = self.header + size
        self.position = 0
        self.entries = {}
        while extension := self.string():
            while directory := self.string():
                while name := self.string():
                    crc, preload_length, archive, offset, length, end = struct.unpack_from('<IHHIIH', self.tree, self.position)
                    self.position += 18
                    if end != 0xFFFF:
                        raise ValueError('Invalid VPK entry terminator')
                    preload = self.tree[self.position:self.position + preload_length]
                    self.position += preload_length
                    key = (('' if directory == ' ' else directory + '/') + name + ('' if extension == ' ' else '.' + extension))
                    self.entries[key] = crc, archive, offset, length, preload

    def string(self):
        end = self.tree.index(b'\0', self.position)
        value = self.tree[self.position:end].decode('utf-8')
        self.position = end + 1
        return value

    def read(self, name):
        crc, archive, offset, length, preload = self.entries[name]
        if length + len(preload) > 16 * 1024 * 1024:
            raise ValueError('Resource exceeds evidence size limit')
        archive_path = self.path if archive == 0x7FFF else self.path.with_name(self.path.name.replace('_dir.vpk', f'_{archive:03d}.vpk'))
        with archive_path.open('rb') as stream:
            stream.seek((self.data_base if archive == 0x7FFF else 0) + offset)
            data = preload + stream.read(length)
        if len(data) != length + len(preload) or zlib.crc32(data) & 0xFFFFFFFF != crc:
            raise ValueError('VPK resource CRC mismatch')
        return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-root', required=True, help='CS2 installation directory containing game/csgo')
    parser.add_argument('--out', default='artifacts/cs2-feasibility/client')
    args = parser.parse_args()
    root = Path(args.game_root)
    output = Path(args.out)
    output.mkdir(parents=True, exist_ok=True)
    pack = Vpk(root / 'game/csgo/pak01_dir.vpk')
    names = [f'cfg/video_defaults_{n}.txt' for n in range(4)] + ['cfg/video_version.txt', 'panorama/layout/settings/settings_video.xml', 'panorama/layout/settings/settings_video.vxml_c']
    evidence = []
    for name in names:
        if name not in pack.entries:
            evidence.append({'resource': name, 'present': False})
            continue
        data = pack.read(name)
        (output / Path(name).name).write_bytes(data)
        item = {'resource': name, 'present': True, 'sha256': hashlib.sha256(data).hexdigest(), 'size': len(data), 'scope': 'installed-client data, not game execution'}
        if name.endswith('.txt'):
            text = data.decode('utf-8-sig')
            item['pairs'] = dict(re.findall(r'"([^"\r\n]+)"\s*"([^"\r\n]*)"', text))
        evidence.append(item)
    steam_inf = root / 'game/csgo/steam.inf'
    version = dict(line.split('=', 1) for line in steam_inf.read_text(encoding='utf-8-sig').splitlines() if '=' in line)
    result = {'client': version, 'vpk_index_sha256': hashlib.sha256(pack.path.read_bytes()).hexdigest(), 'resources': evidence, 'extractorLaunchesGame': False}
    (output / 'client-evidence.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'client': version.get('ClientVersion'), 'patch': version.get('PatchVersion'), 'resources_found': sum(r['present'] for r in evidence), 'resources_expected': len(names), 'game_effectiveness': 'not tested by this extractor'}, ensure_ascii=False))


if __name__ == '__main__':
    main()
