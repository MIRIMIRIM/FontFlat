using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Compile-side planning for owned LigCaretLists. Identical LigGlyph entries
/// share one body by content identity (edited ligs split naturally), and inside a body
/// identical CaretValues share one sub-block; format-3 Devices sit directly after their
/// caret, exactly as the frozen synthetic layouts pin.</summary>
public static class LigCaretListTableCompiler
{
    public static ReadStatus TryWithCarets(LigCaretListTable model, int ligIndex,
        ReadOnlySpan<CaretValueEntry> carets, out LigCaretListTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        updated = null;
        if (ligIndex < 0 || ligIndex >= model.LigGlyphs.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.AtLocal(DataField.Count, LigCaretListLayout.LigGlyphCountAt, ligIndex),
                out diagnostic, unchecked((ulong)(long)ligIndex), (ulong)model.LigGlyphs.Count);
        var budget = new BuildBudget(limits);
        if (!budget.Work(carets.Length + 1L, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int previous = int.MinValue;
        for (int i = 0; i < carets.Length; i++)
        {
            var entry = carets[i];
            if (entry.Format is not (1 or 2 or 3))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    model.Location.AtLocal(DataField.Version, 0, i), out diagnostic, entry.Format);
            if (entry.Device is not null && (entry.Device.Location.SourceId != model.Location.SourceId ||
                    entry.Device.Location.FaceIndex != model.Location.FaceIndex ||
                    model.Location.SourceId != 0 && entry.Device.Location.TableTag != model.Location.TableTag))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    model.Location.AtLocal(DataField.None, 0, i), out diagnostic);
            if (entry.Coordinate is short coordinate)
            {
                if (previous != int.MinValue && coordinate <= previous)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        model.Location.AtLocal(DataField.Offset,
                            LigGlyphLayout.HeaderLength + (ulong)i * LigGlyphLayout.CaretStride, i),
                        out diagnostic, unchecked((ulong)(long)coordinate), unchecked((ulong)(long)previous));
                previous = coordinate;
            }
        }
        var replaced = new LigGlyphTable[model.LigGlyphs.Count];
        for (int i = 0; i < model.LigGlyphs.Count; i++)
        {
            if (i != ligIndex) { replaced[i] = model.LigGlyphs[i]; continue; }
            var entries = new CaretValueEntry[carets.Length];
            for (int j = 0; j < carets.Length; j++) entries[j] = carets[j];
            replaced[i] = new LigGlyphTable(entries, model.Location, 0);
        }
        updated = new(model.Coverage, replaced, model.NumGlyphs, model.Location, model.ConsumedLength, []);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static int Measure(LigCaretListTable model)
    {
        Place(model, out int coverageAt, out _);
        return coverageAt + CoverageTableCompiler.Measure(model.Coverage);
    }

    internal static int MeasureLig(LigGlyphTable lig) => PlaceLig(lig, out _);

    /// <summary>One LigCaretList's placement: the Coverage start (after every LigGlyph
    /// body) and the deduplicated LigGlyph bodies with their positions, in emission
    /// order. Shared by the size, the preflight and the writer, so the three cannot
    /// disagree.</summary>
    private static void Place(LigCaretListTable model, out int coverageAt,
        out List<(LigGlyphTable Lig, int At)> placed)
    {
        int cursor = LigCaretListLayout.HeaderLength +
            LigCaretListLayout.OffsetStride * model.LigGlyphs.Count;
        placed = [];
        for (int i = 0; i < model.LigGlyphs.Count; i++)
        {
            var lig = model.LigGlyphs[i];
            bool shared = false;
            foreach (var (candidate, _) in placed)
            {
                if (SameEntries(candidate, lig)) { shared = true; break; }
            }
            if (shared) continue;
            placed.Add((lig, cursor));
            cursor += MeasureLig(lig);
        }
        coverageAt = cursor;
    }

