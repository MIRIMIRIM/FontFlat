using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The whole-table read/validate face of STAT 1.0/1.1/1.2 (T5-02). The
/// version gate, the two counted arrays and the axis value openers are declared;
/// this partial owns the header domains (designAxisSize==8, the two-way existence
/// relation "If designAxisCount is zero, set to zero; if greater than zero, must be
/// greater than zero", spec STAT.md), the design-axes placement, the DuplicateTag
/// scan (OQ-T5-02-3's lean hard reading of "No two axis records should have the same
/// axisTag value"), the name-ID wire domains and the axis value walk with the
/// version-aware format 4 gate (1.2 adds support - a format 4 table under 1.0/1.1
/// refuses InvalidValue at the value's format field).
///
/// fvar is an explicit nullable input, never an implicit dependency (card 1.2-III):
/// null is the static-font first class (AnekBangla's real STAT), and a non-null
/// context enforces the cross-table contract - designAxisCount must be at least the
/// fvar axis count and every fvar axis needs a design-axes record with the same tag
/// (order is arbitrary, so matching is by tag, not position; both directions are
/// spec "must" text and every real witness conforms). The "same name ID" sentence
/// in the same spec paragraph is the card's registered deviation (OQ-T5-02-8): the
/// frozen corpus itself refutes the hard reading - RobotoFlex (all 13 axes),
/// Amstelvar-avar2.A (3 axes) and sixtyfour (4 axes) carry different STAT/fvar name
/// IDs while being positive deep-read witnesses, so a hard refusal would reject a
/// production font. The agreement therefore lands on the observation face
/// (StatTable.NameIdMismatches) exactly like the avar tolerance domain. No name-table
/// existence is consulted anywhere (card 1.2-II).</summary>
public readonly ref partial struct StatView
{
    public ReadStatus ValidateTable(FvarTable? fvar, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(fvar, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(FvarTable? fvar, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDesignAxisSize(out ushort designAxisSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (designAxisSize != StatLayout.DesignAxisSize)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, StatLayout.DesignAxisSizeAt),
                out diagnostic, designAxisSize, StatLayout.DesignAxisSize);
        status = ValidateExistence(out ushort designAxisCount, out ushort axisValueCount,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ValidateDesignAxes(designAxisCount, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ValidateElided(version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ValidateFvarRelations(fvar, designAxisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ValidateAxisValues(version, designAxisCount, axisValueCount, budget, out diagnostic);
    }

    // The two-way existence domain: a count and its array offset are either both
    // present or both absent (spec STAT.md pins both directions).
    private ReadStatus ValidateExistence(out ushort designAxisCount, out ushort axisValueCount,
        out FontDiagnostic diagnostic)
    {
        designAxisCount = 0;
        axisValueCount = 0;
        ReadStatus status = TryGetDesignAxisCount(out designAxisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxisValueCount(out axisValueCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDesignAxesOffset(out uint designAxesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetOffsetToAxisValueOffsets(out uint valueOffsets, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((designAxisCount > 0) != (designAxesOffset > 0))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, StatLayout.DesignAxesOffsetAt),
                out diagnostic, designAxesOffset, designAxisCount);
        if ((axisValueCount > 0) != (valueOffsets > 0))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, StatLayout.OffsetToAxisValueOffsetsAt),
                out diagnostic, valueOffsets, axisValueCount);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The design-axes array: the placement may not start inside the version-branched
    // header, the native extent refuses out-of-window, then one pass carries the
    // DuplicateTag scan and the axis name ID domain.
    private ReadStatus ValidateDesignAxes(ushort designAxisCount, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        if (designAxisCount == 0) return Done(out diagnostic);
        ReadStatus status = TryGetDesignAxesOffset(out uint designAxesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetHeaderEnd(out ulong headerEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (designAxesOffset < headerEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, StatLayout.DesignAxesOffsetAt),
                out diagnostic, designAxesOffset, headerEnd);
        if (!budget.Work(designAxisCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryRequireDesignAxisExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        var seen = new HashSet<uint>();
        for (int ordinal = 0; ordinal < designAxisCount; ordinal++)
        {
            status = TryGetDesignAxis(ordinal, out var axis, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!seen.Add(axis.AxisTag))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                    Location.AtLocal(DataField.Tag,
                        (ulong)(designAxesOffset + ordinal * StatLayout.DesignAxisStride), ordinal),
                    out diagnostic, axis.AxisTag, 1);
            if (NameIDDomain.StatAxisRefused(axis.AxisNameID))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload,
                        (ulong)(designAxesOffset + ordinal * StatLayout.DesignAxisStride + 4), ordinal),
                    out diagnostic, axis.AxisNameID, 256);
        }
        return Done(out diagnostic);
    }

    // The elided fallback name ID exists only under 1.1/1.2 (the conditional record
    // owns the physical existence domain); its wire domain is the lean wide form.
    private ReadStatus ValidateElided(ulong version, out FontDiagnostic diagnostic)
    {
        if (version < StatLayout.Version11) return Done(out diagnostic);
        ReadStatus status = TryReadElidedFallbackNameAt(StatLayout.ElidedFallbackNameIDAt, -1,
            out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (NameIDDomain.StatAxisRefused(record.NameID))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, StatLayout.ElidedFallbackNameIDAt),
                out diagnostic, record.NameID, 256);
        return Done(out diagnostic);
    }

    // The explicit fvar contract, hard part: dac >= fvar axisCount (InvalidValue)
    // and a same-tag record per fvar axis (InvalidIndex when missing) - both spec
    // "must" text with zero real violations (frozen facts). The name-ID agreement is
    // observed, not enforced (OQ-T5-02-8; see the type header for the corpus
    // evidence). The static path (fvar null) skips the whole domain.
    private ReadStatus ValidateFvarRelations(FvarTable? fvar, ushort designAxisCount,
        out FontDiagnostic diagnostic)
    {
        if (fvar is null) return Done(out diagnostic);
        if (designAxisCount < fvar.AxisCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, StatLayout.DesignAxisCountAt),
                out diagnostic, designAxisCount, (ulong)fvar.AxisCount);
        ReadStatus status = TryGetDesignAxesOffset(out uint designAxesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var tags = new HashSet<uint>(designAxisCount);
        for (int ordinal = 0; ordinal < designAxisCount; ordinal++)
        {
            status = TryGetDesignAxis(ordinal, out var axis, out diagnostic);
            if (status != ReadStatus.Done) return status;
            tags.Add(axis.AxisTag);
        }
        foreach (FvarAxis fvarAxis in fvar.Axes)
        {
            if (!tags.Contains(fvarAxis.Tag))
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.Tag, (ulong)designAxesOffset),
                    out diagnostic, fvarAxis.Tag, (ulong)designAxisCount);
        }
        return Done(out diagnostic);
    }

    // The observation face of the fvar contract: the name-ID mismatches per shared
    // axis, in fvar axis order (OQ-T5-02-8). Empty for static fonts and fully
    // agreeing variable fonts.
    private ReadStatus ObserveFvarNameIds(FvarTable? fvar,
        out StatNameIdMismatch[] mismatches, out FontDiagnostic diagnostic)
    {
        mismatches = Array.Empty<StatNameIdMismatch>();
        if (fvar is null || fvar.AxisCount == 0) return Done(out diagnostic);
        ReadStatus status = TryGetDesignAxisCount(out ushort designAxisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (designAxisCount == 0) return Done(out diagnostic);
        var statNameIds = new Dictionary<uint, ushort>(designAxisCount);
        for (int ordinal = 0; ordinal < designAxisCount; ordinal++)
        {
            status = TryGetDesignAxis(ordinal, out var axis, out diagnostic);
            if (status != ReadStatus.Done) return status;
            statNameIds[axis.AxisTag] = axis.AxisNameID;
        }
        var collected = new List<StatNameIdMismatch>();
        foreach (FvarAxis fvarAxis in fvar.Axes)
        {
            if (statNameIds.TryGetValue(fvarAxis.Tag, out ushort statNameID) &&
                statNameID != fvarAxis.AxisNameID)
                collected.Add(new(fvarAxis.Tag, statNameID, fvarAxis.AxisNameID));
        }
        mismatches = collected.ToArray();
        return Done(out diagnostic);
    }

    // The axis value walk: the declared opener owns the offsets-array-relative
    // addressing, the zero policy and the floor; this loop adds the STAT-version
    // gate for format 4 (1.2 adds support) and the value's own semantic pass with
    // the design-axis-count context.
    private ReadStatus ValidateAxisValues(ulong version, ushort designAxisCount,
        ushort axisValueCount, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (axisValueCount == 0) return Done(out diagnostic);
        if (!budget.Work(axisValueCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int ordinal = 0; ordinal < axisValueCount; ordinal++)
        {
            ReadStatus status = TryOpenAxisValue(ordinal, out var valueView, out bool present,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!present)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset,
                        (ulong)(StatLayout.OffsetToAxisValueOffsetsAt + 2 * ordinal), ordinal),
                    out diagnostic, 0ul, (ulong)_data.Length);
            // The format field is public; the version hook is the child's private
            // face, so the STAT-level gate reads the format scalar directly.
            status = valueView.TryGetFormat(out ushort format, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (format == 4 && version < StatLayout.Version12)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    valueView.Location.AtLocal(DataField.Version, 0),
                    out diagnostic, format, (ulong)StatLayout.Version12);
            status = valueView.Validate(designAxisCount, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        return Done(out diagnostic);
    }

    /// <summary>Reads the complete table into an owned <see cref="StatTable"/>: the
    /// design axes, every axis value in offsets-array order (fully materialized - the
    /// witness bound is 46 values, frozen facts R8) and the elided fallback name ID
    /// under 1.1/1.2 (card 3.2).</summary>
    public ReadStatus TryReadTable(FvarTable? fvar, out StatTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(fvar, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(FvarTable? fvar, out StatTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(fvar, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDesignAxisCount(out ushort designAxisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxisValueCount(out ushort axisValueCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDesignAxesOffset(out uint designAxesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetOffsetToAxisValueOffsets(out uint valueOffsets, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var axes = new StatDesignAxis[designAxisCount];
        for (int ordinal = 0; ordinal < designAxisCount; ordinal++)
        {
            status = TryGetDesignAxis(ordinal, out var axis, out diagnostic);
            if (status != ReadStatus.Done) return status;
            axes[ordinal] = new(axis.AxisTag, axis.AxisNameID, axis.AxisOrdering);
        }
        var values = new StatAxisValue[axisValueCount];
        int consumed = version >= StatLayout.Version11 ? StatLayout.Header11 : StatLayout.Header10;
        consumed = Math.Max(consumed, checked((int)designAxesOffset + designAxisCount * StatLayout.DesignAxisStride));
        consumed = Math.Max(consumed, checked((int)valueOffsets + axisValueCount * StatLayout.AxisValueOffsetStride));
        for (int ordinal = 0; ordinal < axisValueCount; ordinal++)
        {
            status = TryOpenAxisValue(ordinal, out var valueView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = valueView.TryRead(out var value, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            values[ordinal] = value!;
            status = TryGetAxisValueOffsets(ordinal, out var offsetEntry, out diagnostic);
            if (status != ReadStatus.Done) return status;
            consumed = Math.Max(consumed,
                checked((int)(valueOffsets + offsetEntry.Value) + value!.WireLength));
        }
        ushort? elided = null;
        if (version >= StatLayout.Version11)
        {
            status = TryReadElidedFallbackNameAt(StatLayout.ElidedFallbackNameIDAt, -1,
                out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            elided = record.NameID;
        }
        status = ObserveFvarNameIds(fvar, out var mismatches, out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new((uint)version, axes, values, elided, mismatches, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }
}
