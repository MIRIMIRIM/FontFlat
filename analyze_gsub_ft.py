import fontTools.ttLib as ttLib
from fontTools.subset import Subsetter, Options
import os

temp = r"F:\GitHub\FontFlat\temp_compare"
sample = r"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansCN-Regular.otf"
os.makedirs(temp, exist_ok=True)

font = ttLib.TTFont(sample)
options = Options()
options.desubroutinize = True
options.layout_features = ['*']
subsetter = Subsetter(options=options)
subsetter.populate(unicodes=[ord('中'), ord('文')])
subsetter.subset(font)

gsub_data = font.getTableData('GSUB')
print(f"fonttools GSUB: {len(gsub_data)} bytes")
print("\nHex dump:")
for i, b in enumerate(gsub_data):
    print(f"{b:02X}", end=" ")
    if (i + 1) % 16 == 0:
        print()
print()

# Parse header
majorVer = (gsub_data[0] << 8) | gsub_data[1]
minorVer = (gsub_data[2] << 8) | gsub_data[3]
scriptListOffset = (gsub_data[4] << 8) | gsub_data[5]
featureListOffset = (gsub_data[6] << 8) | gsub_data[7]
lookupListOffset = (gsub_data[8] << 8) | gsub_data[9]

print(f"\nHeader: v{majorVer}.{minorVer}")
print(f"  ScriptListOffset: {scriptListOffset}")
print(f"  FeatureListOffset: {featureListOffset}")
print(f"  LookupListOffset: {lookupListOffset}")

# Parse ScriptList
if scriptListOffset > 0:
    pos = scriptListOffset
    scriptCount = (gsub_data[pos] << 8) | gsub_data[pos + 1]
    print(f"\nScriptList at {pos}: {scriptCount} scripts")
    
    pos += 2
    for i in range(scriptCount):
        tag = gsub_data[pos:pos+4].decode('ascii')
        offset = (gsub_data[pos + 4] << 8) | gsub_data[pos + 5]
        print(f"  Script '{tag}' at offset {offset}")
        pos += 6
