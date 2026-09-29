"""Generate the fixed embedded Apple CJK mapping asset from tracked original TXT files.

No network, hashes, code-page substitutes or runtime source parser. C1 rows labelled
undefined in the Apple sources deliberately remain raw-only, per the user's policy.
"""
from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "FontWeave/Tables/NameEncodings/Apple"
TARGET = SOURCE / "CjkMappings.bin"
NAMES = ("JAPANESE", "CHINTRAD", "CHINSIMP", "KOREAN")
ROW = re.compile(r"0x([0-9A-Fa-f]{2}|[0-9A-Fa-f]{4})\s+"
                 r"(0x[0-9A-Fa-f]{4,6}(?:\+0x[0-9A-Fa-f]{4,6})*)")


def valid_pair(name, code):
    lead, trail = code >> 8, code & 255
    if name == "JAPANESE":
        return (0x81 <= lead <= 0x9F or 0xE0 <= lead <= 0xFC) and (
            0x40 <= trail <= 0x7E or 0x80 <= trail <= 0xFC)
    if name == "CHINTRAD":
        return 0xA1 <= lead <= 0xFC and (0x40 <= trail <= 0x7E or 0xA1 <= trail <= 0xFE)
    if name == "CHINSIMP":
        return 0xA1 <= lead <= 0xFC and 0xA1 <= trail <= 0xFE
    return 0xA1 <= lead <= 0xFE and (0xA1 <= trail <= 0xFE or
        lead <= 0xAD and (0x41 <= trail <= 0x7D or 0x81 <= trail <= 0xA0))


output = bytearray(b"NMC1")
for name in NAMES:
    mapping = {}
    for line_no, line in enumerate((SOURCE / (name + ".TXT")).read_text(encoding="ascii").splitlines(), 1):
        row = line.split("#", 1)[0].strip()
        if not row:
            continue
        match = ROW.fullmatch(row)
        if match is None:
            raise ValueError(f"{name}:{line_no}: unsupported row {row!r}")
        code = int(match[1], 16)
        if code in mapping or code > 255 and not valid_pair(name, code):
            raise ValueError(f"{name}:{line_no}: duplicate or malformed encoded unit")
        points = [int(part[2:], 16) for part in match[2].split("+")]
        if any(point > 0x10FFFF or 0xD800 <= point <= 0xDFFF for point in points):
            raise ValueError(f"{name}:{line_no}: invalid Unicode scalar")
        mapping[code] = "".join(chr(point) for point in points)
    for control in (*range(0x20), 0x7F):
        if control in mapping:
            raise ValueError(f"{name}: control unexpectedly appears in table body")
        mapping[control] = chr(control)
    if name == "JAPANESE":
        # Apple JAPANESE.TXT lines243–254; 13 lead rows of188 trail positions.
        point = 0xE000
        for lead in range(0xF0, 0xFD):
            for trail in (*range(0x40, 0x7F), *range(0x80, 0xFD)):
                code = lead << 8 | trail
                if code in mapping:
                    raise ValueError("Published table overlaps Japanese algorithmic PUA")
                mapping[code] = chr(point)
                point += 1
        if point != 0xE98C:
            raise ValueError("Japanese private range is not2444 units")
    else:
        # Explicit raw-only policy: retain the original source rows but do not manufacture
        # name text from byte positions that Apple's c02 notes still call undefined.
        for code in range(0x85 if name == "KOREAN" else 0x83, 0xA0):
            if mapping.get(code) != chr(code):
                raise ValueError(f"{name}: C1 placeholder differs from published rule")
            del mapping[code]
    if len(set(mapping.values())) != len(mapping):
        raise ValueError(f"{name}: ambiguous reverse sequence requires an explicit policy")
    output.extend(struct.pack(">I", len(mapping)))
    for code, text in sorted(mapping.items()):
        utf16 = text.encode("utf-16-be")
        units = len(utf16) // 2
        if not 1 <= units <= 255:
            raise ValueError(f"{name}:{code:04X}: invalid sequence size")
        output.extend(struct.pack(">HB", code, units))
        output.extend(utf16)
    print(name, len(mapping))
TARGET.write_bytes(output)
print(TARGET)
