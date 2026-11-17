import hashlib
import struct
import sys

block = 64


def build_index(src):
    index = {}
    for off in range(0, len(src) - block + 1, block):
        key = src[off:off + block]
        if key not in index:
            index[key] = off
    return index


def extend_forward(src, dst, s, d):
    n = 0
    step = 4096
    while s + n < len(src) and d + n < len(dst):
        m = min(step, len(src) - s - n, len(dst) - d - n)
        if src[s + n:s + n + m] == dst[d + n:d + n + m]:
            n += m
            continue
        while m > 0 and src[s + n] == dst[d + n]:
            n += 1
            m -= 1
        break
    return n


def make(src, dst):
    index = build_index(src)
    ops = []
    pending = bytearray()
    d = 0
    while d < len(dst):
        s = index.get(dst[d:d + block]) if d + block <= len(dst) else None
        if s is None:
            pending.append(dst[d])
            d += 1
            continue
        back = 0
        while back < len(pending) and s - back - 1 >= 0 and src[s - back - 1] == pending[len(pending) - back - 1]:
            back += 1
        if back:
            del pending[len(pending) - back:]
        if pending:
            ops.append(('I', bytes(pending)))
            pending = bytearray()
        n = extend_forward(src, dst, s, d)
        ops.append(('C', s - back, n + back))
        d += n
    if pending:
        ops.append(('I', bytes(pending)))
    return ops


def write(path, src, dst, ops):
    with open(path, 'wb') as f:
        f.write(b'LSCPD1\0\0')
        f.write(hashlib.sha256(src).digest())
        f.write(hashlib.sha256(dst).digest())
        f.write(struct.pack('<I', len(dst)))
        for op in ops:
            if op[0] == 'C':
                f.write(b'C' + struct.pack('<II', op[1], op[2]))
            else:
                f.write(b'I' + struct.pack('<I', len(op[1])) + op[1])
        f.write(b'E')


def apply(src, delta):
    assert delta[:8] == b'LSCPD1\0\0'
    p = 8 + 32 + 32 + 4
    out = bytearray()
    while True:
        t = delta[p:p + 1]
        p += 1
        if t == b'C':
            off, n = struct.unpack_from('<II', delta, p)
            p += 8
            out += src[off:off + n]
        elif t == b'I':
            n = struct.unpack_from('<I', delta, p)[0]
            p += 4
            out += delta[p:p + n]
            p += n
        else:
            break
    return bytes(out)


def main():
    src = open(sys.argv[1], 'rb').read()
    dst = open(sys.argv[2], 'rb').read()
    ops = make(src, dst)
    write(sys.argv[3], src, dst, ops)
    delta = open(sys.argv[3], 'rb').read()
    assert apply(src, delta) == dst, 'round trip failed'
    inserts = [op[1] for op in ops if op[0] == 'I']
    print('delta %d bytes, %d copies, %d inserts, %d new bytes' % (
        len(delta), sum(1 for op in ops if op[0] == 'C'), len(inserts), sum(len(x) for x in inserts)))
    for x in sorted(inserts, key=len, reverse=True)[:5]:
        print('  largest insert %d bytes' % len(x))
    if len(sys.argv) > 4:
        with open(sys.argv[4], 'wb') as f:
            for x in inserts:
                f.write(x + b'\n----\n')


main()
