from fontTools.ttLib import TTFont
import os, tempfile

our_path = os.path.join(tempfile.gettempdir(), "verify_ours.otf")
pyft_path = os.path.join(tempfile.gettempdir(), "verify_pyft.otf")

print("=== Table Size Comparison ===\n")
print(f"{'Table':<8} {'Ours':>8} {'pyft':>8} {'Ratio':>10}")
print("-" * 40)

our_font = TTFont(our_path)
pyft_font = TTFont(pyft_path)

# Only compare real tables, not pseudo-tables like GlyphOrder
real_tables = [t for t in (set(our_font.keys()) | set(pyft_font.keys())) 
               if not t.startswith('_') and t not in ('GlyphOrder',)]

for tag in sorted(real_tables):
    try:
        our_size = len(our_font.getTableData(tag)) if tag in our_font else 0
    except:
        our_size = 0
    try:
        pyft_size = len(pyft_font.getTableData(tag)) if tag in pyft_font else 0
    except:
        pyft_size = 0
    
    ratio = 100.0 * our_size / pyft_size if pyft_size > 0 else 0
    status = "✓" if abs(ratio - 100) <= 5 else ("MISS" if our_size == 0 else "≠")
    print(f"{tag:<8} {our_size:>8} {pyft_size:>8} {ratio:>9.1f}% {status}")

print(f"\n=== Summary ===")
print(f"Ours: {os.path.getsize(our_path)} bytes")
print(f"pyft: {os.path.getsize(pyft_path)} bytes")
print(f"Ratio: {100.0 * os.path.getsize(our_path) / os.path.getsize(pyft_path):.1f}%")
