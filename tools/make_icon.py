"""app.ico 생성 (외부 라이브러리 없이). 4x 슈퍼샘플링으로 가장자리를 부드럽게."""
import struct, zlib, sys

ACCENT = (79, 140, 255)
BODY = (250, 250, 252)
DOT = (60, 64, 80)

def inside_round(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r

def color_at(u, v):
    """u, v in [0,1). Returns RGBA."""
    # 고리 두 개
    for rx in (0.32, 0.68):
        if inside_round(u, v, rx - 0.035, 0.06, rx + 0.035, 0.24, 0.035):
            return DOT + (255,)
    if not inside_round(u, v, 0.08, 0.13, 0.92, 0.92, 0.12):
        return (0, 0, 0, 0)
    if v < 0.34:
        return ACCENT + (255,)
    # 날짜 점 3x3 (하나는 강조)
    for i in range(3):
        for j in range(3):
            cx, cy = 0.29 + i * 0.21, 0.47 + j * 0.15
            if (u - cx) ** 2 + (v - cy) ** 2 <= 0.045 ** 2:
                return (ACCENT if (i, j) == (2, 1) else DOT) + (255,)
    return BODY + (255,)

def render(size, ss=4):
    rows = []
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            acc = [0, 0, 0, 0]
            for sy in range(ss):
                for sx in range(ss):
                    r, g, b, a = color_at((x + (sx + 0.5) / ss) / size, (y + (sy + 0.5) / ss) / size)
                    acc[0] += r * a; acc[1] += g * a; acc[2] += b * a; acc[3] += a
            a = acc[3]
            if a:
                row += bytes([acc[0] // a, acc[1] // a, acc[2] // a, a // (ss * ss)])
            else:
                row += b"\0\0\0\0"
        rows.append(bytes(row))
    raw = b"".join(rows)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))

sizes = [256, 64, 48, 32, 24, 16]
pngs = [render(s) for s in sizes]
out = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, p in zip(sizes, pngs):
    out += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(p), offset)
    offset += len(p)
out += b"".join(pngs)
open(sys.argv[1] if len(sys.argv) > 1 else "app.ico", "wb").write(out)
