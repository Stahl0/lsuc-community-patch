import struct
import sys


def rect(xmax, ymax):
    nbits = 16
    bits = format(nbits, '05b')
    for v in (0, xmax, 0, ymax):
        bits += format(v, '0%db' % nbits)
    bits += '0' * (-len(bits) % 8)
    return bytes(int(bits[i:i + 8], 2) for i in range(0, len(bits), 8))


def tag(code, body):
    if len(body) < 0x3f:
        return struct.pack('<H', (code << 6) | len(body)) + body
    return struct.pack('<HI', (code << 6) | 0x3f, len(body)) + body


body = rect(760 * 20, 420 * 20) + struct.pack('<HH', 30 << 8, 1)
body += tag(69, struct.pack('<I', 0))
body += tag(9, bytes([0, 0, 0]))
body += tag(12, b'\x00')
body += tag(1, b'')
body += tag(0, b'')
data = b'FWS' + bytes([10]) + struct.pack('<I', 8 + len(body)) + body
open(sys.argv[1], 'wb').write(data)
