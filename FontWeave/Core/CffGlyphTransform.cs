using FontWeave.Containers;
using FontWeave.Tables;
namespace FontWeave.Core;

/// <summary>R3's bounded static CID-CFF1 experiment, not a text-subsetting API.
/// The remap profile rejects layout and every table whose GID safety is not implemented.</summary>
public static class CffGlyphTransform
{
    private const uint Cff = 0x43464620, Head = 0x68656164, Maxp = 0x6D617870;
    private const uint Cmap = 0x636D6170, Hhea = 0x68686561, Hmtx = 0x686D7478;
    private const uint Vhea = 0x76686561, Vmtx = 0x766D7478, Post = 0x706F7374;

    /// <summary>Recompiles CFF's internal layout with identity GIDs; other table payloads
    /// remain opaque. This is not full-font validation or normalization.</summary>
    public static ReadStatus TryRebuild(FontFaceRef face, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = TryRebuildCore(face, out output, out diagnostic, budget);
        CompleteProvenance(face, status, ref diagnostic);
        return status;
    }

    /// <summary>Produces a fully materialized, source-independent compiled font.
    /// No output is published when any parsing, compilation or budget check fails.</summary>
    public static ReadStatus TryPrepareRebuild(FontFaceRef face, out CffCompiledFont? result,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        result = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = TryRebuildCore(face, out byte[]? output, out diagnostic, budget);
        CompleteProvenance(face, status, ref diagnostic);
        if (status == ReadStatus.Done) result = new CffCompiledFont(output!, budget.Usage);
        return status;
    }

