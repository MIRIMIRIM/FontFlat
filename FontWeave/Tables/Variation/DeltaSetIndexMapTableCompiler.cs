using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>Compile-side model admission and emission for the HVAR face of the
/// DeltaSetIndexMap (format 0). Entries re-pack with the model's own entryFormat -
/// the source representation is preserved exactly (card 4: no entryFormat widening,
/// no re-pack optimization; the OQ-T5-01-5 finite refusal fires when an edited pair
/// leaves the entryFormat's representable domain).</summary>
public static class DeltaSetIndexMapTableCompiler
{
    public static ReadStatus TryCreate(byte entryFormat, IReadOnlyList<DeltaSetIndex> entries,
        out DeltaSetIndexMapTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(entryFormat, entries, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(byte entryFormat, IReadOnlyList<DeltaSetIndex> entries,
        out DeltaSetIndexMapTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(entries);
        if ((entryFormat & DeltaSetIndexMapView.ReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, DeltaSetIndexMapLayout.EntryFormatAt),
                out diagnostic, (ulong)(entryFormat & DeltaSetIndexMapView.ReservedMask), 0);
        // A non-NULL map always carries at least one entry (OQ-T5-01-3): a map with
        // zero entries has no last item for the fallback to resolve to.
        if (entries.Count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, DeltaSetIndexMapLayout.MapCountAt),
                out diagnostic, 0, 1);
        if (entries.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, DeltaSetIndexMapLayout.MapCountAt),
                out diagnostic, (ulong)entries.Count, ushort.MaxValue);
        if (!budget.Work(entries.Count, location, out diagnostic) ||
            !budget.Buffer(8L * entries.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        foreach (var entry in entries)
        {
            ReadStatus status = CheckRepresentable(entryFormat, entry, location, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        model = new(0, entryFormat, entries.ToArray(), location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Replaces one logical entry; the pair must stay inside the entryFormat's
    /// representable domain (card 4 edit API, OQ-T5-01-5).</summary>
    public static ReadStatus TryWithMapping(DeltaSetIndexMapTable model, int entryIndex,
        DeltaSetIndex entry, out DeltaSetIndexMapTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        ArgumentNullException.ThrowIfNull(model);
        if (entryIndex < 0 || entryIndex >= model.MapCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.AtLocal(DataField.Count, DeltaSetIndexMapLayout.MapCountAt),
                out diagnostic, (ulong)(long)entryIndex, (ulong)model.MapCount);
        ReadStatus status = CheckRepresentable(model.EntryFormat, entry, model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var entries = new DeltaSetIndex[model.MapCount];
        for (int i = 0; i < model.MapCount; i++)
        {
            _ = model.TryGetMapping(i, out entries[i]);
        }
        entries[entryIndex] = entry;
        return TryCreate(model.EntryFormat, entries, out updated, out diagnostic,
            new BuildBudget(limits), model.Location);
    }

    // The spec's logical domains: both indices are 16-bit values, the inner one is
    // additionally capped by the entryFormat's inner-bit count, and the packed pair
    // must fit the entry size (nothing is ever silently truncated).
    private static ReadStatus CheckRepresentable(byte entryFormat, DeltaSetIndex entry,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        int entrySize = DeltaSetIndexMapView.PackedEntrySize(entryFormat);
        int innerBits = DeltaSetIndexMapView.InnerBitCount(entryFormat);
        if (entry.Inner > (ushort)innerMask(innerBits))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, DeltaSetIndexMapLayout.EntryFormatAt),
                out diagnostic, entry.Inner, innerMask(innerBits));
        ulong packed = ((ulong)entry.Outer << innerBits) | entry.Inner;
        ulong limit = entrySize >= 8 ? ulong.MaxValue : (1ul << (entrySize * 8)) - 1;
        if (packed > limit)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Flags, DeltaSetIndexMapLayout.EntryFormatAt),
                out diagnostic, packed, limit);
        diagnostic = default;
        return ReadStatus.Done;

        static uint innerMask(int bits) => bits >= 32 ? uint.MaxValue : (1u << bits) - 1;
    }

    internal static int Measure(DeltaSetIndexMapTable model) =>
        DeltaSetIndexMapLayout.EntriesAt +
        DeltaSetIndexMapView.PackedEntrySize(model.EntryFormat) * model.MapCount;

    /// <summary>Root-writer emission for one map body at the slice origin (format 0;
    /// T6-03 §4 widens the emission to the model's own format word).</summary>
    internal static void WritePlanned(DeltaSetIndexMapTable model, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(DeltaSetIndexMapView.TryWriteFormat(destination, 0,
            out _, location));
        CoverageTableCompiler.RequireWritten(DeltaSetIndexMapView.TryWriteEntryFormat(destination,
            model.EntryFormat, out _, location));
        global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
            destination.Slice(DeltaSetIndexMapLayout.MapCountAt, 2), (ushort)model.MapCount);
        int entrySize = DeltaSetIndexMapView.PackedEntrySize(model.EntryFormat);
        int innerBits = DeltaSetIndexMapView.InnerBitCount(model.EntryFormat);
        int at = DeltaSetIndexMapLayout.EntriesAt;
        for (int i = 0; i < model.MapCount; i++)
        {
            _ = model.TryGetMapping(i, out var entry);
            ulong packed = ((ulong)entry.Outer << innerBits) | entry.Inner;
            for (int b = entrySize - 1; b >= 0; b--)
            {
                destination[at + b] = (byte)(packed & 0xFF);
                packed >>= 8;
            }
            at += entrySize;
        }
    }
}
