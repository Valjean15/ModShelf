"""Writes a RAR 5 archive of stored (uncompressed) entries, as RARLAB's technote describes the format."""
import sys, struct, zlib

def vint(n):
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        if n:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)

def header(body):
    # body: from Header type on; size counts it, CRC covers the size and the body.
    data = vint(len(body)) + body
    return struct.pack('<I', zlib.crc32(data) & 0xFFFFFFFF) + data

def main(path, entries):
    out = bytearray(b'Rar!\x1a\x07\x01\x00')
    out += header(vint(1) + vint(0) + vint(0))  # main header: type 1, flags 0, archive flags 0
    mtime = 1767225600  # 2026-01-01
    for name, content in entries:
        raw = name.encode('utf-8')
        if content is None:
            body = vint(2) + vint(0) + vint(0x0001 | 0x0002) + vint(0) + vint(0x10) + struct.pack('<I', mtime) + vint(0) + vint(0) + vint(len(raw)) + raw
            out += header(body)
        else:
            body = (vint(2) + vint(0x0002) + vint(len(content)) + vint(0x0002 | 0x0004) + vint(len(content)) + vint(0x20)
                    + struct.pack('<I', mtime) + struct.pack('<I', zlib.crc32(content) & 0xFFFFFFFF) + vint(0) + vint(0) + vint(len(raw)) + raw)
            out += header(body) + content
    out += header(vint(5) + vint(0) + vint(0))
    open(path, 'wb').write(out)

readme = "Better Lanterns 1.2\r\n\r\nPut the .esp in your Data folder.\r\nÜbersetzung: Danke!\r\n".encode('utf-8')
main(sys.argv[1], [
    ('Better Lanterns', None),
    ('Better Lanterns/Readme.txt', readme),
    ('Better Lanterns/Data', None),
    ('Better Lanterns/Data/BetterLanterns.esp', bytes(range(256)) * 4),
    ('Better Lanterns/Data/Textures/lantern_d.dds', b'DDS ' + b'\0' * 2044),
    ('Better Lanterns/Notes/примечания.md', '# Заметки\n\nФонари ярче.\n'.encode('utf-8')),
])