    private static ReadStatus TryRebuildCore(FontFaceRef face, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        ReadStatus status = ReadInput(face, false, out var tables, out var font, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Identity input, frozen output-to-input copy and reverse map payloads.
        DataLocation mapLocation = face.Location with { Basis = OffsetBasis.None, Offset = 0, Field = DataField.GlyphId };
        if (!budget.Buffer(8L * font!.GlyphCount, mapLocation, out diagnostic) ||
            !budget.Work(4L * font.GlyphCount, mapLocation, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort[] identity = new ushort[font!.GlyphCount];
        for (int i = 0; i < identity.Length; i++) identity[i] = (ushort)i;
        status = GlyphRemap.TryCreate(face, identity.Length, identity, out var map, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = font.TryCompile(map!, out byte[]? cff, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        tables![Cff] = cff!;
        return SfntEmitter.TryEmit(0x4F54544F, face.Location, tables, out output, out diagnostic, budget);
    }

    /// <summary>One source-bound frozen map drives CFF, cmap, maxp and both metric pairs.
    /// No layout closure, subroutine pruning or silent original-table fallback is performed.</summary>
    public static ReadStatus TryRemap(FontFaceRef face, GlyphRemap map, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = TryRemapCore(face, map, out output, out diagnostic, budget);
        CompleteProvenance(face, status, ref diagnostic);
        return status;
    }

    public static ReadStatus TryPrepareRemap(FontFaceRef face, GlyphRemap map, out CffCompiledFont? result,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        result = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = TryRemapCore(face, map, out byte[]? output, out diagnostic, budget);
        CompleteProvenance(face, status, ref diagnostic);
        if (status == ReadStatus.Done) result = new CffCompiledFont(output!, budget.Usage);
        return status;
    }

    private static ReadStatus TryRemapCore(FontFaceRef face, GlyphRemap map, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(map);
        output = null;
        diagnostic = default;
        if (face != map.SourceFace)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                face.Location.At(DataField.GlyphId, 0) with { Basis = OffsetBasis.None }, out diagnostic,
                (ulong)face.SourceId, (ulong)map.SourceFace.SourceId);
        ReadStatus status = ReadInput(face, true, out var tables, out var font, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (font!.GlyphCount != map.InputGlyphCount)
            return Fail(face, Maxp, DataField.Count, 4, out diagnostic,
                (ulong)map.InputGlyphCount, (ulong)font.GlyphCount);

        // Compile into local tables. Nothing becomes observable until the container succeeds.
        status = CompileMetrics(face, tables!, map, Hhea, Hmtx, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        bool hasVhea = tables!.ContainsKey(Vhea), hasVmtx = tables.ContainsKey(Vmtx);
        if (hasVhea != hasVmtx)
            return Missing(face, hasVhea ? Vmtx : Vhea, out diagnostic);
        if (hasVhea)
        {
            status = CompileMetrics(face, tables, map, Vhea, Vmtx, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (!tables.TryGetValue(Cmap, out byte[]? cmap)) return Missing(face, Cmap, out diagnostic);
        status = CompileCmap(face, cmap, map, out byte[]? newCmap, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = font.TryCompile(map, out byte[]? cff, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        tables[Cff] = cff!;
        tables[Cmap] = newCmap!;
        SfntEmitter.W16(tables[Maxp], 4, (ushort)map.OutputGlyphCount);
        return SfntEmitter.TryEmit(0x4F54544F, face.Location, tables, out output, out diagnostic, budget);
    }

    private static ReadStatus ReadInput(FontFaceRef face, bool remapping,
        out SortedDictionary<uint, byte[]>? tables, out CffCidFont? font,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        tables = null;
        font = null;
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(directory.SourceByteLength, face.Location, out diagnostic) ||
            !budget.Work((long)directory.DeclaredCount * directory.DeclaredCount + 4L * directory.DeclaredCount,
                face.Location.At(DataField.Count, (ulong)face.DirectoryFileOffset + 4), out diagnostic))
            return ReadStatus.ResourceLimit;
        status = directory.Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (directory.Version != 0x4F54544F)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                face.Location.At(DataField.Version, face.DirectoryFileOffset),
                out diagnostic, directory.Version, 0x4F54544F);
        var selected = new SortedDictionary<uint, byte[]>();
        for (int i = 0; i < directory.DeclaredCount; i++)
        {
            status = directory.TryGetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = directory.TryGetTableBytes(i, out var data, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // Source Han carries the canonical version-1 empty signature marker.
            // Identity rebuilding may preserve it, but never an actual or unknown signature.
            bool unsupportedSignature = record.Tag == 0x44534947 &&
                (remapping || data.Length != 8 || ReadWire.U32(data, 0) != 1 || ReadWire.U32(data, 4) != 0);
            if (IsNonStatic(record.Tag) || unsupportedSignature || (remapping && !IsRemapTable(record.Tag)))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    face.Location.At(DataField.Tag, face.DirectoryFileOffset + 12UL + 16UL * (uint)i, i)
                        with { TableTag = record.Tag, TableOrdinal = i }, out diagnostic, record.Tag);
            // CFF is borrowed by the semantic reader and always replaced by its compiler.
            if (record.Tag == Cff) continue;
            DataLocation location = directory.TableLocation(i).At(DataField.Length, 0);
            if (!budget.Buffer(data.Length, location, out diagnostic) ||
                !budget.Work(data.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
            selected.Add(record.Tag, data.ToArray());
            budget.RecordTableCopy(data.Length);
        }
        if (!selected.TryGetValue(Head, out var head)) return Missing(face, Head, out diagnostic);
        status = HeadView.TryOpen(head, out var headView, out diagnostic,
            Location(face, Head, DataField.None, 0));
        if (status != ReadStatus.Done) return status;
        status = headView.Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!selected.TryGetValue(Maxp, out var maxp)) return Missing(face, Maxp, out diagnostic);
        if (maxp.Length < 6) return Truncated(face, Maxp, maxp.Length, 6, out diagnostic);
        if (ReadWire.U32(maxp, 0) != 0x00005000)
            return Unsupported(face, Maxp, DataField.Version, 0, out diagnostic, ReadWire.U32(maxp, 0));
        if (remapping && selected.TryGetValue(Post, out var post))
        {
            if (post.Length < 32) return Truncated(face, Post, post.Length, 32, out diagnostic);
            if (ReadWire.U32(post, 0) != 0x00030000)
                return Unsupported(face, Post, DataField.Version, 0, out diagnostic, ReadWire.U32(post, 0));
        }
        status = CffCidFont.TryRead(face, out font, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (ReadWire.U16(maxp, 4) != font!.GlyphCount)
            return Fail(face, Maxp, DataField.Count, 4, out diagnostic,
                ReadWire.U16(maxp, 4), (ulong)font.GlyphCount);
        tables = selected;
        return ReadStatus.Done;
    }

    private static bool IsRemapTable(uint tag) => tag is Cff or Head or Maxp or Cmap or
        Hhea or Hmtx or Vhea or Vmtx or Post or 0x6E616D65 /* name */ or 0x4F532F32 /* OS/2 */;

    private static bool IsNonStatic(uint tag) => tag is
        0x43464632 /* CFF2 */ or 0x676C7966 /* glyf */ or
        0x6C6F6361 /* loca */ or 0x66766172 /* fvar */ or 0x67766172 /* gvar */ or
        0x61766172 /* avar */ or 0x48564152 /* HVAR */ or 0x56564152 /* VVAR */ or
        0x4D564152 /* MVAR */ or 0x63766172 /* cvar */;

    private static ReadStatus CompileMetrics(FontFaceRef face, SortedDictionary<uint, byte[]> tables,
        GlyphRemap map, uint headerTag, uint metricsTag, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        diagnostic = default;
        if (!tables.TryGetValue(headerTag, out byte[]? header)) return Missing(face, headerTag, out diagnostic);
        if (!tables.TryGetValue(metricsTag, out byte[]? metrics)) return Missing(face, metricsTag, out diagnostic);
        if (header.Length < 36) return Truncated(face, headerTag, header.Length, 36, out diagnostic);
        uint version = ReadWire.U32(header, 0);
        if (version != 0x00010000 && !(headerTag == Vhea && version == 0x00011000))
            return Unsupported(face, headerTag, DataField.Version, 0, out diagnostic, version);
        if (ReadWire.U16(header, 32) != 0)
            return Unsupported(face, headerTag, DataField.Version, 32, out diagnostic, ReadWire.U16(header, 32));
        int longCount = ReadWire.U16(header, 34);
        if (longCount < 1 || longCount > map.InputGlyphCount)
            return Fail(face, headerTag, DataField.Count, 34, out diagnostic,
                (ulong)longCount, (ulong)map.InputGlyphCount);
        int needed = 4 * longCount + 2 * (map.InputGlyphCount - longCount);
        if (metrics.Length < needed) return Truncated(face, metricsTag, metrics.Length, needed, out diagnostic);
        int compiledLength = 4 * map.OutputGlyphCount;
        DataLocation location = Location(face, metricsTag, DataField.Length, 0);
        if (!budget.Buffer(compiledLength, location, out diagnostic) ||
            !budget.Work(compiledLength, location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] compiled = new byte[compiledLength];
        for (int i = 0; i < map.OutputGlyphCount; i++)
        {
            ReadStatus status = map.TryGetInputGlyph(new((ushort)i), out var input, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int gid = input.Value;
            int advanceAt = 4 * Math.Min(gid, longCount - 1);
            int bearingAt = gid < longCount ? 4 * gid + 2 : 4 * longCount + 2 * (gid - longCount);
            SfntEmitter.W16(compiled, 4 * i, ReadWire.U16(metrics, advanceAt));
            SfntEmitter.W16(compiled, 4 * i + 2, ReadWire.U16(metrics, bearingAt));
        }
        SfntEmitter.W16(header, 34, (ushort)map.OutputGlyphCount);
        tables[metricsTag] = compiled;
        budget.RecordCompiledTable(compiled.Length);
        return ReadStatus.Done;
    }

    private readonly record struct CmapEntry(uint Character, ushort Glyph);

    private static ReadStatus CompileCmap(FontFaceRef face, ReadOnlySpan<byte> bytes, GlyphRemap map,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        diagnostic = default;
        if (bytes.Length < 4) return Truncated(face, Cmap, bytes.Length, 4, out diagnostic);
        if (ReadWire.U16(bytes, 0) != 0)
            return Unsupported(face, Cmap, DataField.Version, 0, out diagnostic, ReadWire.U16(bytes, 0));
        int count = ReadWire.U16(bytes, 2);
        int recordsEnd = 4 + 8 * count;
        if (count == 0) return Fail(face, Cmap, DataField.Count, 2, out diagnostic, 0, 1);
        if (recordsEnd > bytes.Length) return Truncated(face, Cmap, bytes.Length, recordsEnd, out diagnostic);
        if (!budget.Work(recordsEnd, Location(face, Cmap, DataField.Count, 2), out diagnostic))
            return ReadStatus.ResourceLimit;
        // Offset aliases are legitimate. Compile each unique subtable only once.
        // Dictionary/list object overhead is not a payload charge; record and character
        // work limits bound cardinality, while controlled list backing arrays are charged.
        var subtables = new Dictionary<uint, byte[]>();
        long outputLength = recordsEnd;
        for (int i = 0; i < count; i++)
        {
            int record = 4 + 8 * i;
            ushort platform = ReadWire.U16(bytes, record), encoding = ReadWire.U16(bytes, record + 2);
            if (platform != 0 && !(platform == 3 && encoding is 1 or 10))
                return Unsupported(face, Cmap, DataField.EncodingId, (ulong)record, out diagnostic,
                    ((ulong)platform << 16) | encoding);
            uint offset = ReadWire.U32(bytes, record + 4);
            if (subtables.ContainsKey(offset)) continue;
            if (offset < recordsEnd || !ReadWire.Slice(bytes, offset, 2, out var prefix))
                return OffsetFailure(face, Cmap, (ulong)record + 4, offset, (ulong)bytes.Length, out diagnostic);
            ushort format = ReadWire.U16(prefix, 0);
            var entries = new List<CmapEntry>();
            ReadStatus status;
            byte[]? compiled;
            if (format == 4)
            {
                status = ReadCmap4(face, bytes[(int)offset..], offset, map, entries,
                    out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = WriteCmap4(face, entries, ReadWire.U16(bytes, (int)offset + 4),
                    out compiled, out diagnostic, budget);
            }
            else if (format == 12)
            {
                status = ReadCmap12(face, bytes[(int)offset..], offset, map, entries,
                    out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = WriteCmap12(face, entries, ReadWire.U32(bytes, (int)offset + 8),
                    out compiled, out diagnostic, budget);
            }
            else return Unsupported(face, Cmap, DataField.Version, offset, out diagnostic, format);
            if (status != ReadStatus.Done) return status;
            outputLength += compiled!.Length;
            if (outputLength > SfntEmitter.MaxOutputBytes)
                return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                    Location(face, Cmap, DataField.Length, 0), out diagnostic,
                    (ulong)outputLength, SfntEmitter.MaxOutputBytes);
            subtables.Add(offset, compiled);
        }
        DataLocation outputLocation = Location(face, Cmap, DataField.Length, 0);
        if (!budget.Buffer(outputLength, outputLocation, out diagnostic) ||
            !budget.Work(outputLength, outputLocation, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] result = new byte[(int)outputLength];
        bytes[..recordsEnd].CopyTo(result);
        var newOffsets = new Dictionary<uint, uint>();
        int cursor = recordsEnd;
        for (int i = 0; i < count; i++)
        {
            int record = 4 + 8 * i;
            uint sourceOffset = ReadWire.U32(bytes, record + 4);
            if (!newOffsets.TryGetValue(sourceOffset, out uint newOffset))
            {
                newOffset = (uint)cursor;
                byte[] compiled = subtables[sourceOffset];
                compiled.CopyTo(result.AsSpan(cursor));
                cursor += compiled.Length;
                newOffsets.Add(sourceOffset, newOffset);
            }
            SfntEmitter.W32(result, record + 4, newOffset);
        }
        output = result;
        budget.RecordCompiledTable(result.Length);
        return ReadStatus.Done;
    }

    private static ReadStatus ReadCmap4(FontFaceRef face, ReadOnlySpan<byte> bytes, uint tableOffset,
        GlyphRemap map, List<CmapEntry> entries, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        diagnostic = default;
        if (bytes.Length < 16) return Truncated(face, Cmap, bytes.Length, 16, out diagnostic, tableOffset);
        int length = ReadWire.U16(bytes, 2), segCountX2 = ReadWire.U16(bytes, 6);
        if (length > bytes.Length) return Truncated(face, Cmap, bytes.Length, length, out diagnostic, tableOffset);
        int count = segCountX2 / 2;
        if (count == 0 || (segCountX2 & 1) != 0 || length < 16 + 8 * count)
            return Fail(face, Cmap, DataField.Count, tableOffset + 6UL, out diagnostic, (ulong)segCountX2);
        if (!budget.Work(8L * count, Location(face, Cmap, DataField.Count, tableOffset + 6UL), out diagnostic))
            return ReadStatus.ResourceLimit;
        bytes = bytes[..length];
        if (ReadWire.U16(bytes, 14 + 2 * count) != 0)
            return Fail(face, Cmap, DataField.Payload, tableOffset + (ulong)(14 + 2 * count), out diagnostic);
        int previousEnd = -1;
        for (int i = 0; i < count; i++)
        {
            int end = ReadWire.U16(bytes, 14 + 2 * i);
            int start = ReadWire.U16(bytes, 16 + 2 * count + 2 * i);
            ushort delta = ReadWire.U16(bytes, 16 + 4 * count + 2 * i);
            int rangeAt = 16 + 6 * count + 2 * i;
            int rangeOffset = ReadWire.U16(bytes, rangeAt);
            if (start > end || start <= previousEnd || (rangeOffset & 1) != 0)
                return Fail(face, Cmap, DataField.GlyphId, tableOffset + (ulong)rangeAt, out diagnostic);
            previousEnd = end;
            if (!budget.Work(8L * (end - start + 1),
                Location(face, Cmap, DataField.GlyphId, tableOffset + (ulong)rangeAt), out diagnostic))
                return ReadStatus.ResourceLimit;
            for (int character = start; character <= end; character++)
            {
                ushort gid;
                if (rangeOffset == 0) gid = unchecked((ushort)(character + delta));
                else
                {
                    int at = rangeAt + rangeOffset + 2 * (character - start);
                    if (at < 16 + 8 * count || at > length - 2)
                        return OffsetFailure(face, Cmap, tableOffset + (ulong)rangeAt,
                            tableOffset + (ulong)at, tableOffset + (ulong)length, out diagnostic);
                    gid = ReadWire.U16(bytes, at);
                    if (gid != 0) gid = unchecked((ushort)(gid + delta));
                }
                ReadStatus status = MapCharacter(face, map, (uint)character, gid, entries,
                    tableOffset + (ulong)rangeAt, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        if (previousEnd != 0xFFFF)
            return Fail(face, Cmap, DataField.GlyphId, tableOffset + 14UL, out diagnostic, (ulong)previousEnd, 0xFFFF);
        return ReadStatus.Done;
    }

    private static ReadStatus ReadCmap12(FontFaceRef face, ReadOnlySpan<byte> bytes, uint tableOffset,
        GlyphRemap map, List<CmapEntry> entries, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        diagnostic = default;
        if (bytes.Length < 16) return Truncated(face, Cmap, bytes.Length, 16, out diagnostic, tableOffset);
        uint length = ReadWire.U32(bytes, 4), count = ReadWire.U32(bytes, 12);
        if (length > bytes.Length) return Truncated(face, Cmap, bytes.Length, length, out diagnostic, tableOffset);
        if (ReadWire.U16(bytes, 2) != 0 || length < 16 || 16UL + 12UL * count > length)
            return Fail(face, Cmap, DataField.Count, tableOffset + 12UL, out diagnostic, count);
        if (!budget.Work(12L * count, Location(face, Cmap, DataField.Count, tableOffset + 12UL), out diagnostic))
            return ReadStatus.ResourceLimit;
        uint previousEnd = 0;
        for (uint i = 0; i < count; i++)
        {
            int at = 16 + (int)i * 12;
            uint start = ReadWire.U32(bytes, at), end = ReadWire.U32(bytes, at + 4);
            uint firstGlyph = ReadWire.U32(bytes, at + 8);
            if (start > end || end > 0x10FFFF || (i > 0 && start <= previousEnd))
                return Fail(face, Cmap, DataField.Payload, tableOffset + (ulong)at, out diagnostic, end, 0x10FFFF);
            if ((ulong)firstGlyph + end - start >= (ulong)map.InputGlyphCount)
                return Fail(face, Cmap, DataField.GlyphId, tableOffset + (ulong)at + 8,
                    out diagnostic, (ulong)firstGlyph + end - start, (ulong)map.InputGlyphCount);
            previousEnd = end;
            if (!budget.Work(8L * (end - start + 1),
                Location(face, Cmap, DataField.GlyphId, tableOffset + (ulong)at), out diagnostic))
                return ReadStatus.ResourceLimit;
            for (uint character = start; character <= end; character++)
            {
                ReadStatus status = MapCharacter(face, map, character,
                    (ushort)(firstGlyph + character - start), entries,
                    tableOffset + (ulong)at + 8, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        return ReadStatus.Done;
    }

    private static ReadStatus MapCharacter(FontFaceRef face, GlyphRemap map, uint character,
        ushort gid, List<CmapEntry> entries, ulong at, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        diagnostic = default;
        if (gid >= map.InputGlyphCount)
            return Fail(face, Cmap, DataField.GlyphId, at, out diagnostic, gid, (ulong)map.InputGlyphCount);
        if (gid == 0) return ReadStatus.Done;
        ReadStatus status = map.TryGetOutputGlyph(face, new(gid), out var mapped, out diagnostic);
        if (status == ReadStatus.NotFound) { diagnostic = default; return ReadStatus.Done; }
        if (status != ReadStatus.Done) return status;
        if (!GrowCmapList(entries, 8, budget, Location(face, Cmap, DataField.GlyphId, at), out diagnostic))
            return ReadStatus.ResourceLimit;
        entries.Add(new(character, mapped.Value));
        return ReadStatus.Done;
    }

    private readonly record struct CmapRange(uint Start, uint End, ushort FirstGlyph);

    private static ReadStatus GroupEntries(FontFaceRef face, List<CmapEntry> entries,
        out List<CmapRange> ranges, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ranges = new List<CmapRange>();
        DataLocation location = Location(face, Cmap, DataField.Count, 0);
        if (!budget.Work(entries.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        foreach (CmapEntry entry in entries)
        {
            if (ranges.Count > 0)
            {
                CmapRange last = ranges[^1];
                if (entry.Character == last.End + 1 &&
                    entry.Glyph == (ulong)last.FirstGlyph + entry.Character - last.Start)
                {
                    ranges[^1] = last with { End = entry.Character };
                    continue;
                }
            }
            if (!GrowCmapList(ranges, 12, budget, location, out diagnostic)) return ReadStatus.ResourceLimit;
            ranges.Add(new(entry.Character, entry.Character, entry.Glyph));
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus WriteCmap12(FontFaceRef face, List<CmapEntry> entries, uint language,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        ReadStatus status = GroupEntries(face, entries, out var ranges, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int length = 16 + 12 * ranges.Count;
        DataLocation location = Location(face, Cmap, DataField.Length, 0);
        if (!budget.Buffer(length, location, out diagnostic) ||
            !budget.Work(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] result = new byte[length];
        SfntEmitter.W16(result, 0, 12);
        SfntEmitter.W32(result, 4, (uint)result.Length);
        SfntEmitter.W32(result, 8, language);
        SfntEmitter.W32(result, 12, (uint)ranges.Count);
        for (int i = 0; i < ranges.Count; i++)
        {
            SfntEmitter.W32(result, 16 + 12 * i, ranges[i].Start);
            SfntEmitter.W32(result, 20 + 12 * i, ranges[i].End);
            SfntEmitter.W32(result, 24 + 12 * i, ranges[i].FirstGlyph);
        }
        output = result;
        return ReadStatus.Done;
    }

    private static ReadStatus WriteCmap4(FontFaceRef face, List<CmapEntry> entries, ushort language,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        diagnostic = default;
        // Format 4's required terminal segment cannot encode a nonzero U+FFFF mapping.
        if (entries.Count > 0 && entries[^1].Character == 0xFFFF)
            return Unsupported(face, Cmap, DataField.GlyphId, 0, out diagnostic, 0xFFFF);
        ReadStatus status = GroupEntries(face, entries, out var ranges, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        DataLocation location = Location(face, Cmap, DataField.Length, 0);
        if (!GrowCmapList(ranges, 12, budget, location, out diagnostic)) return ReadStatus.ResourceLimit;
        ranges.Add(new(0xFFFF, 0xFFFF, 0));
        int count = ranges.Count, length = 16 + 8 * count;
        if (length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location(face, Cmap, DataField.Length, 0), out diagnostic, (ulong)length, ushort.MaxValue);
        if (!budget.Buffer(length, location, out diagnostic) ||
            !budget.Work(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] result = new byte[length];
        SfntEmitter.W16(result, 0, 4);
        SfntEmitter.W16(result, 2, (ushort)length);
        SfntEmitter.W16(result, 4, language);
        SfntEmitter.W16(result, 6, (ushort)(2 * count));
        int searchRange = 2, selector = 0;
        while (searchRange <= count) { searchRange *= 2; selector++; }
        SfntEmitter.W16(result, 8, (ushort)searchRange);
        SfntEmitter.W16(result, 10, (ushort)selector);
        SfntEmitter.W16(result, 12, (ushort)(2 * count - searchRange));
        for (int i = 0; i < count; i++)
        {
            SfntEmitter.W16(result, 14 + 2 * i, (ushort)ranges[i].End);
            SfntEmitter.W16(result, 16 + 2 * count + 2 * i, (ushort)ranges[i].Start);
            SfntEmitter.W16(result, 16 + 4 * count + 2 * i,
                unchecked((ushort)(ranges[i].FirstGlyph - ranges[i].Start)));
        }
        output = result;
        return ReadStatus.Done;
    }

    private static bool GrowCmapList<T>(List<T> list, int itemBytes, BuildBudget budget,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (list.Count < list.Capacity) return true;
        // Each cmap has at most 0x110000 scalar-code positions, so doubling fits Int32.
        int capacity = list.Capacity == 0 ? 4 : 2 * list.Capacity;
        if (!budget.Buffer((long)capacity * itemBytes, location, out diagnostic) ||
            !budget.Work(list.Count, location, out diagnostic)) return false;
        list.Capacity = capacity;
        return true;
    }

    private static DataLocation Location(FontFaceRef face, uint tag, DataField field, ulong at) =>
        new(face.SourceId, face.FaceIndex, tag, -1, field, OffsetBasis.Table, at);

    private static void CompleteProvenance(FontFaceRef face, ReadStatus status, ref FontDiagnostic diagnostic)
    {
        if (status == ReadStatus.Done || diagnostic.Location.TableTag == 0 ||
            diagnostic.Location.TableOrdinal >= 0 || diagnostic.Location.SourceId != face.SourceId)
            return;
        if (face.TryOpenDirectory(out var directory, out _) == ReadStatus.Done &&
            directory.TryFindUniqueTable(diagnostic.Location.TableTag, out int ordinal, out _) == ReadStatus.Done)
            diagnostic = diagnostic with { Location = diagnostic.Location with { TableOrdinal = ordinal } };
    }

    private static ReadStatus Missing(FontFaceRef face, uint tag, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.MissingTable, DiagnosticCode.TableNotFound,
            Location(face, tag, DataField.Tag, 0), out diagnostic, tag);
    private static ReadStatus Fail(FontFaceRef face, uint tag, DataField field, ulong at,
        out FontDiagnostic diagnostic, ulong detail = 0, ulong expected = 0) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            Location(face, tag, field, at), out diagnostic, detail, expected);
    private static ReadStatus Unsupported(FontFaceRef face, uint tag, DataField field, ulong at,
        out FontDiagnostic diagnostic, ulong detail) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
            Location(face, tag, field, at), out diagnostic, detail);
    private static ReadStatus Truncated(FontFaceRef face, uint tag, long length, long expected,
        out FontDiagnostic diagnostic, ulong at = 0) =>
        ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
            Location(face, tag, DataField.Length, at), out diagnostic, (ulong)length, (ulong)expected);
    private static ReadStatus OffsetFailure(FontFaceRef face, uint tag, ulong at,
        ulong offset, ulong length, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
            Location(face, tag, DataField.Offset, at), out diagnostic, offset, length);
}
