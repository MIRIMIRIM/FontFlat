using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The whole-table read/validate face of cvar 1.0 (T5-03). The version
/// gate and the three header scalars are declared; this partial owns the tuple
/// variation store walk through the shared reader. axisCount and cvtValueCount
/// are explicit inputs (the T5-02 axis authority and the CvtView.ValueCount
/// CVT-index authority, card 3) - a cvar without fvar or cvt has no authority
/// value to pass, so the fvar-less/cvt-less forms are unreachable by contract
/// (OQ-6). Unlike gvar there is no context gap: the all-CVTs count comes from
/// the explicit input, so validation decodes every delta run.</summary>
public readonly ref partial struct CvarView
{
    public ReadStatus ValidateTable(int fvarAxisCount, int cvtValueCount,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(fvarAxisCount, cvtValueCount, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int fvarAxisCount, int cvtValueCount,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (fvarAxisCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 0), out diagnostic, (ulong)fvarAxisCount, 0);
        if (cvtValueCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 0), out diagnostic, (ulong)cvtValueCount, 0);
        // The store window is the whole table with the [tvc][dataOffset] pair at
        // offset 4 (after the packed version); dataOffset values are
        // table-relative, exactly the walk's coordinate system.
        return TupleVariationStoreReader.ReadStore(_data, 4, _data.Length, 0,
            isCvar: true, axisCount: fvarAxisCount, sharedTupleCount: 0,
            allPointsDeltaCount: cvtValueCount, cvtValueCount: cvtValueCount,
            materialize: false, tuplesOut: null, location: Location, budget: budget,
            tvcRaw: out _, sharedPointNumbers: out _, serializedEnd: out _,
            diagnostic: out diagnostic);
    }

    /// <summary>Reads the whole store into an owned <see cref="CvarTable"/> with
    /// every tuple materialized (the bounded whole-table strategy, card 2).</summary>
    public ReadStatus TryReadTable(int fvarAxisCount, int cvtValueCount, out CvarTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(fvarAxisCount, cvtValueCount, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int fvarAxisCount, int cvtValueCount, out CvarTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(fvarAxisCount, cvtValueCount, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var tuples = new List<TupleVariation>();
        status = TupleVariationStoreReader.ReadStore(_data, 4, _data.Length,
            0, isCvar: true, axisCount: fvarAxisCount, sharedTupleCount: 0,
            allPointsDeltaCount: cvtValueCount, cvtValueCount: cvtValueCount,
            materialize: true, tuplesOut: tuples, location: Location, budget: budget,
            tvcRaw: out ushort tvcRaw, sharedPointNumbers: out int[]? shared,
            serializedEnd: out int serializedEnd, diagnostic: out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new CvarTable(fvarAxisCount, cvtValueCount, tvcRaw, shared, tuples, Location,
            Math.Max(CvarLayout.HeaderLength, serializedEnd), _data.ToArray());
        diagnostic = default;
        return ReadStatus.Done;
    }
}
