"""ppm2png.py <in.ppm> <out.png>: QEMU's screendump (binary PPM) as PNG, with the standard library only."""
import struct
import sys
import zlib

data = open(sys.argv[1], "rb").read()
fields, pos = [], 0
while len(fields) < 4:
    while data[pos : pos + 1].isspace():
        pos += 1
    if data[pos : pos + 1] == b"#":
        pos = data.index(b"\n", pos)
        continue
    end = pos
    while not data[end : end + 1].isspace():
        end += 1
    fields.append(data[pos:end])
    pos = end
assert fields[0] == b"P6" and fields[3] == b"255", "not an 8-bit binary PPM"
width, height = int(fields[1]), int(fields[2])
pixels = data[pos + 1 :]
rows = b"".join(b"\0" + pixels[y * width * 3 : (y + 1) * width * 3] for y in range(height))


def chunk(kind, body):
    return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))


with open(sys.argv[2], "wb") as f:
    f.write(b"\x89PNG\r\n\x1a\n")
    f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)))
    f.write(chunk(b"IDAT", zlib.compress(rows, 6)))
    f.write(chunk(b"IEND", b""))
