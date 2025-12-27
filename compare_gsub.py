import fontTools.ttLib as ttLib
from fontTools.subset import Subsetter, Options
import os

# Use workspace temp directory
temp = r"F:\GitHub\FontFlat\temp_compare"
sample = r"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansCN-Regular.otf"

# Create temp dir if needed
os.makedirs(temp, exist_ok=True)

# Generate fonttools output
theirs = os.path.join(temp, "fonttools_cff.otf")

# Subset with fonttools
font = ttLib.TTFont(sample)
options = Options()
options.desubroutinize = True
options.layout_features = ['*']
subsetter = Subsetter(options=options)
subsetter.populate(unicodes=[ord('中'), ord('文')])
subsetter.subset(font)
font.save(theirs)
print(f"Saved fonttools subset to: {theirs}")

# Analyze
font = ttLib.TTFont(theirs)
print("\n=== pyftsubset GSUB (66 bytes) ===")
if 'GSUB' in font:
    gsub = font['GSUB']
    print(f"Scripts: {len(gsub.table.ScriptList.ScriptRecord) if gsub.table.ScriptList else 0}")
    if gsub.table.ScriptList:
        for sr in gsub.table.ScriptList.ScriptRecord:
            defls = sr.Script.DefaultLangSys
            print(f"  {sr.ScriptTag}: DefLangSys={defls is not None}", end="")
            if defls:
                print(f" (ReqFeat={defls.ReqFeatureIndex}, Feats={len(getattr(defls, 'FeatureIndex', []))})", end="")
            print(f", LangSysRecs={len(sr.Script.LangSysRecord) if sr.Script.LangSysRecord else 0}")
    print(f"Features: {len(gsub.table.FeatureList.FeatureRecord) if gsub.table.FeatureList else 0}")
    print(f"Lookups: {len(gsub.table.LookupList.Lookup) if gsub.table.LookupList else 0}")
    
    # Get raw bytes
    gsub_bytes = font.getTableData('GSUB')
    print(f"Total bytes: {len(gsub_bytes)}")
else:
    print("No GSUB table")

# Now analyze our output
print("\n=== Generating our output ===")
import subprocess
import sys

# Run dotnet test to generate our output
test_cmd = r'dotnet test OTFontFile.Performance.Tests/OTFontFile.Performance.Tests.csproj --filter "FullyQualifiedName~Compare_CFF_Subsetting_WithPyftsubset"'
# subprocess.run(test_cmd, shell=True, cwd=r"F:\GitHub\FontFlat", capture_output=True)

# Read our output from system temp
import tempfile
ours = os.path.join(tempfile.gettempdir(), "OTFontFile_ExternalTool_Tests", "ours_cff.otf")

if os.path.exists(ours):
    font2 = ttLib.TTFont(ours)
    print("\n=== Our GSUB (168 bytes) ===")
    if 'GSUB' in font2:
        gsub = font2['GSUB']
        print(f"Scripts: {len(gsub.table.ScriptList.ScriptRecord) if gsub.table.ScriptList else 0}")
        if gsub.table.ScriptList:
            for sr in gsub.table.ScriptList.ScriptRecord:
                defls = sr.Script.DefaultLangSys
                print(f"  {sr.ScriptTag}: DefLangSys={defls is not None}", end="")
                if defls:
                    print(f" (ReqFeat={defls.ReqFeatureIndex}, Feats={len(getattr(defls, 'FeatureIndex', []))})", end="")
                print(f", LangSysRecs={len(sr.Script.LangSysRecord) if sr.Script.LangSysRecord else 0}")
        print(f"Features: {len(gsub.table.FeatureList.FeatureRecord) if gsub.table.FeatureList else 0}")
        print(f"Lookups: {len(gsub.table.LookupList.Lookup) if gsub.table.LookupList else 0}")
        
        gsub_bytes = font2.getTableData('GSUB')
        print(f"Total bytes: {len(gsub_bytes)}")
    else:
        print("No GSUB table")
else:
    print(f"Our font not found: {ours}")
    print("Please run the test first to generate the file")
