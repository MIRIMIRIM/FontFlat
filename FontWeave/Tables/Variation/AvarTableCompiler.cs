using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The avar root writer (HvarTableCompiler shape): one planned, atomically
/// published whole-table emission over the fixed canonical layout - header(8), then
/// every axis's segment map contiguous in axis order (positionMapCount plus that
/// many 4-byte F2DOT14 pairs). The owned model carries the raw pairs exactly as
/// read, so a re-emission re-sends tolerance-domain input byte-for-byte instead of
/// normalizing it (card 1.2-I: same-byte re-sends of defined-behavior input are not
/// rewrites); the write-side shape invariant lives in the edit API, whose target map
/// must come out from-strictly-increasing and to-non-decreasing (card 4). All counts
/// and the budget admission complete before any byte is written.</summary>
public static class AvarTableCompiler
{
    /// <summary>Explicit root-model factory: every map is caller-supplied and never
    /// inferred; observations are recomputed from the pairs. Structural domain only -
    /// tolerance-domain shapes stay legal inputs to the faithful serializer (the
    /// strict from/to shape is the edit API's invariant, not the factory's).</summary>
    public static ReadStatus TryCreate(IReadOnlyList<AvarMapPair[]> maps, out AvarTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(maps);
        var budget = new BuildBudget(limits);
        // The header axis count is a UInt16 scalar; anything past the wire domain is
        // the plan's refusal, never a silent truncation at emission.
        if (maps.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, AvarLayout.AxisCountAt),
                out diagnostic, (ulong)maps.Count, (ulong)ushort.MaxValue);
        if (!budget.Work(maps.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        foreach (AvarMapPair[] map in maps)
        {
            if (map.Length > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, AvarLayout.AxisCountAt),
                    out diagnostic, (ulong)map.Length, (ulong)ushort.MaxValue);
            if (!budget.Work(map.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
            // The wire cell is Int16 (F2DOT14 raw); an out-of-domain factory input
            // refuses instead of wrapping silently at emission.
            foreach (AvarMapPair pair in map)
            {
                if (pair.From is < short.MinValue or > short.MaxValue ||
                    pair.To is < short.MinValue or > short.MaxValue)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Payload, AvarLayout.AxisCountAt),
                        out diagnostic, unchecked((ulong)(long)pair.From), (ulong)short.MaxValue);
            }
        }
        var owned = new (AvarMapPair[] Pairs, AvarMapObservation Observation)[maps.Count];
        long length = AvarLayout.HeaderLength;
        for (int i = 0; i < maps.Count; i++)
        {
            owned[i] = (maps[i], Observe(maps[i]));
            length += AvarLayout.SegmentMapFirstPairAt + (long)maps[i].Length * AvarLayout.SegmentPairStride;
        }
        model = new(owned, location, checked((int)length));
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Shared with the read-side observation pass (the corpus probe caught the first
    // draft comparing integral -1/0/+1 against raw F2DOT14 values).
    internal static AvarMapObservation Observe(AvarMapPair[] pairs)
    {
        bool requiredMissing = !ContainsFrom(pairs, -16384) || !ContainsFrom(pairs, 0) ||
            !ContainsFrom(pairs, 16384);
        int ignored = pairs.Length - AvarTable.KeepConforming(pairs).Length;
        return new(requiredMissing, ignored);
    }

    private static bool ContainsFrom(AvarMapPair[] pairs, int from)
    {
        foreach (AvarMapPair pair in pairs)
            if (pair.From == from) return true;
        return false;
    }

    internal static int Measure(IReadOnlyList<(AvarMapPair[] Pairs, AvarMapObservation Observation)> maps)
    {
        long length = AvarLayout.HeaderLength;
        foreach (var map in maps)
            length += AvarLayout.SegmentMapFirstPairAt + (long)map.Pairs.Length * AvarLayout.SegmentPairStride;
        return checked((int)length);
    }

    public static ReadStatus TryCompile(AvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(AvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        byte[] bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(AvarTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus TryPlan(AvarTable model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        long rows = 0;
        foreach (var map in model.Maps) rows += map.Pairs.Length;
        length = Measure(model.Maps);
        if (!budget.Work(model.Maps.Count + rows + length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void WritePlanned(AvarTable model, Span<byte> destination)
    {
        var location = model.Location;
        RequireWritten(AvarView.TryWriteVersion(destination, 0x00010000, out _, location));
        RequireWritten(AvarView.TryWriteReserved(destination, 0, out _, location));
        RequireWritten(AvarView.TryWriteAxisCount(destination, (ushort)model.Maps.Count,
            out _, location));
        int at = AvarLayout.HeaderLength;
        foreach (var map in model.Maps)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(at),
                (ushort)map.Pairs.Length);
            at += AvarLayout.SegmentMapFirstPairAt;
            foreach (AvarMapPair pair in map.Pairs)
            {
                BinaryPrimitives.WriteInt16BigEndian(destination.Slice(at), (short)pair.From);
                BinaryPrimitives.WriteInt16BigEndian(destination.Slice(at + 2), (short)pair.To);
                at += AvarLayout.SegmentPairStride;
            }
        }
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted avar write failed.");
    }
}
