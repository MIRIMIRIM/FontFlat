import fontTools.ttLib as ttLib
from fontTools.subset import Subsetter, Options
import os, tempfile

sample = r"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansCN-Regular.otf"
temp_dir = tempfile.gettempdir()

def subset_with_options(layout_features=None, layout_scripts=None):
    font = ttLib.TTFont(sample)
    options = Options()
    options.desubroutinize = True
    
    if layout_features is not None:
        options.layout_features = layout_features
    if layout_scripts is not None:
        options.layout_scripts = layout_scripts
    
    subsetter = Subsetter(options=options)
    subsetter.populate(unicodes=[ord('中'), ord('文')])
    subsetter.subset(font)
    
    path = os.path.join(temp_dir, f"ft_test_{layout_features}_{layout_scripts}.otf")
    font.save(path)
    
    size = os.path.getsize(path)
    gsub_size = len(font.getTableData('GSUB')) if 'GSUB' in font else 0
    gpos_size = len(font.getTableData('GPOS')) if 'GPOS' in font else 0
    
    print(f"layout_features={layout_features}, layout_scripts={layout_scripts}")
    print(f"  Total: {size} bytes")
    print(f"  GSUB: {gsub_size} bytes")
    print(f"  GPOS: {gpos_size} bytes")
    print()
    return size

# Test various combinations
print("=== pyftsubset Layout Options Testing ===\n")

# Default (all features)
subset_with_options(layout_features=None)

# Specific features
subset_with_options(layout_features=['kern'])
subset_with_options(layout_features=['*'])  # All
subset_with_options(layout_features=[])  # Empty = none

# Scripts
subset_with_options(layout_scripts=['*'])  # All scripts
subset_with_options(layout_scripts=['hani'])  # Only Han
