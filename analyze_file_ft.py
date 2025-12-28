import fontTools.ttLib as ttLib
from fontTools.subset import Subsetter, Options
import struct

sample = r"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansCN-Regular.otf"

font = ttLib.TTFont(sample)
options = Options()
options.desubroutinize = True
options.layout_features = ['*']
subsetter = Subsetter(options=options)
subsetter.populate(unicodes=[ord('中'), ord('文')])
subsetter.subset(font)

# Save
import tempfile
import os
path = os.path.join(tempfile.gettempdir(), "ft_cff.otf")
font.save(path)

# Analyze
data = open(path, 'rb').read()
print(f"fonttools file size: {len(data)} bytes")

# Header
sfnt = struct.unpack('>I', data[0:4])[0]
numTables = struct.unpack('>H', data[4:6])[0]
searchRange = struct.unpack('>H', data[6:8])[0]
entrySelector = struct.unpack('>H', data[8:10])[0]
rangeShift = struct.unpack('>H', data[10:12])[0]

print(f"\n=== File Header ===")
print(f"sfntVersion: 0x{sfnt:08X}")
print(f"numTables: {numTables}")
print(f"searchRange: {searchRange}")
print(f"entrySelector: {entrySelector}")
print(f"rangeShift: {rangeShift}")

# Table directory
print(f"\n=== Table Directory ===")
totalTableSize = 0
for i in range(numTables):
    offset = 12 + i * 16
    tag = data[offset:offset+4].decode('ascii')
    checksum = struct.unpack('>I', data[offset+4:offset+8])[0]
    tblOffset = struct.unpack('>I', data[offset+8:offset+12])[0]
    length = struct.unpack('>I', data[offset+12:offset+16])[0]
    print(f"  {tag}: offset={tblOffset}, length={length}")
    totalTableSize += length

print(f"\nHeader + Directory: 12 + {numTables}*16 = {12 + numTables * 16} bytes")
print(f"Total table data: {totalTableSize} bytes")
print(f"File size: {len(data)} bytes")
print(f"Padding/overhead: {len(data) - (12 + numTables * 16) - totalTableSize} bytes")
