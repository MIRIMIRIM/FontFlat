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

    /// <summary>Mapping edits for format 4/12/13 targets; must be null or empty for format 14.</summary>
    public IReadOnlyList<CmapMappingEdit>? Mappings { get; init; }

    /// <summary>Variation edits for format 14 targets; must be null or empty for format 4/12/13.</summary>
    public IReadOnlyList<CmapVariationEdit>? Variations { get; init; }
}

public sealed record CmapEditPlan
{
    public IReadOnlyList<CmapSubtableEdit> Subtables { get; init; } = [];
}

/// <summary>Canonical cmap compiler. Format 4 subtables are re-encoded as delta-only maximal
/// affine segments plus the structural sentinel; format 12 as maximal (cp+1, gid+1) merged
/// groups; format 13 as constant-gid groups with adjacent equal-gid runs merged; format 14 as
/// ascending selectors with merged default ranges and fresh per-selector tables (internal
/// table aliasing is not preserved). Subtables outside formats 4/12/13/14 are preserved
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
            else if (subtable is CmapFormat4Subtable or CmapFormat12Subtable or CmapFormat13Subtable)
            {
                if (request.Variations is { Count: > 0 })
                    return RejectMismatch(model, records[ordinal].SourceOffset, ordinal,
                        subtable.Format, out diagnostic);
                var mappings = request.Mappings?.ToArray() ?? [];
                Array.Sort(mappings, static (a, b) => a.CharacterCode.CompareTo(b.CharacterCode));
                sortedMappings[e] = mappings;
                uint maxCode = subtable is CmapFormat4Subtable ? 0xFFFEu : 0x10FFFFu;
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
            else if (subtable is CmapFormat13Subtable format13)
            {
                status = EncodeFormat13(format13.Groups.ToArray(), subtable.Location, budget,
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
        BinaryPrimitives.WriteUInt16BigEndian(bytes, 13);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), (uint)runs.Count);
        for (int r = 0; r < runs.Count; r++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16 + 12 * r), runs[r].StartCharCode);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20 + 12 * r), runs[r].EndCharCode);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(24 + 12 * r), runs[r].StartGlyphID);
        }
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
        BinaryPrimitives.WriteUInt16BigEndian(bytes, 14);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2), (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(6), (uint)canonical.Count);
        for (int i = 0; i < canonical.Count; i++)
        {
            int record = 10 + 11 * i;
            WriteUInt24(bytes.AsSpan(record), canonical[i].VarSelector);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record + 3), (uint)defaultOffsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record + 7), (uint)nonDefaultOffsets[i]);
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
                    WriteUInt24(bytes.AsSpan(atTable + 4 + 4 * r), range.StartUnicodeValue);
                    bytes[atTable + 4 + 4 * r + 3] = range.AdditionalCount;
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
                    WriteUInt24(bytes.AsSpan(atTable + 4 + 5 * r), mapping.UnicodeValue);
                    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(atTable + 4 + 5 * r + 3),
                        mapping.GlyphId);
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

    private static void WriteUInt24(Span<byte> destination, uint value)
    {
        destination[0] = (byte)((value >> 16) & 0xFF);
        destination[1] = (byte)((value >> 8) & 0xFF);
        destination[2] = (byte)(value & 0xFF);
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
