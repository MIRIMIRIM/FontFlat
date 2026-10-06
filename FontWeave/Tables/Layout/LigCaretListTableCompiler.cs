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
        int length = LigCaretListLayout.HeaderLength + LigCaretListLayout.OffsetStride * model.LigGlyphs.Count;
        var seen = new List<LigGlyphTable>();
        for (int i = 0; i < model.LigGlyphs.Count; i++)
        {
            var lig = model.LigGlyphs[i];
            bool shared = false;
            foreach (var candidate in seen)
            {
                if (SameEntries(candidate, lig)) { shared = true; break; }
            }
            if (!shared)
            {
                seen.Add(lig);
                length += MeasureLig(lig);
            }
        }
        return length + CoverageTableCompiler.Measure(model.Coverage);
    }

    internal static int MeasureLig(LigGlyphTable lig)
    {
        int length = LigGlyphLayout.HeaderLength + LigGlyphLayout.CaretStride * lig.Count;
        var seen = new List<CaretValueEntry>();
        for (int i = 0; i < lig.Count; i++)
        {
            var entry = lig[i];
            bool shared = false;
            foreach (var candidate in seen)
            {
                if (SameEntry(candidate, entry)) { shared = true; break; }
            }
            if (!shared)
            {
                seen.Add(entry);
                // A format-3 caret body is six bytes (format, coordinate, DeviceOffset);
                // formats 1/2 are four. The Device follows its caret directly.
                length += entry.Device is not null
                    ? CaretValueLayout.Format3Length
                    : CaretValueLayout.Format12Length;
                if (entry.Device is not null) length += StaticDeviceTableCompiler.Measure(entry.Device);
            }
        }
        return length;
    }

    // Only immutable, validated models reach this path; the GDEF root preflighted the
    // destination and every offset before calling in.
    internal static void WritePlanned(LigCaretListTable model, Span<byte> destination)
    {
        var location = model.Location;
        int coverageAt = LigCaretListLayout.HeaderLength + LigCaretListLayout.OffsetStride * model.LigGlyphs.Count;
        var placed = new List<(LigGlyphTable Lig, int At)>();
        int cursor = coverageAt;
        var offsets = new int[model.LigGlyphs.Count];
        for (int i = 0; i < model.LigGlyphs.Count; i++)
        {
            var lig = model.LigGlyphs[i];
            int at = -1;
            foreach (var (candidate, position) in placed)
            {
                if (SameEntries(candidate, lig)) { at = position; break; }
            }
            if (at < 0)
            {
                at = cursor;
                cursor += MeasureLig(lig);
                placed.Add((lig, at));
            }
            offsets[i] = at;
        }
        CoverageTableCompiler.RequireWritten(LigCaretListView.TryWriteCoverageOffset(destination,
            (ushort)cursor, out _, location));
        CoverageTableCompiler.RequireWritten(LigCaretListView.TryWriteLigGlyphCount(destination,
            (ushort)model.LigGlyphs.Count, out _, location));
        for (int i = 0; i < model.LigGlyphs.Count; i++)
            CoverageTableCompiler.RequireWritten(LigCaretListView.TryWriteLigGlyphOffsetRecordAt(destination,
                LigCaretListLayout.HeaderLength + (ulong)i * LigCaretListLayout.OffsetStride,
                new LigCaretListView.LigGlyphOffsetRecordData(i, (ushort)offsets[i]), out _, location));
        foreach (var (lig, at) in placed)
            WriteLig(lig, destination.Slice(at), location);
        CoverageTableCompiler.WritePlanned(model.Coverage, destination.Slice(cursor));
    }

    private static void WriteLig(LigGlyphTable lig, Span<byte> destination, DataLocation location)
    {
        CoverageTableCompiler.RequireWritten(LigGlyphView.TryWriteCaretCount(destination,
            (ushort)lig.Count, out _, location));
        var placed = new List<(CaretValueEntry Entry, int At)>();
        int cursor = LigGlyphLayout.HeaderLength + LigGlyphLayout.CaretStride * lig.Count;
        var offsets = new int[lig.Count];
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
        for (int i = 0; i < lig.Count; i++)
            CoverageTableCompiler.RequireWritten(LigGlyphView.TryWriteCaretValueOffsetRecordAt(destination,
                LigGlyphLayout.HeaderLength + (ulong)i * LigGlyphLayout.CaretStride,
                new LigGlyphView.CaretValueOffsetRecordData(i, (ushort)offsets[i]), out _, location));
        foreach (var (entry, at) in placed)
            WriteEntry(entry, destination.Slice(at), location);
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
