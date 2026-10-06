using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct CmapMappingEdit(uint CharacterCode, int? GlyphId);

public enum CmapVariationAction
{
    AddDefault = 0, RemoveDefault = 1, SetNonDefault = 2, RemoveNonDefault = 3
}

/// <summary>One format 14 variation edit. GlyphId is only meaningful for SetNonDefault;
/// unknown selectors are created on demand and their values follow the validation rules.</summary>
public readonly record struct CmapVariationEdit(uint VarSelector, uint BaseCharacter,
    CmapVariationAction Action, int GlyphId);

public sealed record CmapSubtableEdit
{
    public int RecordOrdinal { get; init; }

    /// <summary>Mapping edits for format 0/2/4/6/8/10/12/13 targets; must be null or empty for format 14.</summary>
    public IReadOnlyList<CmapMappingEdit>? Mappings { get; init; }

    /// <summary>Variation edits for format 14 targets; must be null or empty for the other formats.</summary>
    public IReadOnlyList<CmapVariationEdit>? Variations { get; init; }
}

public sealed record CmapEditPlan
{
    public IReadOnlyList<CmapSubtableEdit> Subtables { get; init; } = [];
}

/// <summary>Canonical cmap compiler. Format 0 subtables re-encode as the full 256-entry array;
/// format 4 as delta-only maximal affine segments plus the structural sentinel; format 6 as one
/// trimmed window with zero-filled holes; format 2 as a full subHeader-0 array plus one explicit
/// idDelta-0 window per leading byte (no sharing, merging or delta tricks); format 12 as maximal
/// (cp+1, gid+1) merged groups; format 13 as constant-gid groups with adjacent equal-gid runs
/// merged; format 8 as maximal (cp+1, gid+1) merged groups split at the 0xFFFF/0x10000 boundary
/// plus the minimal deterministic is32 bitmap (only the high 16-bit values covered by non-BMP
/// groups are set); format 10 as one minimal trimmed window with zero-filled holes; format 14 as
/// ascending selectors with merged default ranges and fresh per-selector tables (internal table
/// aliasing is not preserved). Subtables outside formats 0/2/4/6/8/10/12/13/14 are preserved
/// byte-for-byte, and records that shared a subtable keep sharing the recompiled one.</summary>
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
        var sortedMappings = new CmapMappingEdit[edit.Subtables.Count][];
        var sortedVariations = new CmapVariationEdit[edit.Subtables.Count][];
        for (int e = 0; e < edit.Subtables.Count; e++)
        {
            var request = edit.Subtables[e];
            ArgumentNullException.ThrowIfNull(request);
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
            if (subtable is CmapFormat14Subtable)
            {
                if (request.Mappings is { Count: > 0 })
                    return RejectMismatch(model, records[ordinal].SourceOffset, ordinal,
                        subtable.Format, out diagnostic);
                if (request.Variations is not { Count: > 0 }) continue;
                var variations = request.Variations.ToArray();
                Array.Sort(variations, static (a, b) => a.VarSelector != b.VarSelector
                    ? a.VarSelector.CompareTo(b.VarSelector) : a.BaseCharacter.CompareTo(b.BaseCharacter));
                for (int v = 1; v < variations.Length; v++)
                    if (variations[v].VarSelector == variations[v - 1].VarSelector &&
                        variations[v].BaseCharacter == variations[v - 1].BaseCharacter)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            model.Location.At(DataField.CharCode, variations[v].BaseCharacter, ordinal),
                            out diagnostic, variations[v].BaseCharacter,
                            variations[v - 1].BaseCharacter);
                foreach (var variation in variations)
                {
                    if (!CmapFormat14View.IsVariationSelector(variation.VarSelector))
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            model.Location.At(DataField.CharCode, variation.VarSelector, ordinal),
                            out diagnostic, variation.VarSelector, 0);
                    if (variation.BaseCharacter > 0x10FFFF ||
                        variation.BaseCharacter is >= 0xD800 and <= 0xDFFF)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            model.Location.At(DataField.CharCode, variation.BaseCharacter, ordinal),
                            out diagnostic, variation.BaseCharacter, 0x10FFFF);
                    if (variation.Action == CmapVariationAction.SetNonDefault &&
                        (variation.GlyphId < 1 || variation.GlyphId >= model.NumGlyphs))
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            model.Location.At(DataField.GlyphId, variation.BaseCharacter, ordinal),
                            out diagnostic, unchecked((ulong)(long)variation.GlyphId),
                            (uint)model.NumGlyphs);
                }
                sortedVariations[e] = variations;
            }
            else if (subtable is CmapFormat0Subtable or CmapFormat2Subtable or CmapFormat4Subtable
                or CmapFormat6Subtable or CmapFormat8Subtable or CmapFormat10Subtable
                or CmapFormat12Subtable or CmapFormat13Subtable)
            {
                if (request.Variations is { Count: > 0 })
                    return RejectMismatch(model, records[ordinal].SourceOffset, ordinal,
                        subtable.Format, out diagnostic);
                var mappings = request.Mappings?.ToArray() ?? [];
                Array.Sort(mappings, static (a, b) => a.CharacterCode.CompareTo(b.CharacterCode));
                sortedMappings[e] = mappings;
                uint maxCode = subtable switch
                {
                    CmapFormat4Subtable => 0xFFFEu,
                    CmapFormat0Subtable => 0xFFu,
                    CmapFormat2Subtable or CmapFormat6Subtable => 0xFFFFu,
                    _ => 0x10FFFFu,
                };
                // Format 0 stores one-byte glyph ids: the edit bound is the tighter of
                // the glyph count and what the wire can hold.
                uint maxGlyph = subtable is CmapFormat0Subtable
                    ? (uint)Math.Min(model.NumGlyphs, 256) : (uint)model.NumGlyphs;
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
                    if (mapping.GlyphId is { } gid && (gid < 1 || gid >= maxGlyph))
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            model.Location.At(DataField.GlyphId, mapping.CharacterCode, ordinal),
                            out diagnostic, unchecked((ulong)(long)gid), maxGlyph);
                }
            }
            else
            {
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.At(DataField.Version, records[ordinal].SourceOffset, ordinal),
                    out diagnostic, subtable?.Format ?? 0);
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
            bool editChanged;
            CmapSubtable replacement;
            switch (subtable)
            {
                case CmapFormat4Subtable format4:
                    status = CmapTable.EnumerateFormat4(format4, out var existing4, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    status = ApplyMappings(existing4!, sortedMappings[e]!, format4.Location,
                        out var merged4, out diagnostic, out editChanged);
                    if (status != ReadStatus.Done) return status;
                    if (!editChanged) continue;
                    status = BuildFormat4Canonical(merged4, format4.Location,
                        out var segments, out var glyphArray, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    replacement = new CmapFormat4Subtable(format4.Language, segments, glyphArray,
                        format4.SourceOffset, format4.Location);
                    break;
                case CmapFormat12Subtable format12:
                    status = CmapTable.EnumerateFormat12(format12, out var existing12, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    status = ApplyMappings(existing12!, sortedMappings[e]!, format12.Location,
                        out var merged12, out diagnostic, out editChanged);
                    if (status != ReadStatus.Done) return status;
                    if (!editChanged) continue;
                    var runs = BuildRuns(merged12);
                    var groups12 = new CmapFormat12Group[runs.Count];
                    for (int r = 0; r < runs.Count; r++)
                        groups12[r] = new(runs[r].Start, runs[r].End, runs[r].Glyph);
                    replacement = new CmapFormat12Subtable(groups12, format12.SourceOffset,
                        format12.Location);
                    break;
                case CmapFormat13Subtable format13:
                    if (!budget.Work(16L * format13.GroupCount, format13.Location, out diagnostic) ||
                        !budget.Buffer(16L * format13.GroupCount, format13.Location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    status = ApplyConstantGroupEdits(format13.Groups.ToArray(), sortedMappings[e] ?? [],
                        format13.Location, out var groups13, out diagnostic, out editChanged);
                    if (status != ReadStatus.Done) return status;
                    if (!editChanged) continue;
                    replacement = new CmapFormat13Subtable(groups13, format13.SourceOffset,
                        format13.Location);
                    break;
                case CmapFormat8Subtable format8:
                    status = CmapTable.EnumerateFormat8(format8, out var existing8, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    status = ApplyMappings(existing8!, sortedMappings[e]!, format8.Location,
                        out var merged8, out diagnostic, out editChanged);
                    if (status != ReadStatus.Done) return status;
                    if (!editChanged) continue;
                    status = BuildFormat8Canonical(merged8!, format8.Location,
                        out var groups8, out var is32Map8, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(CmapFormat8Layout.Is32Length + 12L * groups8.Length,
                        format8.Location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    replacement = new CmapFormat8Subtable(format8.Language, groups8, is32Map8,
                        format8.SourceOffset, format8.Location);
                    break;
                case CmapFormat10Subtable format10:
                    status = CmapTable.EnumerateFormat10(format10, out var existing10, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    status = ApplyMappings(existing10!, sortedMappings[e]!, format10.Location,
                        out var merged10, out diagnostic, out editChanged);
                    if (status != ReadStatus.Done) return status;
                    if (!editChanged) continue;
                    status = BuildFormat10Canonical(merged10!, format10.Location,
                        out uint start10, out var glyphArray10, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(2L * glyphArray10.Length, format10.Location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    replacement = new CmapFormat10Subtable(format10.Language, start10, glyphArray10,
                        format10.SourceOffset, format10.Location);
                    break;
                case CmapFormat0Subtable format0:
                    status = CmapTable.EnumerateFormat0(format0, out var existing0, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    status = ApplyMappings(existing0!, sortedMappings[e]!, format0.Location,
                        out var merged0, out diagnostic, out editChanged);
                    if (status != ReadStatus.Done) return status;
                    if (!editChanged) continue;
                    if (!budget.Buffer(256L, format0.Location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    var glyphArray0 = new byte[256];
                    foreach (var mapping in merged0!)
                        glyphArray0[(int)mapping.CharacterCode] = checked((byte)mapping.GlyphId);
                    replacement = new CmapFormat0Subtable(format0.Language, glyphArray0,
                        format0.SourceOffset, format0.Location);
                    break;
                case CmapFormat6Subtable format6:
                    status = CmapTable.EnumerateFormat6(format6, out var existing6, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    status = ApplyMappings(existing6!, sortedMappings[e]!, format6.Location,
                        out var merged6, out diagnostic, out editChanged);
                    if (status != ReadStatus.Done) return status;
                    if (!editChanged) continue;
                    status = BuildFormat6Canonical(merged6!, format6.Location,
                        out ushort firstCode6, out var glyphArray6, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(2L * glyphArray6.Length, format6.Location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    replacement = new CmapFormat6Subtable(format6.Language, firstCode6, glyphArray6,
                        format6.SourceOffset, format6.Location);
                    break;
                case CmapFormat2Subtable format2:
                    status = CmapTable.EnumerateFormat2(format2, out var existing2, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    status = ApplyMappings(existing2!, sortedMappings[e]!, format2.Location,
                        out var merged2, out diagnostic, out editChanged);
                    if (status != ReadStatus.Done) return status;
                    if (!editChanged) continue;
                    status = BuildFormat2Canonical(merged2!, format2.Location,
                        out var keys2, out var headers2, out var glyphs2, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(2L * keys2.Length + 8L * headers2.Length + 2L * glyphs2.Length,
                        format2.Location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    replacement = new CmapFormat2Subtable(format2.Language, keys2, headers2,
                        glyphs2, format2.SourceOffset, format2.Location);
                    break;
                case CmapFormat14Subtable format14:
                    long variationBytes = 16L * format14.SelectorCount;
                    foreach (var selector in format14.Selectors)
                        variationBytes += 12L * selector.DefaultRanges.Length +
                            6L * selector.NonDefaultMappings.Length;
                    if (!budget.Work(variationBytes, format14.Location, out diagnostic) ||
                        !budget.Buffer(variationBytes, format14.Location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    status = ApplyVariationEdits(format14.Selectors.ToArray(), sortedVariations[e] ?? [],
                        model.NumGlyphs, format14.Location, out var selectors, out diagnostic,
                        out editChanged);
                    if (status != ReadStatus.Done) return status;
                    if (!editChanged) continue;
                    replacement = new CmapFormat14Subtable(selectors, format14.SourceOffset,
                        format14.Location);
                    break;
                default:
                    // Pre-validation already rejected every other subtable format.
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                        model.Location.At(DataField.Version,
                            records[request.RecordOrdinal].SourceOffset, request.RecordOrdinal),
                        out diagnostic, subtable?.Format ?? 0);
            }
            replacements[targets[e]] = replacement;
            replaced[targets[e]] = true;
            changed = true;
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

    private static ReadStatus RejectMismatch(CmapTable model, uint sourceOffset, int ordinal,
        ushort format, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            model.Location.At(DataField.Version, sourceOffset, ordinal), out diagnostic, format);

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

    /// <summary>Applies sorted constant-gid group edits directly on the group structure:
    /// covering groups split at edit points, changes rewrite single code points, removals
    /// drop them, and adjacent equal-gid runs merge afterwards. No per-code enumeration.</summary>
    private static ReadStatus ApplyConstantGroupEdits(CmapFormat13Group[] groups,
        CmapMappingEdit[] edits, DataLocation location, out CmapFormat13Group[] merged,
        out FontDiagnostic diagnostic, out bool changed)
    {
        merged = [];
        changed = false;
        var result = new List<CmapFormat13Group>(groups.Length + edits.Length);
        int e = 0;
        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            while (e < edits.Length && edits[e].CharacterCode < group.StartCharCode)
            {
                var edit = edits[e++];
                if (edit.GlyphId is not { } gid)
                {
                    merged = [];
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.At(DataField.CharCode, edit.CharacterCode), out diagnostic,
                        edit.CharacterCode);
                }
                result.Add(new(edit.CharacterCode, edit.CharacterCode, (uint)gid));
                changed = true;
            }
            uint cursor = group.StartCharCode;
            while (e < edits.Length && edits[e].CharacterCode <= group.EndCharCode)
            {
                var edit = edits[e++];
                uint code = edit.CharacterCode;
                if (cursor < code)
                    result.Add(new(cursor, code - 1, group.StartGlyphID));
                if (edit.GlyphId is { } gid)
                {
                    result.Add(new(code, code, (uint)gid));
                    if (gid != group.StartGlyphID) changed = true;
                }
                else if (group.StartGlyphID == 0)
                {
                    // Codes covered by a GID-0 group are unmapped; removing one is the
                    // same "removing unmapped code" rejection as formats 4/12.
                    merged = [];
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.At(DataField.CharCode, code), out diagnostic, code);
                }
                else changed = true;
                cursor = code + 1;
            }
            if (cursor <= group.EndCharCode)
                result.Add(new(cursor, group.EndCharCode, group.StartGlyphID));
        }
        while (e < edits.Length)
        {
            var edit = edits[e++];
            if (edit.GlyphId is not { } gid)
            {
                merged = [];
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.At(DataField.CharCode, edit.CharacterCode), out diagnostic,
                    edit.CharacterCode);
            }
            result.Add(new(edit.CharacterCode, edit.CharacterCode, (uint)gid));
            changed = true;
        }
        merged = [.. MergeAdjacentConstantGroups([.. result])];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Merges adjacent constant-gid groups with equal GIDs into maximal runs.</summary>
    internal static List<CmapFormat13Group> MergeAdjacentConstantGroups(CmapFormat13Group[] groups)
    {
        var merged = new List<CmapFormat13Group>(groups.Length);
        foreach (var group in groups)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (group.StartCharCode == last.EndCharCode + 1 &&
                    group.StartGlyphID == last.StartGlyphID)
                {
                    merged[^1] = last with { EndCharCode = group.EndCharCode };
                    continue;
                }
            }
            merged.Add(group);
        }
        return merged;
    }

    /// <summary>Applies sorted variation edits sequentially: presence rules are checked against
    /// the current state, unknown selectors are created on demand, and selectors left with no
    /// ranges and no mappings are removed. Re-asserting an identical non-default mapping is a
    /// no-op that counts toward the whole-plan no-op rejection.</summary>
    private static ReadStatus ApplyVariationEdits(CmapVarSelectorRecord[] selectors,
        CmapVariationEdit[] edits, int numGlyphs, DataLocation location,
        out CmapVarSelectorRecord[] updated, out FontDiagnostic diagnostic, out bool changed)
    {
        updated = selectors;
        changed = false;
        var working = new List<CmapVarSelectorRecord>(selectors);
        foreach (var edit in edits)
        {
            int index = FindSelectorIndex(working, edit.VarSelector);
            if (index < 0)
            {
                switch (edit.Action)
                {
                    case CmapVariationAction.AddDefault:
                        working.Insert(-1 - index,
                            new(edit.VarSelector, [new CmapDefaultUvsRange(edit.BaseCharacter, 0)], []));
                        changed = true;
                        break;
                    case CmapVariationAction.SetNonDefault:
                        working.Insert(-1 - index,
                            new(edit.VarSelector, [],
                                [new CmapUvsMapping(edit.BaseCharacter, (ushort)edit.GlyphId)]));
                        changed = true;
                        break;
                    default:
                        updated = [];
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            location.At(DataField.CharCode, edit.BaseCharacter), out diagnostic,
                            edit.BaseCharacter);
                }
                continue;
            }
            var record = working[index];
            switch (edit.Action)
            {
                case CmapVariationAction.AddDefault:
                    if (ContainsDefaultRange(record.DefaultRanges, edit.BaseCharacter))
                    {
                        updated = [];
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            location.At(DataField.CharCode, edit.BaseCharacter), out diagnostic,
                            edit.BaseCharacter);
                    }
                    working[index] = record with
                    { DefaultRanges = InsertDefaultRange(record.DefaultRanges, edit.BaseCharacter) };
                    changed = true;
                    break;
                case CmapVariationAction.RemoveDefault:
                    if (!TryRemoveDefaultRange(record.DefaultRanges, edit.BaseCharacter,
                        out var defaultRanges))
                    {
                        updated = [];
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            location.At(DataField.CharCode, edit.BaseCharacter), out diagnostic,
                            edit.BaseCharacter);
                    }
                    working[index] = record with { DefaultRanges = defaultRanges };
                    changed = true;
                    break;
                case CmapVariationAction.SetNonDefault:
                    int mappingIndex = FindMappingIndex(record.NonDefaultMappings, edit.BaseCharacter);
                    if (mappingIndex >= 0 &&
                        record.NonDefaultMappings[mappingIndex].GlyphId == (ushort)edit.GlyphId)
                        break;
                    if (mappingIndex >= 0)
                    {
                        var mappings = record.NonDefaultMappings.ToArray();
                        mappings[mappingIndex] = new(edit.BaseCharacter, (ushort)edit.GlyphId);
                        working[index] = record with { NonDefaultMappings = mappings };
                    }
                    else
                    {
                        working[index] = record with
                        { NonDefaultMappings = InsertMapping(record.NonDefaultMappings,
                            new CmapUvsMapping(edit.BaseCharacter, (ushort)edit.GlyphId)) };
                    }
                    changed = true;
                    break;
                case CmapVariationAction.RemoveNonDefault:
                    int removeIndex = FindMappingIndex(record.NonDefaultMappings, edit.BaseCharacter);
                    if (removeIndex < 0)
                    {
                        updated = [];
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            location.At(DataField.CharCode, edit.BaseCharacter), out diagnostic,
                            edit.BaseCharacter);
                    }
                    working[index] = record with
                    { NonDefaultMappings = RemoveMappingAt(record.NonDefaultMappings, removeIndex) };
                    changed = true;
                    break;
            }
        }
        var final = new List<CmapVarSelectorRecord>(working.Count);
        foreach (var selector in working)
            if (selector.DefaultRanges.Length != 0 || selector.NonDefaultMappings.Length != 0)
                final.Add(selector);
        updated = [.. final];
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static int FindSelectorIndex(List<CmapVarSelectorRecord> selectors, uint varSelector)
    {
        int low = 0, high = selectors.Count - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (selectors[mid].VarSelector < varSelector) low = mid + 1;
            else if (selectors[mid].VarSelector > varSelector) high = mid - 1;
            else return mid;
        }
        return -1 - low;
    }

    private static bool ContainsDefaultRange(CmapDefaultUvsRange[] ranges, uint codePoint)
    {
        int low = 0, high = ranges.Length - 1, found = -1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (ranges[mid].StartUnicodeValue <= codePoint) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return found >= 0 &&
            codePoint <= ranges[found].StartUnicodeValue + ranges[found].AdditionalCount;
    }

    private static CmapDefaultUvsRange[] InsertDefaultRange(CmapDefaultUvsRange[] ranges, uint codePoint)
    {
        var result = new List<CmapDefaultUvsRange>(ranges.Length + 1);
        int at = 0;
        while (at < ranges.Length && ranges[at].StartUnicodeValue < codePoint)
        {
            result.Add(ranges[at]);
            at++;
        }
        result.Add(new CmapDefaultUvsRange(codePoint, 0));
        for (int i = at; i < ranges.Length; i++)
            result.Add(ranges[i]);
        return [.. result];
    }

    private static bool TryRemoveDefaultRange(CmapDefaultUvsRange[] ranges, uint codePoint,
        out CmapDefaultUvsRange[] updated)
    {
        int low = 0, high = ranges.Length - 1, found = -1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (ranges[mid].StartUnicodeValue <= codePoint) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        if (found < 0 || codePoint > ranges[found].StartUnicodeValue + ranges[found].AdditionalCount)
        {
            updated = [];
            return false;
        }
        var range = ranges[found];
        uint end = range.StartUnicodeValue + range.AdditionalCount;
        var result = new List<CmapDefaultUvsRange>(ranges.Length + 1);
        for (int i = 0; i < ranges.Length; i++)
        {
            if (i == found)
            {
                if (range.StartUnicodeValue < codePoint)
                    result.Add(new(range.StartUnicodeValue,
                        (byte)(codePoint - 1 - range.StartUnicodeValue)));
                if (codePoint < end)
                    result.Add(new(codePoint + 1, (byte)(end - codePoint - 1)));
            }
            else result.Add(ranges[i]);
        }
        updated = [.. result];
        return true;
    }

    private static int FindMappingIndex(CmapUvsMapping[] mappings, uint codePoint)
    {
        int low = 0, high = mappings.Length - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (mappings[mid].UnicodeValue < codePoint) low = mid + 1;
            else if (mappings[mid].UnicodeValue > codePoint) high = mid - 1;
            else return mid;
        }
        return -1;
    }

    private static CmapUvsMapping[] InsertMapping(CmapUvsMapping[] mappings, CmapUvsMapping mapping)
    {
        var result = new List<CmapUvsMapping>(mappings.Length + 1);
        int at = 0;
        while (at < mappings.Length && mappings[at].UnicodeValue < mapping.UnicodeValue)
        {
            result.Add(mappings[at]);
            at++;
        }
        result.Add(mapping);
        for (int i = at; i < mappings.Length; i++)
            result.Add(mappings[i]);
        return [.. result];
    }

    private static CmapUvsMapping[] RemoveMappingAt(CmapUvsMapping[] mappings, int index)
    {
        var result = new List<CmapUvsMapping>(mappings.Length - 1);
        for (int i = 0; i < mappings.Length; i++)
            if (i != index) result.Add(mappings[i]);
        return [.. result];
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
            if (subtable is CmapFormat0Subtable format0)
            {
                status = EncodeFormat0(format0, subtable.Location, budget,
                    out payloads[j], out diagnostic);
            }
            else if (subtable is CmapFormat2Subtable format2)
            {
                status = CmapTable.EnumerateFormat2(format2, out var mappings2, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = EncodeFormat2(format2.Language, mappings2!, subtable.Location,
                    budget, out payloads[j], out diagnostic);
            }
            else if (subtable is CmapFormat4Subtable format4)
            {
                status = CmapTable.EnumerateFormat4(format4, out var mappings, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = EncodeFormat4(format4.Language, mappings!, subtable.Location,
                    budget, out payloads[j], out diagnostic);
            }
            else if (subtable is CmapFormat6Subtable format6)
            {
                status = CmapTable.EnumerateFormat6(format6, out var mappings6, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = EncodeFormat6(format6.Language, mappings6!, subtable.Location,
                    budget, out payloads[j], out diagnostic);
            }
            else if (subtable is CmapFormat12Subtable format12)
            {
                status = CmapTable.EnumerateFormat12(format12, out var mappings, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = EncodeFormat12(mappings!, subtable.Location, budget,
                    out payloads[j], out diagnostic);
            }
            else if (subtable is CmapFormat13Subtable format13)
            {
                status = EncodeFormat13(format13.Groups.ToArray(), subtable.Location, budget,
                    out payloads[j], out diagnostic);
            }
            else if (subtable is CmapFormat8Subtable format8)
            {
                status = CmapTable.EnumerateFormat8(format8, out var mappings8, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = EncodeFormat8(format8.Language, mappings8!, subtable.Location, budget,
                    out payloads[j], out diagnostic);
            }
            else if (subtable is CmapFormat10Subtable format10)
            {
                status = CmapTable.EnumerateFormat10(format10, out var mappings10, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = EncodeFormat10(format10.Language, mappings10!, subtable.Location, budget,
                    out payloads[j], out diagnostic);
            }
            else if (subtable is CmapFormat14Subtable format14)
            {
                status = EncodeFormat14(format14.Selectors.ToArray(), subtable.Location, budget,
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

    /// <summary>Canonical format 0: the fixed 256-entry array exactly as materialized, with
    /// the language value preserved. The validated array is already canonical, so the writer
    /// copies it instead of re-deriving it from mappings.</summary>
    internal static ReadStatus EncodeFormat0(CmapFormat0Subtable subtable, DataLocation location,
        BuildBudget budget, out byte[]? output, out FontDiagnostic diagnostic)
    {
        output = null;
        const int length = CmapFormat0Layout.SubtableLength;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        RequireWritten(CmapFormat0View.TryWriteFormat(bytes, 0, out _, location));
        RequireWritten(CmapFormat0View.TryWriteLength(bytes, length, out _, location));
        RequireWritten(CmapFormat0View.TryWriteLanguage(bytes, subtable.Language, out _, location));
        var glyphArray = subtable.GlyphArray;
        for (int i = 0; i < CmapFormat0Layout.GlyphCount; i++)
            bytes[6 + i] = glyphArray[i];
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Canonical format 6 structure: one trimmed window from the smallest to the
    /// largest mapped code with zero-filled holes; an empty mapping list yields the legal
    /// entryCount-0 form. Shared by the compiler writer and the edited-model constructor.</summary>
    internal static ReadStatus BuildFormat6Canonical(CmapMapping[] mappings, DataLocation location,
        out ushort firstCode, out ushort[] glyphArray, out FontDiagnostic diagnostic)
    {
        firstCode = 0;
        glyphArray = [];
        if (mappings.Length == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        uint start = mappings[0].CharacterCode, end = start;
        foreach (var mapping in mappings)
        {
            if (mapping.CharacterCode < start) start = mapping.CharacterCode;
            if (mapping.CharacterCode > end) end = mapping.CharacterCode;
        }
        long entryCount = end - start + 1;
        long length = 10L + 2L * entryCount;
        if (length > 0xFFFF)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Length, 2), out diagnostic, (ulong)length, 0xFFFF);
        var array = new ushort[entryCount];
        foreach (var mapping in mappings)
            array[(int)(mapping.CharacterCode - start)] = mapping.GlyphId;
        firstCode = (ushort)start;
        glyphArray = array;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus EncodeFormat6(ushort language, CmapMapping[] mappings,
        DataLocation location, BuildBudget budget, out byte[]? output, out FontDiagnostic diagnostic)
    {
        output = null;
        ReadStatus status = BuildFormat6Canonical(mappings, location,
            out ushort firstCode, out var glyphArray, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int length = 10 + 2 * glyphArray.Length;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        RequireWritten(CmapFormat6View.TryWriteFormat(bytes, 6, out _, location));
        RequireWritten(CmapFormat6View.TryWriteLength(bytes, (ushort)length, out _, location));
        RequireWritten(CmapFormat6View.TryWriteLanguage(bytes, language, out _, location));
        RequireWritten(CmapFormat6View.TryWriteFirstCode(bytes, firstCode, out _, location));
        RequireWritten(CmapFormat6View.TryWriteEntryCount(bytes, (ushort)glyphArray.Length, out _, location));
        for (int i = 0; i < glyphArray.Length; i++)
            RequireWritten(CmapFormat6View.TryWriteGlyphEntryAt(bytes,
                CmapFormat6Layout.HeaderLength + (ulong)(uint)(2 * i),
                new CmapFormat6View.GlyphEntryData(i, glyphArray[i]), out _, location));
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Canonical format 2 structure: subHeader 0 always holds the full 256-entry
    /// single-byte array (firstCode 0, entryCount 256, idDelta 0), every mapped leading byte
    /// above 0xFF gets its own explicit idDelta-0 window [minLow, maxLow] with zero-filled
    /// holes, keys point at those windows and every other key stays 0. Windows are never
    /// shared or merged. The uint16 subtable length bounds the result.</summary>
    internal static ReadStatus BuildFormat2Canonical(CmapMapping[] mappings, DataLocation location,
        out ushort[] subHeaderKeys, out CmapFormat2SubHeader[] subHeaders, out ushort[] glyphArray,
        out FontDiagnostic diagnostic)
    {
        subHeaderKeys = new ushort[256];
        subHeaders = [];
        glyphArray = [];
        foreach (var mapping in mappings)
        {
            if (mapping.CharacterCode > 0xFFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.At(DataField.CharCode, mapping.CharacterCode), out diagnostic,
                    mapping.CharacterCode, 0xFFFF);
        }
        var leadFirst = new int[256];
        var leadLast = new int[256];
        var leadMapped = new bool[256];
        foreach (var mapping in mappings)
        {
            uint code = mapping.CharacterCode;
            if (code <= 0xFF) continue;
            int lead = (int)(code >> 8);
            int low = (int)(code & 0xFF);
            if (!leadMapped[lead])
            {
                leadMapped[lead] = true;
                leadFirst[lead] = low;
                leadLast[lead] = low;
            }
            else
            {
                if (low < leadFirst[lead]) leadFirst[lead] = low;
                if (low > leadLast[lead]) leadLast[lead] = low;
            }
        }
        int subHeaderCount = 1;
        long windowEntries = 256;
        for (int b = 1; b < 256; b++)
        {
            if (!leadMapped[b]) continue;
            subHeaderCount++;
            windowEntries += leadLast[b] - leadFirst[b] + 1;
        }
        long length = 518L + 8L * subHeaderCount + 2L * windowEntries;
        if (length > 0xFFFF)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Length, 2), out diagnostic, (ulong)length, 0xFFFF);
        glyphArray = new ushort[windowEntries];
        subHeaders = new CmapFormat2SubHeader[subHeaderCount];
        long glyphArrayStart = 518L + 8L * subHeaderCount;
        subHeaders[0] = new(0, 256, 0, checked((ushort)(glyphArrayStart - 524L)));
        var leadEntryBase = new long[256];
        long at = 256;
        int index = 1;
        for (int b = 1; b < 256; b++)
        {
            if (!leadMapped[b]) continue;
            int first = leadFirst[b], last = leadLast[b];
            long windowStart = glyphArrayStart + 2L * at;
            long fieldAddress = 518L + 8L * index + 6L;
            subHeaders[index] = new((ushort)first, (ushort)(last - first + 1), 0,
                checked((ushort)(windowStart - fieldAddress)));
            leadEntryBase[b] = at;
            subHeaderKeys[b] = (ushort)(8 * index);
            at += last - first + 1;
            index++;
        }
        foreach (var mapping in mappings)
        {
            uint code = mapping.CharacterCode;
            if (code <= 0xFF)
                glyphArray[code] = mapping.GlyphId;
            else
                glyphArray[leadEntryBase[code >> 8] + (int)(code & 0xFF) - leadFirst[code >> 8]] =
                    mapping.GlyphId;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus EncodeFormat2(ushort language, CmapMapping[] mappings,
        DataLocation location, BuildBudget budget, out byte[]? output, out FontDiagnostic diagnostic)
    {
        output = null;
        ReadStatus status = BuildFormat2Canonical(mappings, location,
            out var subHeaderKeys, out var subHeaders, out var glyphArray, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int length = 518 + 8 * subHeaders.Length + 2 * glyphArray.Length;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        RequireWritten(CmapFormat2View.TryWriteFormat(bytes, 2, out _, location));
        RequireWritten(CmapFormat2View.TryWriteLength(bytes, (ushort)length, out _, location));
        RequireWritten(CmapFormat2View.TryWriteLanguage(bytes, language, out _, location));
        for (int i = 0; i < 256; i++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6 + 2 * i), subHeaderKeys[i]);
        for (int k = 0; k < subHeaders.Length; k++)
        {
            var header = subHeaders[k];
            RequireWritten(CmapFormat2View.TryWriteSubHeaderAt(bytes,
                CmapFormat2Layout.KeysEnd + (ulong)(uint)(8 * k),
                new CmapFormat2View.SubHeaderData(k, header.FirstCode, header.EntryCount,
                    header.IdDelta, header.IdRangeOffset),
                out _, location));
        }
        int glyphStart = 518 + 8 * subHeaders.Length;
        for (int i = 0; i < glyphArray.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(glyphStart + 2 * i), glyphArray[i]);
        output = bytes;
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
        RequireWritten(CmapFormat4View.TryWriteFormat(bytes, 4, out _, location));
        RequireWritten(CmapFormat4View.TryWriteLength(bytes, (ushort)length, out _, location));
        RequireWritten(CmapFormat4View.TryWriteLanguage(bytes, language, out _, location));
        RequireWritten(CmapFormat4View.TryWriteSegCountX2(bytes, (ushort)(count * 2), out _, location));
        int power = 1, selector = 0;
        while (power <= count / 2) { power *= 2; selector++; }
        RequireWritten(CmapFormat4View.TryWriteSearchRange(bytes, (ushort)(power * 2), out _, location));
        RequireWritten(CmapFormat4View.TryWriteEntrySelector(bytes, (ushort)selector, out _, location));
        RequireWritten(CmapFormat4View.TryWriteRangeShift(bytes, (ushort)(count * 2 - power * 2), out _, location));
        for (int r = 0; r < count; r++)
        {
            var segment = segments[r];
            RequireWritten(CmapFormat4View.TryWriteEndCodeAt(bytes,
                (ulong)(14 + 2 * r), new CmapFormat4View.EndCodeData(r, segment.EndCode), out _, location));
            RequireWritten(CmapFormat4View.TryWriteStartCodeAt(bytes,
                (ulong)(16 + 2 * count + 2 * r),
                new CmapFormat4View.StartCodeData(r, segment.StartCode), out _, location));
            RequireWritten(CmapFormat4View.TryWriteIdDeltaAt(bytes,
                (ulong)(16 + 4 * count + 2 * r),
                new CmapFormat4View.IdDeltaData(r, segment.IdDelta), out _, location));
            RequireWritten(CmapFormat4View.TryWriteIdRangeOffsetAt(bytes,
                (ulong)(16 + 6 * count + 2 * r),
                new CmapFormat4View.IdRangeOffsetData(r, segment.IdRangeOffset), out _, location));
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
        RequireWritten(CmapFormat12View.TryWriteFormat(bytes, 12, out _, location));
        RequireWritten(CmapFormat12View.TryWriteLength(bytes, (uint)length, out _, location));
        RequireWritten(CmapFormat12View.TryWriteNumGroups(bytes, (uint)runs.Count, out _, location));
        for (int r = 0; r < runs.Count; r++)
        {
            RequireWritten(CmapFormat12View.TryWriteGroupAt(bytes,
                CmapFormat12Layout.HeaderLength + (ulong)(uint)(12 * r),
                new CmapFormat12View.GroupData(r, runs[r].Start, runs[r].End, runs[r].Glyph),
                out _, location));
        }
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Canonical format 13: the constant-gid groups with adjacent equal-gid runs
    /// merged; reserved, language and GID 0 groups are emitted exactly as validated. The
    /// canonical writer does not accept non-scalar coverage: groups intersecting the
    /// surrogate block are rejected here even though the read side interprets them honestly.</summary>
    internal static ReadStatus EncodeFormat13(CmapFormat13Group[] groups, DataLocation location,
        BuildBudget budget, out byte[]? output, out FontDiagnostic diagnostic)
    {
        output = null;
        foreach (var group in groups)
        {
            if (group.StartCharCode <= 0xDFFF && group.EndCharCode >= 0xD800)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.At(DataField.CharCode, group.StartCharCode), out diagnostic,
                    group.EndCharCode, group.StartCharCode);
        }
        var runs = MergeAdjacentConstantGroups(groups);
        long length = 16 + 12L * runs.Count;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        RequireWritten(CmapFormat13View.TryWriteFormat(bytes, 13, out _, location));
        RequireWritten(CmapFormat13View.TryWriteLength(bytes, (uint)length, out _, location));
        RequireWritten(CmapFormat13View.TryWriteNumGroups(bytes, (uint)runs.Count, out _, location));
        for (int r = 0; r < runs.Count; r++)
        {
            RequireWritten(CmapFormat13View.TryWriteGroupAt(bytes,
                CmapFormat13Layout.HeaderLength + (ulong)(uint)(12 * r),
                new CmapFormat13View.GroupData(r, runs[r].StartCharCode, runs[r].EndCharCode,
                    runs[r].StartGlyphID),
                out _, location));
        }
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Canonical format 8 structure: maximal (cp+1, gid+1) merged groups as in format
    /// 12, split at the 0xFFFF/0x10000 boundary so no group spans the 16/32-bit domains, plus
    /// the minimal deterministic is32 bitmap: only the high 16-bit values actually covered by
    /// non-BMP groups are set and proactive input bits are not preserved. The canonical output
    /// therefore revalidates against the format 8 rules. Shared by the compiler writer and the
    /// edited-model constructor so both agree.</summary>
    internal static ReadStatus BuildFormat8Canonical(CmapMapping[] mappings, DataLocation location,
        out CmapFormat8Group[] groups, out byte[] is32, out FontDiagnostic diagnostic)
    {
        groups = [];
        is32 = [];
        foreach (var mapping in mappings)
        {
            if (mapping.CharacterCode > 0x10FFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.At(DataField.CharCode, mapping.CharacterCode), out diagnostic,
                    mapping.CharacterCode, 0x10FFFF);
        }
        var runs = BuildRuns(mappings);
        var split = new List<Run>(runs.Count);
        foreach (var run in runs)
        {
            if (run.Start <= 0xFFFF && run.End > 0xFFFF)
            {
                split.Add(new(run.Start, 0xFFFF, run.Glyph));
                split.Add(new(0x10000, run.End, run.Glyph + (0x10000 - run.Start)));
            }
            else split.Add(run);
        }
        groups = new CmapFormat8Group[split.Count];
        is32 = new byte[CmapFormat8Layout.Is32Length];
        for (int r = 0; r < split.Count; r++)
        {
            var run = split[r];
            groups[r] = new(run.Start, run.End, run.Glyph);
            if (run.Start > 0xFFFF)
            {
                for (uint high = run.Start >> 16; ; high++)
                {
                    is32[(int)(high >> 3)] |= (byte)(1 << (7 - (int)(high & 7)));
                    if (high == run.End >> 16) break;
                }
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Canonical format 8 wire form: validated header with the preserved language, the
    /// rebuilt minimal is32 bitmap and the merged groups; an empty mapping list yields the legal
    /// numGroups-0 form.</summary>
    internal static ReadStatus EncodeFormat8(uint language, CmapMapping[] mappings,
        DataLocation location, BuildBudget budget, out byte[]? output, out FontDiagnostic diagnostic)
    {
        output = null;
        ReadStatus status = BuildFormat8Canonical(mappings, location,
            out var groups, out var is32, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long length = CmapFormat8Layout.HeaderLength + 12L * groups.Length;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        RequireWritten(CmapFormat8View.TryWriteFormat(bytes, 8, out _, location));
        RequireWritten(CmapFormat8View.TryWriteLength(bytes, (uint)length, out _, location));
        RequireWritten(CmapFormat8View.TryWriteLanguage(bytes, language, out _, location));
        is32.CopyTo(bytes.AsSpan(CmapFormat8Layout.Is32Offset));
        RequireWritten(CmapFormat8View.TryWriteNumGroups(bytes, (uint)groups.Length, out _, location));
        for (int r = 0; r < groups.Length; r++)
        {
            RequireWritten(CmapFormat8View.TryWriteGroupAt(bytes,
                CmapFormat8Layout.HeaderLength + (ulong)(uint)(12 * r),
                new CmapFormat8View.GroupData(r, groups[r].StartCharCode, groups[r].EndCharCode,
                    groups[r].StartGlyphID),
                out _, location));
        }
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Canonical format 10 structure: one minimal window from the smallest to the
    /// largest mapped code with zero-filled holes, or the legal empty (numChars-0) form. A
    /// mapping set spanning the surrogate block has no valid minimal window and is rejected,
    /// mirroring the read-side window rule.</summary>
    internal static ReadStatus BuildFormat10Canonical(CmapMapping[] mappings, DataLocation location,
        out uint startCharCode, out ushort[] glyphArray, out FontDiagnostic diagnostic)
    {
        startCharCode = 0;
        glyphArray = [];
        foreach (var mapping in mappings)
        {
            if (mapping.CharacterCode > 0x10FFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.At(DataField.CharCode, mapping.CharacterCode), out diagnostic,
                    mapping.CharacterCode, 0x10FFFF);
        }
        if (mappings.Length == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        uint start = mappings[0].CharacterCode, end = start;
        foreach (var mapping in mappings)
        {
            if (mapping.CharacterCode < start) start = mapping.CharacterCode;
            if (mapping.CharacterCode > end) end = mapping.CharacterCode;
        }
        if (start <= 0xDFFF && end >= 0xD800)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                location.At(DataField.CharCode, start), out diagnostic, end, 0xD800);
        var array = new ushort[end - start + 1];
        foreach (var mapping in mappings)
            array[(int)(mapping.CharacterCode - start)] = mapping.GlyphId;
        startCharCode = start;
        glyphArray = array;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Canonical format 10 wire form: validated header with the preserved language and
    /// the minimal window; an empty mapping list yields the legal numChars-0 form.</summary>
    internal static ReadStatus EncodeFormat10(uint language, CmapMapping[] mappings,
        DataLocation location, BuildBudget budget, out byte[]? output, out FontDiagnostic diagnostic)
    {
        output = null;
        ReadStatus status = BuildFormat10Canonical(mappings, location,
            out uint start, out var glyphArray, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int length = CmapFormat10Layout.HeaderLength + 2 * glyphArray.Length;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        RequireWritten(CmapFormat10View.TryWriteFormat(bytes, 10, out _, location));
        RequireWritten(CmapFormat10View.TryWriteLength(bytes, (uint)length, out _, location));
        RequireWritten(CmapFormat10View.TryWriteLanguage(bytes, language, out _, location));
        RequireWritten(CmapFormat10View.TryWriteStartCharCode(bytes, start, out _, location));
        RequireWritten(CmapFormat10View.TryWriteNumChars(bytes, (uint)glyphArray.Length, out _, location));
        for (int i = 0; i < glyphArray.Length; i++)
            RequireWritten(CmapFormat10View.TryWriteGlyphEntryAt(bytes,
                CmapFormat10Layout.HeaderLength + (ulong)(uint)(2 * i),
                new CmapFormat10View.GlyphEntryData(i, glyphArray[i]), out _, location));
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Canonical format 14: ascending selectors without empty ones, default ranges
    /// merged into maximal runs, and after the record array every non-empty default and
    /// non-default table laid out in selector order. Input internal table aliasing is not
    /// preserved; each selector emits its own tables.</summary>
    internal static ReadStatus EncodeFormat14(CmapVarSelectorRecord[] selectors,
        DataLocation location, BuildBudget budget, out byte[]? output, out FontDiagnostic diagnostic)
    {
        output = null;
        var canonical = new List<CmapVarSelectorRecord>(selectors.Length);
        foreach (var selector in selectors)
        {
            if (selector.DefaultRanges.Length == 0 && selector.NonDefaultMappings.Length == 0)
                continue;
            canonical.Add(selector with
            { DefaultRanges = MergeAdjacentDefaultRanges(selector.DefaultRanges) });
        }
        long at = 10 + 11L * canonical.Count;
        var defaultOffsets = new long[canonical.Count];
        var nonDefaultOffsets = new long[canonical.Count];
        for (int i = 0; i < canonical.Count; i++)
        {
            int rangeCount = canonical[i].DefaultRanges.Length;
            if (rangeCount > 0) { defaultOffsets[i] = at; at += 4 + 4L * rangeCount; }
            int mappingCount = canonical[i].NonDefaultMappings.Length;
            if (mappingCount > 0) { nonDefaultOffsets[i] = at; at += 4 + 5L * mappingCount; }
        }
        long length = at;
        if (!budget.Work(length + 24L * canonical.Count, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        RequireWritten(CmapFormat14View.TryWriteFormat(bytes, 14, out _, location));
        RequireWritten(CmapFormat14View.TryWriteLength(bytes, (uint)length, out _, location));
        RequireWritten(CmapFormat14View.TryWriteNumVarSelectorRecords(bytes,
            (uint)canonical.Count, out _, location));
        for (int i = 0; i < canonical.Count; i++)
        {
            RequireWritten(CmapFormat14View.TryWriteVarSelectorAt(bytes,
                CmapFormat14Layout.HeaderLength + (ulong)(uint)(11 * i),
                new CmapFormat14View.VarSelectorData(i, canonical[i].VarSelector,
                    (uint)defaultOffsets[i], (uint)nonDefaultOffsets[i]),
                out _, location));
        }
        for (int i = 0; i < canonical.Count; i++)
        {
            var selector = canonical[i];
            if (defaultOffsets[i] != 0)
            {
                int atTable = (int)defaultOffsets[i];
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(atTable),
                    (uint)selector.DefaultRanges.Length);
                for (int r = 0; r < selector.DefaultRanges.Length; r++)
                {
                    var range = selector.DefaultRanges[r];
                    RequireWritten(CmapFormat14View.TryWriteDefaultRangeAt(bytes,
                        (ulong)(atTable + 4 + 4 * r),
                        new CmapFormat14View.DefaultRangeData(r, range.StartUnicodeValue,
                            range.AdditionalCount),
                        out _, location));
                }
            }
            if (nonDefaultOffsets[i] != 0)
            {
                int atTable = (int)nonDefaultOffsets[i];
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(atTable),
                    (uint)selector.NonDefaultMappings.Length);
                for (int r = 0; r < selector.NonDefaultMappings.Length; r++)
                {
                    var mapping = selector.NonDefaultMappings[r];
                    RequireWritten(CmapFormat14View.TryWriteUvsMappingAt(bytes,
                        (ulong)(atTable + 4 + 5 * r),
                        new CmapFormat14View.UvsMappingData(r, mapping.UnicodeValue, mapping.GlyphId),
                        out _, location));
                }
            }
        }
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Merges adjacent default ranges into maximal ones, bounded by the additionalCount
    /// limit of 239 that the format's validation enforces.</summary>
    internal static CmapDefaultUvsRange[] MergeAdjacentDefaultRanges(CmapDefaultUvsRange[] ranges)
    {
        var merged = new List<CmapDefaultUvsRange>(ranges.Length);
        foreach (var range in ranges)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                uint lastEnd = last.StartUnicodeValue + last.AdditionalCount;
                uint combined = range.StartUnicodeValue + range.AdditionalCount - last.StartUnicodeValue;
                if (range.StartUnicodeValue == lastEnd + 1 && combined <= 239)
                {
                    merged[^1] = last with { AdditionalCount = (byte)combined };
                    continue;
                }
            }
            merged.Add(range);
        }
        return [.. merged];
    }

    // Same contract as the Vorg/Hmtx emission paths: a preflighted write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted cmap write failed.");
    }

    private static void WriteShell(CmapTable model, byte[]?[] payloads, Span<byte> bytes)
    {
        RequireWritten(CmapView.TryWriteVersion(bytes, 0, out _, model.Location));
        RequireWritten(CmapView.TryWriteNumTables(bytes, (ushort)model.Records.Length, out _, model.Location));
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
            RequireWritten(CmapView.TryWriteEncodingRecordAt(bytes,
                CmapLayout.HeaderLength + (ulong)(uint)(8 * i),
                new CmapView.EncodingRecordData(i, record.PlatformId, record.EncodingId,
                    checked((uint)offsets[record.SubtableIndex])),
                out _, model.Location));
        }
        for (int j = 0; j < payloads.Length; j++)
            payloads[j]!.CopyTo(bytes.Slice((int)offsets[j], payloads[j]!.Length));
    }
}
