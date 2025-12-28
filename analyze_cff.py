import fontTools.ttLib as ttLib
from fontTools.subset import Subsetter, Options

sample = r"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansCN-Regular.otf"

font = ttLib.TTFont(sample)
options = Options()
options.desubroutinize = True
options.layout_features = ['*']
subsetter = Subsetter(options=options)
subsetter.populate(unicodes=[ord('中'), ord('文')])
subsetter.subset(font)

cff = font['CFF ']
cff_data = font.getTableData('CFF ')
print(f"fonttools CFF table size: {len(cff_data)} bytes")

# Analyze CFF structure
print("\n=== CFF Structure ===")
print(f"Header: major={cff_data[0]}, minor={cff_data[1]}, hdrSize={cff_data[2]}, offSize={cff_data[3]}")

# Parse Name INDEX (after header)
pos = cff_data[2]  # hdrSize
def read_index(data, pos):
    count = (data[pos] << 8) | data[pos+1]
    if count == 0:
        return [], pos + 2
    offSize = data[pos + 2]
    offsets = []
    for i in range(count + 1):
        off = 0
        for j in range(offSize):
            off = (off << 8) | data[pos + 3 + i * offSize + j]
        offsets.append(off)
    data_start = pos + 3 + (count + 1) * offSize
    items = []
    for i in range(count):
        item_data = data[data_start + offsets[i] - 1:data_start + offsets[i+1] - 1]
        items.append(item_data)
    end_pos = data_start + offsets[-1] - 1
    return items, end_pos

name_index, pos = read_index(cff_data, pos)
print(f"Name INDEX: {len(name_index)} names at pos {pos}")
for i, name in enumerate(name_index):
    print(f"  [{i}] {name}")

top_dict_index, pos = read_index(cff_data, pos)
print(f"TopDICT INDEX: {len(top_dict_index)} at pos {pos}")

string_index, pos = read_index(cff_data, pos)
print(f"String INDEX: {len(string_index)} strings at pos {pos}")

global_subr_index, pos = read_index(cff_data, pos)
print(f"GlobalSubr INDEX: {len(global_subr_index)} subrs at pos {pos}")

# Show CharStrings
cs = cff.cff.topDictIndex[0].CharStrings
print(f"\nCharStrings: {len(cs)} glyphs")
for name in list(cs.keys())[:5]:
    cs_data = cs[name].program
    print(f"  {name}: {len(cs.charStrings[name])} bytes")