    /// <summary>One LigGlyph's size and its caret-record targets, from one walk. A
    /// format-3 caret body is six bytes (format, coordinate, DeviceOffset); formats 1/2
    /// are four, and the Device follows its caret directly.</summary>
    private static int PlaceLig(LigGlyphTable lig, out int[] offsets)
    {
        int cursor = LigGlyphLayout.HeaderLength + LigGlyphLayout.CaretStride * lig.Count;
        var placed = new List<(CaretValueEntry Entry, int At)>();
        offsets = new int[lig.Count];
        for (int i = 0; i < lig.Count; i++)
        {
            var entry = lig[i];
            int at = -1;
            foreach (var (candidate, position) in placed)
            {
                if (SameEntry(candidate, entry)) { at = position; break; }
            }
            if (at < 0)
            {
                at = cursor;
                cursor += (entry.Device is null
                    ? CaretValueLayout.Format12Length
                    : CaretValueLayout.Format3Length +
                        StaticDeviceTableCompiler.Measure(entry.Device));
                placed.Add((entry, at));
            }
            offsets[i] = at;
        }
        return cursor;
    }

    /// <summary>Plan-time admission of every Offset16 word this child owns: the
    /// Coverage target, each LigGlyph record and each CaretValue record, plus the two
    /// count words. The writer's narrowing casts then cannot throw in a checked build
    /// or truncate in an unchecked one, so an over-wide list refuses as the finite
    /// too-narrow face instead of publishing a bad table.</summary>
    internal static ReadStatus TryPlan(LigCaretListTable model, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        if (!budget.Work(2L * model.LigGlyphs.Count + 1, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (model.LigGlyphs.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Count, LigCaretListLayout.LigGlyphCountAt),
                out diagnostic, (ulong)model.LigGlyphs.Count, (ulong)ushort.MaxValue);
        Place(model, out int coverageAt, out var placed);
        ReadStatus status = CheckTarget(model.Location, coverageAt,
            LigCaretListLayout.CoverageOffsetAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // One record slot per glyph in coverage order; the target is the shared body's
        // position, matched by content identity exactly like the placement.
        for (int i = 0; i < model.LigGlyphs.Count; i++)
        {
            foreach (var (candidate, at) in placed)
            {
                if (!SameEntries(candidate, model.LigGlyphs[i])) continue;
                status = CheckTarget(model.Location, at, LigCaretListLayout.HeaderLength +
                    (ulong)i * LigCaretListLayout.OffsetStride, out diagnostic);
                if (status != ReadStatus.Done) return status;
                break;
            }
        }
        foreach (var (lig, _) in placed)
        {
            if (lig.Count > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    lig.Location.AtLocal(DataField.Count, LigGlyphLayout.CountAt),
                    out diagnostic, (ulong)lig.Count, (ulong)ushort.MaxValue);
            PlaceLig(lig, out var caretOffsets);
            for (int c = 0; c < caretOffsets.Length; c++)
            {
                status = CheckTarget(lig.Location, caretOffsets[c],
                    LigGlyphLayout.HeaderLength + (ulong)c * LigGlyphLayout.CaretStride,
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus CheckTarget(DataLocation location, int target, ulong fieldAt,
        out FontDiagnostic diagnostic)
    {
        if (target <= ushort.MaxValue)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            location.AtLocal(DataField.Offset, fieldAt), out diagnostic, (ulong)target,
            (ulong)ushort.MaxValue);
    }

    // Only immutable, preflighted models reach this path (GdefTableCompiler.TryPlan runs
    // TryPlan for every child before any byte exists).
    internal static void WritePlanned(LigCaretListTable model, Span<byte> destination)
    {
        var location = model.Location;
        Place(model, out int coverageAt, out var placed);
        // The record slot answers per glyph by content identity, exactly like the
        // placement: two distinct-but-equal LigGlyph models share one body.
        var offsets = new int[model.LigGlyphs.Count];
        for (int i = 0; i < model.LigGlyphs.Count; i++)
        {
            foreach (var (candidate, at) in placed)
            {
                if (SameEntries(candidate, model.LigGlyphs[i])) { offsets[i] = at; break; }
            }
        }
        CoverageTableCompiler.RequireWritten(LigCaretListView.TryWriteCoverageOffset(destination,
            (ushort)coverageAt, out _, location));
        CoverageTableCompiler.RequireWritten(LigCaretListView.TryWriteLigGlyphCount(destination,
            (ushort)model.LigGlyphs.Count, out _, location));
        for (int i = 0; i < model.LigGlyphs.Count; i++)
            CoverageTableCompiler.RequireWritten(LigCaretListView.TryWriteLigGlyphOffsetRecordAt(destination,
                LigCaretListLayout.HeaderLength + (ulong)i * LigCaretListLayout.OffsetStride,
                new LigCaretListView.LigGlyphOffsetRecordData(i, (ushort)offsets[i]), out _, location));
        foreach (var (lig, at) in placed)
            WriteLig(lig, destination.Slice(at), location);
        CoverageTableCompiler.WritePlanned(model.Coverage, destination.Slice(coverageAt));
    }

    private static void WriteLig(LigGlyphTable lig, Span<byte> destination, DataLocation location)
    {
        CoverageTableCompiler.RequireWritten(LigGlyphView.TryWriteCaretCount(destination,
            (ushort)lig.Count, out _, location));
        PlaceLig(lig, out var offsets);
        for (int i = 0; i < lig.Count; i++)
            CoverageTableCompiler.RequireWritten(LigGlyphView.TryWriteCaretValueOffsetRecordAt(destination,
                LigGlyphLayout.HeaderLength + (ulong)i * LigGlyphLayout.CaretStride,
                new LigGlyphView.CaretValueOffsetRecordData(i, (ushort)offsets[i]), out _, location));
        // The bodies sit behind the record array in first-use order; PlaceLig already
        // decided both the targets and that order.
        var written = new List<CaretValueEntry>();
        int cursor = LigGlyphLayout.HeaderLength + LigGlyphLayout.CaretStride * lig.Count;
        for (int i = 0; i < lig.Count; i++)
        {
            var entry = lig[i];
            bool shared = false;
            foreach (var candidate in written)
            {
                if (SameEntry(candidate, entry)) { shared = true; break; }
            }
            if (shared) continue;
            written.Add(entry);
            WriteEntry(entry, destination.Slice(cursor), location);
            cursor += entry.Device is null
                ? CaretValueLayout.Format12Length
                : CaretValueLayout.Format3Length + StaticDeviceTableCompiler.Measure(entry.Device);
        }
    }

    private static void WriteEntry(CaretValueEntry entry, Span<byte> destination, DataLocation location)
    {
        CoverageTableCompiler.RequireWritten(CaretValueView.TryWriteFormat(destination, entry.Format,
            out _, location));
        switch (entry.Format)
        {
            case 1:
                CoverageTableCompiler.RequireWritten(CaretValueView.TryWriteFormat1HeaderAt(destination,
                    CaretValueLayout.BodyAt, new CaretValueView.Format1HeaderData(-1, entry.Coordinate!.Value),
                    out _, location));
                break;
            case 2:
                CoverageTableCompiler.RequireWritten(CaretValueView.TryWriteFormat2HeaderAt(destination,
                    CaretValueLayout.BodyAt,
                    new CaretValueView.Format2HeaderData(-1, entry.PointIndex!.Value), out _, location));
                break;
            default:
                CoverageTableCompiler.RequireWritten(CaretValueView.TryWriteFormat3HeaderAt(destination,
                    CaretValueLayout.BodyAt,
                    new CaretValueView.Format3HeaderData(-1, entry.Coordinate!.Value,
                        CaretValueLayout.Format3Length), out _, location));
                StaticDeviceTableCompiler.WritePlanned(entry.Device!,
                    destination.Slice(CaretValueLayout.Format3Length,
                        StaticDeviceTableCompiler.Measure(entry.Device!)));
                break;
        }
    }

    private static bool SameEntries(LigGlyphTable left, LigGlyphTable right) =>
        left.Count == right.Count && Matches(left, right);

    private static bool Matches(LigGlyphTable left, LigGlyphTable right)
    {
        for (int i = 0; i < left.Count; i++)
        {
            if (!SameEntry(left[i], right[i])) return false;
        }
        return true;
    }

    private static bool SameEntry(CaretValueEntry left, CaretValueEntry right) =>
        left.Format == right.Format && Nullable.Equals(left.Coordinate, right.Coordinate) &&
        Nullable.Equals(left.PointIndex, right.PointIndex) &&
        (ReferenceEquals(left.Device, right.Device) || (left.Device is not null && right.Device is not null &&
            left.Device.StartSize == right.Device.StartSize && left.Device.EndSize == right.Device.EndSize &&
            left.Device.DeltaFormat == right.Device.DeltaFormat &&
            left.Device.Deltas.ToArray().AsSpan().SequenceEqual(right.Device.Deltas.ToArray())));
}
