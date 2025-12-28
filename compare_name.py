import fontTools.ttLib as ttLib
from fontTools.subset import Subsetter, Options
import os
import tempfile

# Use workspace temp directory
temp = r"F:\GitHub\FontFlat\temp_compare"
sample = r"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansCN-Regular.otf"

os.makedirs(temp, exist_ok=True)

# Generate fonttools output
theirs = os.path.join(temp, "fonttools_cff.otf")

font = ttLib.TTFont(sample)
options = Options()
options.desubroutinize = True
options.layout_features = ['*']
subsetter = Subsetter(options=options)
subsetter.populate(unicodes=[ord('中'), ord('文')])
subsetter.subset(font)
font.save(theirs)

print("=== pyftsubset name table ===")
font = ttLib.TTFont(theirs)
name = font['name']
print(f"Records: {len(name.names)}")
for rec in name.names:
    text = rec.toUnicode() if hasattr(rec, 'toUnicode') else str(rec.string)
    print(f"  ({rec.platformID}, {rec.platEncID}, {rec.langID}, {rec.nameID}): {text[:50]}...")
print(f"Table size: {len(font.getTableData('name'))} bytes")

# Our output
ours_path = os.path.join(tempfile.gettempdir(), "OTFontFile_ExternalTool_Tests", "ours_cff.otf")
print(f"\nLooking for: {ours_path}")
print(f"Exists: {os.path.exists(ours_path)}")

if os.path.exists(ours_path):
    our_font = ttLib.TTFont(ours_path)
    name2 = our_font['name']
    print(f"\n=== Our name table ===")
    print(f"Records: {len(name2.names)}")
    for rec in name2.names:
        text = rec.toUnicode() if hasattr(rec, 'toUnicode') else str(rec.string)
        print(f"  ({rec.platformID}, {rec.platEncID}, {rec.langID}, {rec.nameID}): {text[:50]}...")
    print(f"Table size: {len(our_font.getTableData('name'))} bytes")
