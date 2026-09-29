"""Generate the finite Unicode 1.1 / ISO 10646:1993 name identity resource.

Input is the tracked Unicode DerivedAge 18.0.0 data and obsolete Hangul
conversion table. This emits only a versioned assignment bitmap and the
published old-Hangul-to-modern mapping. It does not use UnicodeData 1.1's
incomplete composite-name list or infer Unicode 1.0/Tibetan migrations.
"""

from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "FontWeave/Tables/NameEncodings/Unicode11"
TARGET = SOURCE / "Unicode11Mappings.bin"
AGE = SOURCE / "DerivedAge-18.0.0.txt"
HANGUL = SOURCE / "HANGUL.TXT"

age_lines = AGE.read_text(encoding="utf-8").splitlines()
if not age_lines or age_lines[0] != "# DerivedAge-18.0.0.txt":
    raise ValueError("Unexpected DerivedAge revision")
age_re = re.compile(r"^([0-9A-F]{4,6})(?:\.\.([0-9A-F]{4,6}))?\s*;\s*1\.1\b")
assigned = set()
for line in age_lines:
    match = age_re.match(line)
    if not match:
        continue
    start = int(match[1], 16)
    end = int(match[2], 16) if match[2] else start
    if start > end or end > 0xFFFF:
        raise ValueError("Unexpected non-BMP Age 1.1 range")
    if any(code in assigned for code in range(start, end + 1)):
        raise ValueError("Overlapping Age 1.1 ranges")
    assigned.update(range(start, end + 1))
if len(assigned) != 33979:
    raise ValueError(f"Unexpected Age 1.1 repertoire: {len(assigned)}")

# This historical semantic profile leaves private-use and noncharacter positions raw;
# it does not impose a general prohibition on such UTF-16 name payloads.
identity = assigned.difference(range(0xE000, 0xF900)).difference((0xFFFE, 0xFFFF))
if len(identity) != 27577 or any(0x3400 <= code <= 0x4DFF for code in identity) or any(
    0xAC00 <= code <= 0xD7A3 for code in identity):
    raise ValueError("Unexpected historical identity domain")
bitmap = bytearray(8192)
for code in identity:
    bitmap[code >> 3] |= 1 << (code & 7)

mapping = {}
total_rows = 0
for number, line in enumerate(HANGUL.read_text(encoding="ascii").splitlines(), 1):
    parts = line.split("\t")
    if len(parts) < 7 or not parts[0].isdigit():
        continue
    total_rows += 1
    old, modern = parts[5], parts[6]
    if old == "-":
        continue
    try:
        source = int(old, 16)
        target = int(modern, 16)
    except ValueError as error:
        raise ValueError(f"HANGUL row {number}: bad code") from error
    if source in mapping:
        raise ValueError(f"HANGUL row {number}: duplicate old code")
    mapping[source] = target
if total_rows != 11172 or len(mapping) != 6656 or set(mapping) != set(range(0x3400, 0x4E00)):
    raise ValueError("Unexpected Unicode 1.1 Hangul rows")
if len(set(mapping.values())) != 6656 or not all(0xAC00 <= value <= 0xD7A3
                                                 for value in mapping.values()):
    raise ValueError("Ambiguous or out-of-range modern Hangul mapping")
if mapping[0x3400] != 0xAC00 or mapping[0x3D2E] != 0xAC02:
    raise ValueError("Unexpected old Hangul witnesses")

resource = bytearray(struct.pack(">II", 0x4E553131, 1))
resource.extend(bitmap)
for old in range(0x3400, 0x4E00):
    resource.extend(struct.pack(">H", mapping[old]))
if len(resource) != 21512:
    raise ValueError(f"Unexpected resource length: {len(resource)}")
TARGET.write_bytes(resource)
print(f"{TARGET}: Age1.1={len(assigned)}, excludedPrivateNoncharacter={len(assigned)-len(identity)}, "
      f"identity={len(identity)}, oldHangul={len(mapping)}, semantic={len(identity)+len(mapping)}, "
      f"bytes={len(resource)}")
