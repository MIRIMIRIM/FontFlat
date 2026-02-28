# OTFontFile2

`OTFontFile2` is a modern OpenType parser/writer for .NET, focused on:
- high performance
- low allocation
- span-first table access

It supports both single-font sfnt (`.ttf/.otf`) and TTC collections (`.ttc`).

## Quick Start

### 1) Open a font file

```csharp
using OTFontFile2;

using var file = SfntFile.Open("font.ttf"); // or font.ttc
var font = file.GetFont(0);                 // first font in file/collection
```

### 2) Read common tables

```csharp
using OTFontFile2.Tables;

if (font.TryGetHead(out var head))
{
    int unitsPerEm = head.UnitsPerEm;
}

if (font.TryGetMaxp(out var maxp))
{
    int glyphCount = maxp.NumGlyphs;
}

if (font.TryGetName(out var name))
{
    string? fullName = name.GetFullNameString();
}
```

### 3) Map Unicode to glyph id (`cmap`)

```csharp
using OTFontFile2;

if (CmapUnicodeMap.TryCreate(font, out var map) &&
    map.TryMapCodePoint(0x4F60, out uint glyphId)) // U+4F60
{
    // glyphId
}
```

### 4) Repack/write font

```csharp
using OTFontFile2;

using var inFile = SfntFile.Open("in.ttf");
var inFont = inFile.GetFont(0);

using var outStream = File.Create("out.ttf");
SfntWriter.Write(outStream, inFont);
```

## Core API

### SfntFile
- `SfntFile.Open(path)`: open from disk.
- `SfntFile.TryOpen(path, out file, out error)`: non-throwing open.
- `SfntFile.FromMemory(memory)`: open from in-memory bytes.
- `FontCount`: number of fonts (1 for normal sfnt, >1 for TTC).
- `GetFont(index)`: get `SfntFont`.

### SfntFont
- `TableCount`: number of tables in this font.
- `TryGetTable(tag, out record)`: table directory lookup.
- `TryGetTableData(tag, out data, out record)`: raw bytes of a table.
- `TryGetTableSlice(tag, out slice)`: zero-copy table slice.
- Generated helpers like `TryGetHead`, `TryGetMaxp`, `TryGetName`, `TryGetCmap`, `TryGetGlyf`, etc.

### Table views
Most table structs in `OTFontFile2.Tables` expose:
- `TryCreate(...)`
- typed field properties (e.g. `UnitsPerEm`, `NumGlyphs`)
- helper methods for common read scenarios

## Build New sfnt from table bytes

```csharp
using OTFontFile2;

Tag.TryParse("head", out var headTag);
Tag.TryParse("cmap", out var cmapTag);

var builder = new SfntBuilder
{
    SfntVersion = 0x00010000
};

builder.SetTable(headTag, headBytes);
builder.SetTable(cmapTag, cmapBytes);

using var fs = File.Create("new.ttf");
builder.WriteTo(fs);
```

## Notes

- API is span-based and optimized for low allocations.
- Current APIs are designed for safe table access; invalid font data usually returns `false` instead of throwing.
- Very large files (>2GB) are not supported by span-based offsets.
