#!/usr/bin/env python3
"""Scan a tree for MP4 files whose video track has B-frames but no ctts box.

Such files carry decode-order timestamps only (no composition offsets), which makes
clients that trust container timestamps drop frames while the bitstream itself is fine.
"""
import json
import os
import struct
import subprocess
import sys
import time

ROOT = sys.argv[1] if len(sys.argv) > 1 else '/vol4/1000/video'
OUT = sys.argv[2] if len(sys.argv) > 2 else '/tmp/ctts_bad.txt'
EXTS = ('.mp4', '.m4v', '.mov')
CONTAINERS = {b'moov', b'trak', b'mdia', b'minf', b'stbl', b'edts', b'udta', b'meta'}


def box_children(f, start, end, depth=0):
    """Yield (type, offset, size, header_len) for boxes in [start, end)."""
    pos = start
    while pos < end:
        f.seek(pos)
        hdr = f.read(8)
        if len(hdr) < 8:
            return
        size = struct.unpack('>I', hdr[:4])[0]
        typ = hdr[4:8]
        hlen = 8
        if size == 1:
            ext = f.read(8)
            if len(ext) < 8:
                return
            size = struct.unpack('>Q', ext)[0]
            hlen = 16
        elif size == 0:
            size = end - pos
        if size < hlen:
            return
        yield typ, pos, size, hlen
        pos += size


def inspect(path):
    """Return (verdict, codec, extra) where verdict is 'ctts' / 'no-ctts' / 'skip'."""
    with open(path, 'rb') as f:
        moov = None
        moof = False
        for typ, off, size, hlen in box_children(f, 0, os.path.getsize(path)):
            if typ == b'moov':
                moov = (off + hlen, off + size)
            elif typ == b'moof':
                moof = True
        if moov is None:
            return 'skip', None, 'no-moov' if not moof else 'fragmented'
        if moof:
            return 'skip', None, 'fragmented'

        for typ, off, size, hlen in box_children(f, *moov):
            if typ != b'trak':
                continue
            body = (off + hlen, off + size)
            is_video = False
            has_ctts = False
            codec = None
            stbl = None
            for t2, o2, s2, h2 in box_children(f, *body):
                if t2 == b'mdia':
                    for t3, o3, s3, h3 in box_children(f, o2 + h2, o2 + s2):
                        if t3 == b'hdlr':
                            f.seek(o3 + h3 + 8)
                            if f.read(4) == b'vide':
                                is_video = True
                        elif t3 == b'minf':
                            for t4, o4, s4, h4 in box_children(f, o3 + h3, o3 + s3):
                                if t4 == b'stbl':
                                    stbl = (o4 + h4, o4 + s4)
            if not is_video or stbl is None:
                continue
            for t4, o4, s4, h4 in box_children(f, *stbl):
                if t4 == b'ctts':
                    has_ctts = True
                elif t4 == b'stsd':
                    f.seek(o4 + h4 + 8)
                    codec = f.read(4).decode('latin1', 'replace')
            if not has_ctts:
                return 'no-ctts', codec, ''
        return 'ctts', None, ''


def has_bframes(path):
    try:
        out = subprocess.run(
            ['ffprobe', '-v', 'error', '-select_streams', 'v:0',
             '-show_entries', 'stream=codec_name,has_b_frames,width,height,avg_frame_rate,bit_rate',
             '-of', 'json', path],
            capture_output=True, text=True, timeout=60)
        streams = json.loads(out.stdout or '{}').get('streams') or []
        if not streams:
            return None
        s = streams[0]
        return {
            'codec': s.get('codec_name'),
            'bf': int(s.get('has_b_frames') or 0),
            'w': s.get('width'),
            'h': s.get('height'),
            'fps': s.get('avg_frame_rate'),
            'br': s.get('bit_rate'),
        }
    except Exception:
        return None


def main():
    started = time.time()
    total = 0
    bad = []
    skipped = 0
    errors = 0
    with open(OUT, 'w', encoding='utf-8') as out:
        out.write('# path | codec | has_b_frames | WxH | fps | bitrate | size_bytes\n')
        for dirpath, dirnames, filenames in os.walk(ROOT):
            dirnames[:] = [d for d in dirnames if not d.startswith('@')]
            for name in filenames:
                if not name.lower().endswith(EXTS):
                    continue
                path = os.path.join(dirpath, name)
                total += 1
                try:
                    verdict, codec, extra = inspect(path)
                except Exception:
                    errors += 1
                    verdict, codec, extra = 'skip', None, 'error'
                if verdict == 'skip':
                    skipped += 1
                elif verdict == 'no-ctts':
                    info = has_bframes(path)
                    if info and info['bf'] > 0:
                        bad.append(path)
                        size = os.path.getsize(path)
                        out.write('%s | %s | %s | %sx%s | %s | %s | %d\n' % (
                            path, info['codec'], info['bf'], info['w'], info['h'],
                            info['fps'], info['br'], size))
                        out.flush()
                    else:
                        out.write('# NO-BFRAMES %s (%s)\n' % (path, codec))
                        out.flush()
                if total % 500 == 0:
                    with open(OUT + '.progress', 'w', encoding='utf-8') as p:
                        p.write('scanned %d files, bad %d, skipped %d, errors %d, %.0fs\n'
                                % (total, len(bad), skipped, errors, time.time() - started))
    with open(OUT + '.progress', 'w', encoding='utf-8') as p:
        p.write('DONE scanned %d files, bad %d, skipped %d, errors %d, %.0fs\n'
                % (total, len(bad), skipped, errors, time.time() - started))
    print('scanned %d, bad %d, skipped %d, errors %d' % (total, len(bad), skipped, errors))


if __name__ == '__main__':
    main()
