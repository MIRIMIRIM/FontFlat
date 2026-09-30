using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct CmapMappingEdit(uint CharacterCode, int? GlyphId);

public sealed record CmapSubtableEdit
{
    public int RecordOrdinal { get; init; }
    public required IReadOnlyList<CmapMappingEdit> Mappings { get; init; }
}

public sealed record CmapEditPlan
{
    public IReadOnlyList<CmapSubtableEdit> Subtables { get; init; } = [];
}

/// <summary>Canonical cmap compiler. Format 4 subtables are re-encoded as delta-only maximal
/// affine segments plus the structural sentinel; format 12 as maximal (cp+1, gid+1) merged
/// groups. Subtables outside formats 4/12 are preserved byte-for-byte, and records that
/// shared a subtable keep sharing the recompiled one.</summary>
public static class CmapTableCompiler
{
    // 16 + 8*segCount must stay within the uint16 subtable length.
    internal const int MaxFormat4Segments = 8189;

    public static ReadStatus TryCompile(CmapTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(CmapTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = EncodeAll(model, budget, out var payloads, out long payloadBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        long total = CmapLayout.HeaderLength + 8L * model.Records.Length + payloadBytes;
        if (!budget.Work(total, model.Location, out diagnostic) ||
            !budget.Output(total, model.Location, out diagnostic) ||
            !budget.Buffer(total, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[total];
        WriteShell(model, payloads, bytes);
        budget.RecordCompiledTable((int)total);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(CmapTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = TryCompile(model, out byte[]? encoded, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < encoded!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)encoded.Length);
        encoded.CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryApply(CmapTable model, CmapEditPlan edit, out CmapTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryApply(model, edit, out updated, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryApply(CmapTable model, CmapEditPlan edit, out CmapTable? updated,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Subtables);
        updated = null;
        if (edit.Subtables.Count == 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        if (!budget.Work(64L * edit.Subtables.Count, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var records = model.Records;
        var targets = new int[edit.Subtables.Count];
        var targetOrdinals = new int[edit.Subtables.Count];
        var sortedEdits = new CmapMappingEdit[edit.Subtables.Count][];
        for (int e = 0; e < edit.Subtables.Count; e++)
        {
            var request = edit.Subtables[e];
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Mappings);
            int ordinal = request.RecordOrdinal;
            if (ordinal < 0 || ordinal >= records.Length)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.Count, 2, ordinal), out diagnostic,
                    unchecked((ulong)(long)ordinal), (uint)records.Length);
            targets[e] = records[ordinal].SubtableIndex;
            targetOrdinals[e] = ordinal;
            for (int t = 0; t < e; t++)
                if (targets[t] == targets[e])
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        model.Location.At(DataField.Offset, records[ordinal].SourceOffset, ordinal),
                        out diagnostic, records[ordinal].SourceOffset,
                        records[targetOrdinals[t]].SourceOffset);
            _ = model.TryGetSubtable(ordinal, out var subtable, out diagnostic);
            if (subtable is not (CmapFormat4Subtable or CmapFormat12Subtable))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.At(DataField.Version, records[ordinal].SourceOffset, ordinal),
                    out diagnostic, subtable?.Format ?? 0);
            uint maxCode = subtable is CmapFormat4Subtable ? 0xFFFEu : 0x10FFFFu;
            var mappings = request.Mappings.ToArray();
            Array.Sort(mappings, static (a, b) => a.CharacterCode.CompareTo(b.CharacterCode));
            sortedEdits[e] = mappings;
            for (int m = 1; m < mappings.Length; m++)
                if (mappings[m].CharacterCode == mappings[m - 1].CharacterCode)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        model.Location.At(DataField.CharCode, mappings[m].CharacterCode, ordinal),
                        out diagnostic, mappings[m].CharacterCode, mappings[m - 1].CharacterCode);
            foreach (var mapping in mappings)
            {
                if (mapping.CharacterCode > maxCode ||
                    mapping.CharacterCode is >= 0xD800 and <= 0xDFFF)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        model.Location.At(DataField.CharCode, mapping.CharacterCode, ordinal),
                        out diagnostic, mapping.CharacterCode, maxCode);
                if (mapping.GlyphId is { } gid && (gid < 1 || gid >= model.NumGlyphs))
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        model.Location.At(DataField.GlyphId, mapping.CharacterCode, ordinal),
                        out diagnostic, unchecked((ulong)(long)gid), (uint)model.NumGlyphs);
            }
        }
        var replacements = new CmapSubtable[model.SubtableCount];
        var replaced = new bool[model.SubtableCount];
        bool changed = false;
        for (int e = 0; e < edit.Subtables.Count; e++)
        {
            var request = edit.Subtables[e];
            _ = model.TryGetSubtable(request.RecordOrdinal, out var subtable, out _);
            ReadStatus status;
            CmapMapping[]? existing;
            if (subtable is CmapFormat4Subtable format4)
            {
                status = CmapTable.EnumerateFormat4(format4, out existing, out diagnostic, budget);
            }
            else
            {
                status = CmapTable.EnumerateFormat12((CmapFormat12Subtable)subtable!,
                    out existing, out diagnostic, budget);
            }
            if (status != ReadStatus.Done) return status;
            status = ApplyMappings(existing!, sortedEdits[e], subtable!.Location,
                out var merged, out diagnostic, out bool editChanged);
            if (status != ReadStatus.Done) return status;
            changed |= editChanged;
            if (!editChanged) continue;
            if (subtable is CmapFormat4Subtable format4Subtable)
            {
                status = BuildFormat4Canonical(merged, subtable.Location,
                    out var segments, out var glyphArray, out diagnostic);
                if (status != ReadStatus.Done) return status;
                replacements[targets[e]] = new CmapFormat4Subtable(format4Subtable.Language,
                    segments, glyphArray, subtable.SourceOffset, subtable.Location);
            }
            else
            {
                var runs = BuildRuns(merged);
                var groups = new CmapFormat12Group[runs.Count];
                for (int r = 0; r < runs.Count; r++)
                    groups[r] = new(runs[r].Start, runs[r].End, runs[r].Glyph);
                replacements[targets[e]] = new CmapFormat12Subtable(groups,
                    subtable.SourceOffset, subtable.Location);
            }
            replaced[targets[e]] = true;
        }
        if (!changed)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        var finalSubtables = new CmapSubtable[model.SubtableCount];
        for (int j = 0; j < finalSubtables.Length; j++)
            finalSubtables[j] = replaced[j] ? replacements[j] : GetSubtableAt(model, j);
        updated = new CmapTable(model.Records.ToArray(), finalSubtables, model.NumGlyphs,
            model.Location, model.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static CmapSubtable GetSubtableAt(CmapTable model, int index)
    {
        for (int i = 0; i < model.Records.Length; i++)
            if (model.Records[i].SubtableIndex == index)
            {
                _ = model.TryGetSubtable(i, out var subtable, out _);
                return subtable!;
            }
        throw new InvalidOperationException("subtable index without referencing record");
    }

    /// <summary>Merges sorted edits into the sorted mapping list. Removing a code that is not
    /// mapped is rejected; re-asserting identical values counts toward the no-op check.</summary>
    private static ReadStatus ApplyMappings(CmapMapping[] existing, IReadOnlyList<CmapMappingEdit> edits,
        DataLocation location, out CmapMapping[] merged, out FontDiagnostic diagnostic, out bool changed)
    {
        merged = [];
        changed = false;
        var result = new List<CmapMapping>(existing.Length + edits.Count);
        int i = 0;
        for (int e = 0; e < edits.Count; e++)
        {
            uint code = edits[e].CharacterCode;
            while (i < existing.Length && existing[i].CharacterCode < code)
                result.Add(existing[i++]);
            if (i < existing.Length && existing[i].CharacterCode == code)
            {
                if (edits[e].GlyphId is { } gid)
                {
                    if (existing[i].GlyphId != gid) changed = true;
                    result.Add(new(code, (ushort)gid));
                }
                else changed = true;
                i++;
            }
            else if (edits[e].GlyphId is not { } gid)
            {
                merged = [];
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.At(DataField.CharCode, code), out diagnostic, code);
            }
            else
            {
                changed = true;
                result.Add(new(code, (ushort)gid));
            }
        }
        while (i < existing.Length)
            result.Add(existing[i++]);
        merged = [.. result];
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal readonly record struct Run(uint Start, uint End, uint Glyph);

    internal static List<Run> BuildRuns(CmapMapping[] mappings)
    {
        var runs = new List<Run>(mappings.Length);
        foreach (var mapping in mappings)
        {
            if (runs.Count > 0)
            {
                var last = runs[^1];
                if (mapping.CharacterCode == last.End + 1 &&
                    mapping.GlyphId == last.Glyph + (mapping.CharacterCode - last.Start))
                {
                    runs[^1] = last with { End = mapping.CharacterCode };
                    continue;
                }
            }
            runs.Add(new(mapping.CharacterCode, mapping.CharacterCode, mapping.GlyphId));
        }
        return runs;
    }

    private static ReadStatus EncodeAll(CmapTable model, BuildBudget budget,
        out byte[]?[] payloads, out long payloadBytes, out FontDiagnostic diagnostic)
    {
        payloads = new byte[model.SubtableCount][];
        payloadBytes = 0;
        for (int j = 0; j < model.SubtableCount; j++)
        {
            var subtable = GetSubtableAt(model, j);
            ReadStatus status;
            if (subtable is CmapFormat4Subtable format4)
            {
                status = CmapTable.EnumerateFormat4(format4, out var mappings, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = EncodeFormat4(format4.Language, mappings!, subtable.Location,
                    budget, out payloads[j], out diagnostic);
            }
            else if (subtable is CmapFormat12Subtable format12)
            {
                status = CmapTable.EnumerateFormat12(format12, out var mappings, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = EncodeFormat12(mappings!, subtable.Location, budget,
                    out payloads[j], out diagnostic);
            }
            else
            {
                payloads[j] = ((CmapRawSubtable)subtable).RawBytes.ToArray();
                if (!budget.Buffer(payloads[j]!.Length, subtable.Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                status = ReadStatus.Done;
            }
            if (status != ReadStatus.Done) return status;
            payloadBytes += payloads[j]!.Length;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Canonical format 4 structure: maximal contiguous mapped code runs, each encoded
    /// as an affine delta segment when possible and otherwise through a self-relative
    /// idRangeOffset window into one sequential glyph array, plus the structural sentinel.
    /// Shared by the compiler writer and the edited-model constructor so both agree.</summary>
    internal static ReadStatus BuildFormat4Canonical(CmapMapping[] mappings, DataLocation location,
        out CmapFormat4Segment[] segments, out ushort[] glyphArray, out FontDiagnostic diagnostic)
    {
        segments = [];
        glyphArray = [];
        var starts = new List<uint>();
        var firstIndices = new List<int>();
        var affineFlags = new List<bool>();
        for (int i = 0; i < mappings.Length; i++)
        {
            uint code = mappings[i].CharacterCode;
            if (starts.Count > 0 && code == mappings[firstIndices[^1]].CharacterCode + (uint)(i - firstIndices[^1]))
            {
                int first = firstIndices[^1];
                if (mappings[i].GlyphId != mappings[first].GlyphId + (i - first))
                    affineFlags[^1] = false;
            }
            else
            {
                starts.Add(code);
                firstIndices.Add(i);
                affineFlags.Add(true);
            }
        }
        int runCount = starts.Count;
        int segCount = runCount + 1;
        long entryCount = 0;
        var entryOffsets = new long[runCount];
        for (int r = 0; r < runCount; r++)
        {
            entryOffsets[r] = entryCount;
            if (affineFlags[r]) continue;
            int last = r + 1 < runCount ? firstIndices[r + 1] : mappings.Length;
            entryCount += last - firstIndices[r];
        }
        long length = 16 + 8L * segCount + 2 * entryCount;
        if (segCount > MaxFormat4Segments)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Count, 6), out diagnostic, (uint)segCount, MaxFormat4Segments);
        if (length > 0xFFFF)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Length, 2), out diagnostic, (ulong)length, 0xFFFF);
        var glyphs = new ushort[entryCount];
        segments = new CmapFormat4Segment[segCount];
        long glyphArrayStart = 16 + 8L * segCount;
        for (int r = 0; r < runCount; r++)
        {
            int first = firstIndices[r];
            int last = r + 1 < runCount ? firstIndices[r + 1] : mappings.Length;
            uint start = starts[r];
            uint end = start + (uint)(last - first - 1);
            if (affineFlags[r])
            {
                segments[r] = new((ushort)start, (ushort)end,
                    unchecked((short)(ushort)(mappings[first].GlyphId - start)), 0);
            }
            else
            {
                long entryPosition = glyphArrayStart + 2 * entryOffsets[r];
                long fieldPosition = 16 + 6L * segCount + 2 * r;
                segments[r] = new((ushort)start, (ushort)end, 0,
                    checked((ushort)(entryPosition - fieldPosition)));
                for (int i = first; i < last; i++)
                    glyphs[entryOffsets[r] + (i - first)] = mappings[i].GlyphId;
            }
        }
        segments[runCount] = new(0xFFFF, 0xFFFF, 1, 0);
        glyphArray = glyphs;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus EncodeFormat4(ushort language, CmapMapping[] mappings,
        DataLocation location, BuildBudget budget, out byte[]? output, out FontDiagnostic diagnostic)
    {
        output = null;
        ReadStatus status = BuildFormat4Canonical(mappings, location,
            out var segments, out var glyphArray, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = segments.Length;
        int length = 16 + 8 * count + 2 * glyphArray.Length;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, 4);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)length);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), language);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)(count * 2));
        int power = 1, selector = 0;
        while (power <= count / 2) { power *= 2; selector++; }
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), (ushort)(power * 2));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), (ushort)selector);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), (ushort)(count * 2 - power * 2));
        for (int r = 0; r < count; r++)
        {
            var segment = segments[r];
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14 + 2 * r), segment.EndCode);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16 + 2 * count + 2 * r), segment.StartCode);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16 + 4 * count + 2 * r),
                unchecked((ushort)segment.IdDelta));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16 + 6 * count + 2 * r),
                segment.IdRangeOffset);
        }
        for (int g = 0; g < glyphArray.Length; g++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16 + 8 * count + 2 * g), glyphArray[g]);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus EncodeFormat12(CmapMapping[] mappings, DataLocation location,
        BuildBudget budget, out byte[]? output, out FontDiagnostic diagnostic)
    {
        output = null;
        var runs = BuildRuns(mappings);
        long length = 16 + 12L * runs.Count;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), (uint)runs.Count);
        for (int r = 0; r < runs.Count; r++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16 + 12 * r), runs[r].Start);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20 + 12 * r), runs[r].End);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(24 + 12 * r), runs[r].Glyph);
        }
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void WriteShell(CmapTable model, byte[]?[] payloads, Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt16BigEndian(bytes, 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(2), (ushort)model.Records.Length);
        long at = CmapLayout.HeaderLength + 8L * model.Records.Length;
        var offsets = new long[payloads.Length];
        for (int j = 0; j < payloads.Length; j++)
        {
            offsets[j] = at;
            at += payloads[j]!.Length;
        }
        for (int i = 0; i < model.Records.Length; i++)
        {
            var record = model.Records[i];
            BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(4 + 8 * i, 2), record.PlatformId);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(6 + 8 * i, 2), record.EncodingId);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(8 + 8 * i, 4),
                checked((uint)offsets[record.SubtableIndex]));
        }
        for (int j = 0; j < payloads.Length; j++)
            payloads[j]!.CopyTo(bytes.Slice((int)offsets[j], payloads[j]!.Length));
    }
}
