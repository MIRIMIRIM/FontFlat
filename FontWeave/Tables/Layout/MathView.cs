using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The whole-table read/validate face of the MATH root (T4-10 section 3).
/// One strict pass whose materialize flag decides owned models, never which checks
/// run - ValidateTable and TryReadTable answer with identical diagnostics at identical
/// locations. numGlyphs is the single explicit authority input with no default (the
/// OQ-4 lean: MATH has zero lookup dereferences, so no GdefLookupContext-style
/// carrier exists; the authority bounds the three GID domains - every reused
/// coverage through the T4-01 chain, the variant glyph ids and the part glyph ids -
/// and MATH keeps no neighbor-table inputs at all, the section 3.2 contrast with
/// T4-09's cross-table index authority).
///
/// The hard faces are exactly the card's list: the native version gate, bounds and
/// count/extent truncation, the OQ-6 MUST combination contracts (topAccent, mathKern,
/// vert and horiz count == the coverage's covered-glyph total, with coverage NULL
/// implying count 0), the variant/part GID domains and the declared kernValues length
/// heightCount+1. Everything else is the OQ-5/6/7/8 observation set - correction
/// height order, the partFlags reserved ridge, zero-extender assemblies, the italics
/// "Should" coincidence and the whole empty-form matrix are recorded, never refused;
/// no diagnostic is ever emitted for an observation. Every non-zero deviceOffset
/// resolves through the existing StaticDeviceView (the T4-05/06/07 semantics, zero
/// new Device logic; MATH has no variation store of its own, so a 0x8000 device
/// keeps the activated VariationIndex shape with no store resolution). Shared blocks
/// keep model identity: a wire position materializes once per kind (the BASE R7
/// discipline; the real hosts repeat kern bodies and devices heavily). Math layout
/// application stays outside this face (the T0 Operations ban).</summary>
public readonly ref partial struct MathView
{
    /// <summary>Whole-table validate face. The observations are not reported here
    /// (no diagnostic is ever emitted for them); only the hard faces refuse.</summary>
    public ReadStatus ValidateTable(int numGlyphs, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ReadTable(numGlyphs, false, out _, out diagnostic, budget);
    }

    /// <summary>Reads the complete table into an owned <see cref="MathTable"/> with
    /// every present subtree materialized per budget. The materialize flag decides
    /// owned models, never which checks run.</summary>
    public ReadStatus TryReadTable(int numGlyphs, out MathTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(numGlyphs, out model, out diagnostic, budget);
    }

    /// <summary>The operation-budget overload: a caller that already owns a running
    /// <see cref="BuildBudget"/> (the subset pipeline) charges this read to it, so the
    /// per-operation ceiling and the final usage stay one accounting domain.</summary>
    internal ReadStatus TryReadTable(int numGlyphs, out MathTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = ReadTable(numGlyphs, true, out var read, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = read;
        return ReadStatus.Done;
    }

    // The one strict pass: version gate, the explicit authority input, then the three
    // Optional heads in wire order (constants -> glyphInfo -> variants, the real-host
    // natural order; the declared HeaderEnd floor keeps every head strictly forward,
    // so an increasing order is guaranteed by construction and no order face exists).
    private ReadStatus ReadTable(int numGlyphs, bool materialize, out MathTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(MathLayout.Header10, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var walk = new Walk(numGlyphs, materialize);
        status = TryGetConstantsOffset(out ushort constantsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetGlyphInfoOffset(out ushort glyphInfoOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetVariantsOffset(out ushort variantsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int consumed = MathLayout.Header10;
        MathConstantsTable? constants = null;
        if (constantsOffset != 0)
        {
            // Optional head: the opener owns the zero policy, the HeaderEnd floor and
            // the source bounds.
            status = TryOpenConstants(out var constantsView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadConstants(constantsView, constantsOffset, walk,
                out constants, out int constantsLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            consumed = Math.Max(consumed, constantsOffset + constantsLength);
        }
        MathGlyphInfoTable? glyphInfo = null;
        if (glyphInfoOffset != 0)
        {
            status = TryOpenGlyphInfo(out var glyphInfoView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadGlyphInfo(glyphInfoView, glyphInfoOffset, walk,
                out glyphInfo, out int glyphInfoLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            consumed = Math.Max(consumed, glyphInfoOffset + glyphInfoLength);
        }
        MathVariantsTable? variants = null;
        if (variantsOffset != 0)
        {
            status = TryOpenVariants(out var variantsView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadVariants(variantsView, variantsOffset, walk,
                out variants, out int variantsLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            consumed = Math.Max(consumed, variantsOffset + variantsLength);
        }
        if (materialize)
        {
            if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new((uint)version, numGlyphs, constantsOffset, glyphInfoOffset,
                variantsOffset, constants, glyphInfo, variants, Location, consumed);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ----------------------------------------------------------------- constants

    // The fixed 56-field body: the layout is declared (no count field), so the walk
    // reads the five scalars and the 51 value records and resolves the 51 device
    // offsets through the handwritten opener (OQ-2's lean form has no declared device
    // reference - the StaticDeviceView reuse stays the semantic layer's, the JstfMax
    // OQ-6 shape).
    private ReadStatus ReadConstants(MathConstantsView constants, int position, Walk walk,
        out MathConstantsTable? model, out int length, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        length = MathLayout.ConstantsSize;
        if (!budget.Work(length, constants.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = constants.TryGetScriptPercentScaleDown(out short scriptPercent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = constants.TryGetScriptScriptPercentScaleDown(out short scriptScriptPercent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = constants.TryGetDelimitedSubFormulaMinHeight(out ushort delimitedMin, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = constants.TryGetDisplayOperatorMinHeight(out ushort displayMin, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = constants.TryGetRadicalDegreeBottomRaisePercent(out short radicalPercent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        MathValueRecord[]? values = null;
        StaticDeviceTable?[]? devices = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(8L * MathLayout.ConstantsRecordCount, constants.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            values = new MathValueRecord[MathLayout.ConstantsRecordCount];
            devices = new StaticDeviceTable?[MathLayout.ConstantsRecordCount];
        }
        for (int i = 0; i < MathLayout.ConstantsRecordCount; i++)
        {
            int valueAt = MathLayout.ConstantsValueRecordsAt + i * MathLayout.ValueRecordSize;
            status = ReadConstantRecord(constants, i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            StaticDeviceTable? device = null;
            if (record.DeviceOffset != 0)
            {
                // The handwritten OQ-6 shape: zero is the present-null (checked above);
                // the BodyEnd floor and the source bounds are this opener's faces, the
                // declared-reference diagnostics byte for byte.
                if (record.DeviceOffset < MathLayout.ConstantsSize)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                        constants.Location.AtLocal(DataField.Offset,
                            (ulong)(valueAt + MathLayout.ValueDeviceOffsetAt), i),
                        out diagnostic, record.DeviceOffset, (ulong)MathLayout.ConstantsSize);
                if (record.DeviceOffset >= (ulong)constants.RawBytes.Length)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                        constants.Location.AtLocal(DataField.Offset,
                            (ulong)(valueAt + MathLayout.ValueDeviceOffsetAt), i),
                        out diagnostic, record.DeviceOffset, (ulong)constants.RawBytes.Length);
                // The deviceOffset is constants-body relative (the ViewStart origin):
                // the slice and the location basis are the constants view's own.
                status = StaticDeviceView.TryOpen(constants.RawBytes.Slice(record.DeviceOffset),
                    out var deviceView, out diagnostic,
                    constants.Location.AtLocal(DataField.None, record.DeviceOffset));
                if (status != ReadStatus.Done) return status;
                status = ReadDevice(deviceView, position + record.DeviceOffset, walk,
                    out device, out int deviceLength, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                length = Math.Max(length, record.DeviceOffset + deviceLength);
            }
            if (values is not null)
            {
                values[i] = record;
                devices![i] = device;
            }
        }
        if (values is not null)
        {
            if (!budget.Buffer(16, constants.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(scriptPercent, scriptScriptPercent, delimitedMin, displayMin,
                values, devices!, radicalPercent, constants.Location, length);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The 51 fixed value records in wire order: the generated per-field accessors own
    // the layout; this dispatch only pairs each record's value with its deviceOffset.
    private static ReadStatus ReadConstantRecord(MathConstantsView constants, int ordinal,
        out MathValueRecord record, out FontDiagnostic diagnostic)
    {
        ReadStatus status;
        short value;
        ushort deviceOffset;
        switch (ordinal)
        {
            case 0: status = constants.TryGetMathLeadingValue(out value, out diagnostic); break;
            case 1: status = constants.TryGetAxisHeightValue(out value, out diagnostic); break;
            case 2: status = constants.TryGetAccentBaseHeightValue(out value, out diagnostic); break;
            case 3: status = constants.TryGetFlattenedAccentBaseHeightValue(out value, out diagnostic); break;
            case 4: status = constants.TryGetSubscriptShiftDownValue(out value, out diagnostic); break;
            case 5: status = constants.TryGetSubscriptTopMaxValue(out value, out diagnostic); break;
            case 6: status = constants.TryGetSubscriptBaselineDropMinValue(out value, out diagnostic); break;
            case 7: status = constants.TryGetSuperscriptShiftUpValue(out value, out diagnostic); break;
            case 8: status = constants.TryGetSuperscriptShiftUpCrampedValue(out value, out diagnostic); break;
            case 9: status = constants.TryGetSuperscriptBottomMinValue(out value, out diagnostic); break;
            case 10: status = constants.TryGetSuperscriptBaselineDropMaxValue(out value, out diagnostic); break;
            case 11: status = constants.TryGetSubSuperscriptGapMinValue(out value, out diagnostic); break;
            case 12: status = constants.TryGetSuperscriptBottomMaxWithSubscriptValue(out value, out diagnostic); break;
            case 13: status = constants.TryGetSpaceAfterScriptValue(out value, out diagnostic); break;
            case 14: status = constants.TryGetUpperLimitGapMinValue(out value, out diagnostic); break;
            case 15: status = constants.TryGetUpperLimitBaselineRiseMinValue(out value, out diagnostic); break;
            case 16: status = constants.TryGetLowerLimitGapMinValue(out value, out diagnostic); break;
            case 17: status = constants.TryGetLowerLimitBaselineDropMinValue(out value, out diagnostic); break;
            case 18: status = constants.TryGetStackTopShiftUpValue(out value, out diagnostic); break;
            case 19: status = constants.TryGetStackTopDisplayStyleShiftUpValue(out value, out diagnostic); break;
            case 20: status = constants.TryGetStackBottomShiftDownValue(out value, out diagnostic); break;
            case 21: status = constants.TryGetStackBottomDisplayStyleShiftDownValue(out value, out diagnostic); break;
            case 22: status = constants.TryGetStackGapMinValue(out value, out diagnostic); break;
            case 23: status = constants.TryGetStackDisplayStyleGapMinValue(out value, out diagnostic); break;
            case 24: status = constants.TryGetStretchStackTopShiftUpValue(out value, out diagnostic); break;
            case 25: status = constants.TryGetStretchStackBottomShiftDownValue(out value, out diagnostic); break;
            case 26: status = constants.TryGetStretchStackGapAboveMinValue(out value, out diagnostic); break;
            case 27: status = constants.TryGetStretchStackGapBelowMinValue(out value, out diagnostic); break;
            case 28: status = constants.TryGetFractionNumeratorShiftUpValue(out value, out diagnostic); break;
            case 29: status = constants.TryGetFractionNumeratorDisplayStyleShiftUpValue(out value, out diagnostic); break;
            case 30: status = constants.TryGetFractionDenominatorShiftDownValue(out value, out diagnostic); break;
            case 31: status = constants.TryGetFractionDenominatorDisplayStyleShiftDownValue(out value, out diagnostic); break;
            case 32: status = constants.TryGetFractionNumeratorGapMinValue(out value, out diagnostic); break;
            case 33: status = constants.TryGetFractionNumDisplayStyleGapMinValue(out value, out diagnostic); break;
            case 34: status = constants.TryGetFractionRuleThicknessValue(out value, out diagnostic); break;
            case 35: status = constants.TryGetFractionDenominatorGapMinValue(out value, out diagnostic); break;
            case 36: status = constants.TryGetFractionDenomDisplayStyleGapMinValue(out value, out diagnostic); break;
            case 37: status = constants.TryGetSkewedFractionHorizontalGapValue(out value, out diagnostic); break;
            case 38: status = constants.TryGetSkewedFractionVerticalGapValue(out value, out diagnostic); break;
            case 39: status = constants.TryGetOverbarVerticalGapValue(out value, out diagnostic); break;
            case 40: status = constants.TryGetOverbarRuleThicknessValue(out value, out diagnostic); break;
            case 41: status = constants.TryGetOverbarExtraAscenderValue(out value, out diagnostic); break;
            case 42: status = constants.TryGetUnderbarVerticalGapValue(out value, out diagnostic); break;
            case 43: status = constants.TryGetUnderbarRuleThicknessValue(out value, out diagnostic); break;
            case 44: status = constants.TryGetUnderbarExtraDescenderValue(out value, out diagnostic); break;
            case 45: status = constants.TryGetRadicalVerticalGapValue(out value, out diagnostic); break;
            case 46: status = constants.TryGetRadicalDisplayStyleVerticalGapValue(out value, out diagnostic); break;
            case 47: status = constants.TryGetRadicalRuleThicknessValue(out value, out diagnostic); break;
            case 48: status = constants.TryGetRadicalExtraAscenderValue(out value, out diagnostic); break;
            case 49: status = constants.TryGetRadicalKernBeforeDegreeValue(out value, out diagnostic); break;
            default: status = constants.TryGetRadicalKernAfterDegreeValue(out value, out diagnostic); break;
        }
        if (status != ReadStatus.Done)
        {
            record = default;
            return status;
        }
        status = ordinal switch
        {
            0 => constants.TryGetMathLeadingDeviceOffset(out deviceOffset, out diagnostic),
            1 => constants.TryGetAxisHeightDeviceOffset(out deviceOffset, out diagnostic),
            2 => constants.TryGetAccentBaseHeightDeviceOffset(out deviceOffset, out diagnostic),
            3 => constants.TryGetFlattenedAccentBaseHeightDeviceOffset(out deviceOffset, out diagnostic),
            4 => constants.TryGetSubscriptShiftDownDeviceOffset(out deviceOffset, out diagnostic),
            5 => constants.TryGetSubscriptTopMaxDeviceOffset(out deviceOffset, out diagnostic),
            6 => constants.TryGetSubscriptBaselineDropMinDeviceOffset(out deviceOffset, out diagnostic),
            7 => constants.TryGetSuperscriptShiftUpDeviceOffset(out deviceOffset, out diagnostic),
            8 => constants.TryGetSuperscriptShiftUpCrampedDeviceOffset(out deviceOffset, out diagnostic),
            9 => constants.TryGetSuperscriptBottomMinDeviceOffset(out deviceOffset, out diagnostic),
            10 => constants.TryGetSuperscriptBaselineDropMaxDeviceOffset(out deviceOffset, out diagnostic),
            11 => constants.TryGetSubSuperscriptGapMinDeviceOffset(out deviceOffset, out diagnostic),
            12 => constants.TryGetSuperscriptBottomMaxWithSubscriptDeviceOffset(out deviceOffset, out diagnostic),
            13 => constants.TryGetSpaceAfterScriptDeviceOffset(out deviceOffset, out diagnostic),
            14 => constants.TryGetUpperLimitGapMinDeviceOffset(out deviceOffset, out diagnostic),
            15 => constants.TryGetUpperLimitBaselineRiseMinDeviceOffset(out deviceOffset, out diagnostic),
            16 => constants.TryGetLowerLimitGapMinDeviceOffset(out deviceOffset, out diagnostic),
            17 => constants.TryGetLowerLimitBaselineDropMinDeviceOffset(out deviceOffset, out diagnostic),
            18 => constants.TryGetStackTopShiftUpDeviceOffset(out deviceOffset, out diagnostic),
            19 => constants.TryGetStackTopDisplayStyleShiftUpDeviceOffset(out deviceOffset, out diagnostic),
            20 => constants.TryGetStackBottomShiftDownDeviceOffset(out deviceOffset, out diagnostic),
            21 => constants.TryGetStackBottomDisplayStyleShiftDownDeviceOffset(out deviceOffset, out diagnostic),
            22 => constants.TryGetStackGapMinDeviceOffset(out deviceOffset, out diagnostic),
            23 => constants.TryGetStackDisplayStyleGapMinDeviceOffset(out deviceOffset, out diagnostic),
            24 => constants.TryGetStretchStackTopShiftUpDeviceOffset(out deviceOffset, out diagnostic),
            25 => constants.TryGetStretchStackBottomShiftDownDeviceOffset(out deviceOffset, out diagnostic),
            26 => constants.TryGetStretchStackGapAboveMinDeviceOffset(out deviceOffset, out diagnostic),
            27 => constants.TryGetStretchStackGapBelowMinDeviceOffset(out deviceOffset, out diagnostic),
            28 => constants.TryGetFractionNumeratorShiftUpDeviceOffset(out deviceOffset, out diagnostic),
            29 => constants.TryGetFractionNumeratorDisplayStyleShiftUpDeviceOffset(out deviceOffset, out diagnostic),
            30 => constants.TryGetFractionDenominatorShiftDownDeviceOffset(out deviceOffset, out diagnostic),
            31 => constants.TryGetFractionDenominatorDisplayStyleShiftDownDeviceOffset(out deviceOffset, out diagnostic),
            32 => constants.TryGetFractionNumeratorGapMinDeviceOffset(out deviceOffset, out diagnostic),
            33 => constants.TryGetFractionNumDisplayStyleGapMinDeviceOffset(out deviceOffset, out diagnostic),
            34 => constants.TryGetFractionRuleThicknessDeviceOffset(out deviceOffset, out diagnostic),
            35 => constants.TryGetFractionDenominatorGapMinDeviceOffset(out deviceOffset, out diagnostic),
            36 => constants.TryGetFractionDenomDisplayStyleGapMinDeviceOffset(out deviceOffset, out diagnostic),
            37 => constants.TryGetSkewedFractionHorizontalGapDeviceOffset(out deviceOffset, out diagnostic),
            38 => constants.TryGetSkewedFractionVerticalGapDeviceOffset(out deviceOffset, out diagnostic),
            39 => constants.TryGetOverbarVerticalGapDeviceOffset(out deviceOffset, out diagnostic),
            40 => constants.TryGetOverbarRuleThicknessDeviceOffset(out deviceOffset, out diagnostic),
            41 => constants.TryGetOverbarExtraAscenderDeviceOffset(out deviceOffset, out diagnostic),
            42 => constants.TryGetUnderbarVerticalGapDeviceOffset(out deviceOffset, out diagnostic),
            43 => constants.TryGetUnderbarRuleThicknessDeviceOffset(out deviceOffset, out diagnostic),
            44 => constants.TryGetUnderbarExtraDescenderDeviceOffset(out deviceOffset, out diagnostic),
            45 => constants.TryGetRadicalVerticalGapDeviceOffset(out deviceOffset, out diagnostic),
            46 => constants.TryGetRadicalDisplayStyleVerticalGapDeviceOffset(out deviceOffset, out diagnostic),
            47 => constants.TryGetRadicalRuleThicknessDeviceOffset(out deviceOffset, out diagnostic),
            48 => constants.TryGetRadicalExtraAscenderDeviceOffset(out deviceOffset, out diagnostic),
            49 => constants.TryGetRadicalKernBeforeDegreeDeviceOffset(out deviceOffset, out diagnostic),
            _ => constants.TryGetRadicalKernAfterDegreeDeviceOffset(out deviceOffset, out diagnostic),
        };
        if (status != ReadStatus.Done)
        {
            record = default;
            return status;
        }
        record = new(value, deviceOffset);
        return ReadStatus.Done;
    }

    // ------------------------------------------------------------ glyph-info subtree

    private ReadStatus ReadGlyphInfo(MathGlyphInfoView glyphInfo, int position, Walk walk,
        out MathGlyphInfoTable? model, out int length, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        length = MathGlyphInfoLayout.HeaderLength;
        if (!budget.Work(length, glyphInfo.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int end = length;
        ReadStatus status = glyphInfo.TryGetItalicsCorrectionInfoOffset(out ushort italicsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        MathItalicsCorrectionInfoTable? italics = null;
        if (italicsOffset != 0)
        {
            status = glyphInfo.TryOpenItalicsCorrectionInfo(out var italicsView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadItalics(italicsView, position + italicsOffset, walk,
                out italics, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, italicsOffset + slotLength);
        }
        status = glyphInfo.TryGetTopAccentAttachmentOffset(out ushort topAccentOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        MathTopAccentAttachmentTable? topAccent = null;
        if (topAccentOffset != 0)
        {
            status = glyphInfo.TryOpenTopAccentAttachment(out var topAccentView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadTopAccent(topAccentView, position + topAccentOffset, walk,
                out topAccent, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, topAccentOffset + slotLength);
        }
        status = glyphInfo.TryGetExtendedShapeCoverageOffset(out ushort extendedOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        CoverageTable? extendedShapes = null;
        if (extendedOffset != 0)
        {
            status = glyphInfo.TryOpenExtendedShapeCoverage(out var coverageView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadCoverage(coverageView, position + extendedOffset, walk,
                out extendedShapes, out _, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, extendedOffset + slotLength);
        }
        status = glyphInfo.TryGetKernInfoOffset(out ushort kernInfoOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        MathKernInfoTable? kernInfo = null;
        if (kernInfoOffset != 0)
        {
            status = glyphInfo.TryOpenKernInfo(out var kernInfoView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadKernInfo(kernInfoView, position + kernInfoOffset, walk,
                out kernInfo, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, kernInfoOffset + slotLength);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, glyphInfo.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(italics, topAccent, extendedShapes, kernInfo, glyphInfo.Location, end);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The count/coverage family head shared by italics/topAccent/kernInfo: the
    // Optional coverage consumption plus the relation, whose modality is the caller's
    // (topAccent and mathKern "Must" refuse - the OQ-6 hard face, the T4-02
    // count-mismatch shape at the count field; italics "Should" is the observation
    // the model records, never a refusal).
    private ReadStatus ReadFamilyCoverage(CoverageView coverageView, int coveragePosition,
        bool coveragePresent, int count, DataLocation countLocation, bool hardRelation,
        Walk walk, out CoverageTable? coverage, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        coverage = null;
        int covered = 0;
        if (coveragePresent)
        {
            ReadStatus status = ReadCoverage(coverageView!, coveragePosition, walk,
                out coverage, out covered, out _, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (count == covered)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (!hardRelation)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            countLocation, out diagnostic, (ulong)count, (ulong)covered);
    }

    private ReadStatus ReadItalics(MathItalicsCorrectionInfoView italics, int position,
        Walk walk, out MathItalicsCorrectionInfoTable? model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        ReadStatus status = italics.TryGetCoverageOffset(out ushort coverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = italics.TryGetItalicsCorrectionCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = italics.TryRequireItalicsCorrectionExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = italics.TryGetRecordsEnd(out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, italics.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        CoverageView coverageView = default;
        int coveragePosition = 0;
        bool coveragePresent = false;
        if (coverageOffset != 0)
        {
            status = italics.TryOpenCoverage(out var coverageChild, out coveragePresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (coveragePresent)
            {
                coverageView = coverageChild;
                coveragePosition = position + coverageOffset;
            }
        }
        status = ReadFamilyCoverage(coverageView, coveragePosition, coveragePresent, count,
            italics.Location.AtLocal(DataField.Count, MathItalicsCorrectionInfoLayout.CountAt),
            hardRelation: false, walk, out var coverage, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int end = recordsEnd;
        if (coverage is not null)
            end = Math.Max(end, coverageOffset + coverage.ConsumedLength);
        MathValueRecord[]? records = null;
        StaticDeviceTable?[]? devices = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(8L * count, italics.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            records = new MathValueRecord[count];
            devices = new StaticDeviceTable?[count];
        }
        for (int i = 0; i < count; i++)
        {
            status = italics.TryGetItalicsCorrection(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            StaticDeviceTable? device = null;
            if (record.DeviceOffset != 0)
            {
                status = italics.TryOpenDevice(i, out var deviceView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ReadDevice(deviceView, position + record.DeviceOffset, walk,
                    out device, out int deviceLength, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                end = Math.Max(end, record.DeviceOffset + deviceLength);
            }
            if (records is not null)
            {
                records[i] = new(record.Value, record.DeviceOffset);
                devices![i] = device;
            }
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, italics.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(coverage, records!, devices!, italics.Location, end);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadTopAccent(MathTopAccentAttachmentView topAccent, int position,
        Walk walk, out MathTopAccentAttachmentTable? model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        ReadStatus status = topAccent.TryGetCoverageOffset(out ushort coverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = topAccent.TryGetTopAccentAttachmentCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = topAccent.TryRequireTopAccentAttachmentExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = topAccent.TryGetRecordsEnd(out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, topAccent.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        CoverageView coverageView = default;
        int coveragePosition = 0;
        bool coveragePresent = false;
        if (coverageOffset != 0)
        {
            status = topAccent.TryOpenCoverage(out var coverageChild, out coveragePresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (coveragePresent)
            {
                coverageView = coverageChild;
                coveragePosition = position + coverageOffset;
            }
        }
        status = ReadFamilyCoverage(coverageView, coveragePosition, coveragePresent, count,
            topAccent.Location.AtLocal(DataField.Count, MathTopAccentAttachmentLayout.CountAt),
            hardRelation: true, walk, out var coverage, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int end = recordsEnd;
        if (coverage is not null)
            end = Math.Max(end, coverageOffset + coverage.ConsumedLength);
        MathValueRecord[]? records = null;
        StaticDeviceTable?[]? devices = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(8L * count, topAccent.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            records = new MathValueRecord[count];
            devices = new StaticDeviceTable?[count];
        }
        for (int i = 0; i < count; i++)
        {
            status = topAccent.TryGetTopAccentAttachment(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            StaticDeviceTable? device = null;
            if (record.DeviceOffset != 0)
            {
                status = topAccent.TryOpenDevice(i, out var deviceView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ReadDevice(deviceView, position + record.DeviceOffset, walk,
                    out device, out int deviceLength, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                end = Math.Max(end, record.DeviceOffset + deviceLength);
            }
            if (records is not null)
            {
                records[i] = new(record.Value, record.DeviceOffset);
                devices![i] = device;
            }
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, topAccent.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(coverage, records!, devices!, topAccent.Location, end);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadKernInfo(MathKernInfoView kernInfo, int position, Walk walk,
        out MathKernInfoTable? model, out int length, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        length = 0;
        ReadStatus status = kernInfo.TryGetCoverageOffset(out ushort coverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = kernInfo.TryGetMathKernCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = kernInfo.TryRequireMathKernInfoRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = kernInfo.TryGetRecordsEnd(out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, kernInfo.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        CoverageView coverageView = default;
        int coveragePosition = 0;
        bool coveragePresent = false;
        if (coverageOffset != 0)
        {
            status = kernInfo.TryOpenCoverage(out var coverageChild, out coveragePresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (coveragePresent)
            {
                coverageView = coverageChild;
                coveragePosition = position + coverageOffset;
            }
        }
        status = ReadFamilyCoverage(coverageView, coveragePosition, coveragePresent, count,
            kernInfo.Location.AtLocal(DataField.Count, MathKernInfoLayout.CountAt),
            hardRelation: true, walk, out var coverage, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int end = recordsEnd;
        if (coverage is not null)
            end = Math.Max(end, coverageOffset + coverage.ConsumedLength);
        MathKernInfoRecord[]? records = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(8L * count, kernInfo.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            records = new MathKernInfoRecord[count];
        }
        for (int i = 0; i < count; i++)
        {
            status = kernInfo.TryGetMathKernInfoRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadCorner(kernInfo, i, record.TopRightMathKernOffset,
                static (view, ord, out child, out present, out diag) =>
                    view.TryOpenTopRightMathKern(ord, out child, out present, out diag),
                position, walk, out var topRight, out int topRightLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = ReadCorner(kernInfo, i, record.TopLeftMathKernOffset,
                static (view, ord, out child, out present, out diag) =>
                    view.TryOpenTopLeftMathKern(ord, out child, out present, out diag),
                position, walk, out var topLeft, out int topLeftLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = ReadCorner(kernInfo, i, record.BottomRightMathKernOffset,
                static (view, ord, out child, out present, out diag) =>
                    view.TryOpenBottomRightMathKern(ord, out child, out present, out diag),
                position, walk, out var bottomRight, out int bottomRightLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = ReadCorner(kernInfo, i, record.BottomLeftMathKernOffset,
                static (view, ord, out child, out present, out diag) =>
                    view.TryOpenBottomLeftMathKern(ord, out child, out present, out diag),
                position, walk, out var bottomLeft, out int bottomLeftLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            // The corner bodies are the kernInfo leaf's farthest blocks: each present
            // corner extends the consumed end by its own closed subtree.
            if (record.TopRightMathKernOffset != 0)
                end = Math.Max(end, record.TopRightMathKernOffset + topRightLength);
            if (record.TopLeftMathKernOffset != 0)
                end = Math.Max(end, record.TopLeftMathKernOffset + topLeftLength);
            if (record.BottomRightMathKernOffset != 0)
                end = Math.Max(end, record.BottomRightMathKernOffset + bottomRightLength);
            if (record.BottomLeftMathKernOffset != 0)
                end = Math.Max(end, record.BottomLeftMathKernOffset + bottomLeftLength);
            if (records is not null)
                records[i] = new(topRight, topLeft, bottomRight, bottomLeft);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, kernInfo.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(coverage, records!, kernInfo.Location, end);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One MathKernInfoRecord corner: the per-slot "May be NULL" is the generated
    // Optional opener's zero policy; the shared kern body keeps its model identity.
    private ReadStatus ReadCorner(MathKernInfoView kernInfo, int ordinal, ushort cornerOffset,
        CornerOpener opener, int position, Walk walk, out MathKernTable? corner,
        out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        corner = null;
        length = 0;
        if (cornerOffset == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = opener(kernInfo, ordinal, out var cornerView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ReadKern(cornerView, position + cornerOffset, walk, out corner,
            out length, out diagnostic, budget);
    }

    private delegate ReadStatus CornerOpener(MathKernInfoView view, int ordinal,
        out MathKernView child, out bool present, out FontDiagnostic diagnostic);

    // One MathKern body: the heights and the derived kernValues array (the computed
    // count owns the length; truncation is native), each row's device slot through the
    // declared Optional reference. The heights' increasing order is the OQ-5 family
    // observation - the wire order is what the model records.
    private ReadStatus ReadKern(MathKernView kern, int position, Walk walk,
        out MathKernTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        if (walk.TryTakeKern(position, out var cached))
        {
            model = cached;
            length = cached?.ConsumedLength ?? 0;
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = kern.TryGetHeightCount(out ushort heightCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = kern.TryGetKernValueCount(out ulong kernValueCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = kern.TryRequireCorrectionHeightExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = kern.TryRequireKernValueExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = kern.TryGetBodyEnd(out ulong bodyEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int bodyEnd = (int)bodyEndExtent;
        if (!budget.Work(bodyEnd, kern.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        MathValueRecord[]? heights = null;
        StaticDeviceTable?[]? heightDevices = null;
        MathValueRecord[]? kernValues = null;
        StaticDeviceTable?[]? kernValueDevices = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(8L * heightCount, kern.Location, out diagnostic) ||
                !budget.Buffer(8L * (long)kernValueCount, kern.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            heights = new MathValueRecord[heightCount];
            heightDevices = new StaticDeviceTable?[heightCount];
            kernValues = new MathValueRecord[kernValueCount];
            kernValueDevices = new StaticDeviceTable?[kernValueCount];
        }
        int end = bodyEnd;
        for (int i = 0; i < heightCount; i++)
        {
            status = kern.TryGetCorrectionHeight(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            StaticDeviceTable? device = null;
            if (record.DeviceOffset != 0)
            {
                status = kern.TryOpenCorrectionHeightDevice(i, out var deviceView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ReadDevice(deviceView, position + record.DeviceOffset, walk,
                    out device, out int deviceLength, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                end = Math.Max(end, record.DeviceOffset + deviceLength);
            }
            if (heights is not null)
            {
                heights[i] = new(record.Value, record.DeviceOffset);
                heightDevices![i] = device;
            }
        }
        for (int i = 0; i < (int)kernValueCount; i++)
        {
            status = kern.TryGetKernValue(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            StaticDeviceTable? device = null;
            if (record.DeviceOffset != 0)
            {
                status = kern.TryOpenKernValueDevice(i, out var deviceView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ReadDevice(deviceView, position + record.DeviceOffset, walk,
                    out device, out int deviceLength, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                end = Math.Max(end, record.DeviceOffset + deviceLength);
            }
            if (kernValues is not null)
            {
                kernValues[i] = new(record.Value, record.DeviceOffset);
                kernValueDevices![i] = device;
            }
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, kern.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(heights!, heightDevices!, kernValues!, kernValueDevices!,
                kern.Location, end);
        }
        return walk.PublishKern(position, model, kern.Location, budget, out diagnostic);
    }

    // ------------------------------------------------------------- variants subtree

    private ReadStatus ReadVariants(MathVariantsView variants, int position, Walk walk,
        out MathVariantsTable? model, out int length, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        length = 0;
        ReadStatus status = variants.TryGetMinConnectorOverlap(out ushort minOverlap, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = variants.TryGetVertGlyphCount(out ushort vertCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = variants.TryGetHorizGlyphCount(out ushort horizCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = variants.TryRequireVertGlyphConstructionOffsetExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = variants.TryRequireHorizGlyphConstructionOffsetExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = variants.TryGetRecordsEnd(out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, variants.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // The two coverage slots: Optional (the OQ-1 lean), each with the OQ-6 hard
        // relation against its own count (a NULL coverage implies count 0).
        CoverageTable? vertCoverage = null;
        status = variants.TryGetVertGlyphCoverageOffset(out ushort vertCoverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadVariantsCoverage(variants, vertical: true, vertCoverageOffset, vertCount,
            variants.Location.AtLocal(DataField.Count, MathVariantsLayout.VertGlyphCountAt),
            position, walk, out vertCoverage, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int end = recordsEnd;
        if (vertCoverage is not null)
            end = Math.Max(end, vertCoverageOffset + vertCoverage.ConsumedLength);
        CoverageTable? horizCoverage = null;
        status = variants.TryGetHorizGlyphCoverageOffset(out ushort horizCoverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadVariantsCoverage(variants, vertical: false, horizCoverageOffset, horizCount,
            variants.Location.AtLocal(DataField.Count, MathVariantsLayout.HorizGlyphCountAt),
            position, walk, out horizCoverage, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (horizCoverage is not null)
            end = Math.Max(end, horizCoverageOffset + horizCoverage.ConsumedLength);
        MathGlyphConstructionTable[]? vertConstructions = null;
        MathGlyphConstructionTable[]? horizConstructions = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(8L * vertCount, variants.Location, out diagnostic) ||
                !budget.Buffer(8L * horizCount, variants.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            vertConstructions = new MathGlyphConstructionTable[vertCount];
            horizConstructions = new MathGlyphConstructionTable[horizCount];
        }
        for (int i = 0; i < vertCount; i++)
        {
            // Required per-item: the opener owns the zero policy, the RecordsEnd floor
            // and the source bounds; a repeated offset is legal sharing.
            status = variants.TryGetVertGlyphConstructionOffset(i, out var offsetRow, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = variants.TryOpenVertGlyphConstruction(i, out var constructionView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadConstruction(constructionView, position + offsetRow.Offset, walk,
                out var construction, out int constructionLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (vertConstructions is not null)
                vertConstructions[i] = construction!;
            end = Math.Max(end, offsetRow.Offset + constructionLength);
        }
        for (int i = 0; i < horizCount; i++)
        {
            status = variants.TryGetHorizGlyphConstructionOffset(i, out var offsetRow, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = variants.TryOpenHorizGlyphConstruction(i, out var constructionView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadConstruction(constructionView, position + offsetRow.Offset, walk,
                out var construction, out int constructionLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (horizConstructions is not null)
                horizConstructions[i] = construction!;
            end = Math.Max(end, offsetRow.Offset + constructionLength);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, variants.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(minOverlap, vertCoverage, horizCoverage, vertConstructions!,
                horizConstructions!, variants.Location, end);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One variants coverage slot: Optional consumption plus the OQ-6 hard relation
    // (coverage NULL implies count 0; otherwise count == the covered-glyph total).
    private ReadStatus ReadVariantsCoverage(MathVariantsView variants, bool vertical,
        ushort coverageOffset, int count, DataLocation countLocation, int position, Walk walk,
        out CoverageTable? coverage, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        coverage = null;
        int covered = 0;
        if (coverageOffset != 0)
        {
            ReadStatus status = vertical
                ? variants.TryOpenVertGlyphCoverage(out var coverageView, out bool present, out diagnostic)
                : variants.TryOpenHorizGlyphCoverage(out coverageView, out present, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (present)
            {
                status = ReadCoverage(coverageView, position + coverageOffset, walk,
                    out coverage, out covered, out _, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        if (count == covered)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            countLocation, out diagnostic, (ulong)count, (ulong)covered);
    }

    private ReadStatus ReadConstruction(MathGlyphConstructionView construction, int position,
        Walk walk, out MathGlyphConstructionTable? model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        if (walk.TryTakeConstruction(position, out var cached))
        {
            model = cached;
            length = cached?.ConsumedLength ?? 0;
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = construction.TryGetGlyphAssemblyOffset(out ushort assemblyOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = construction.TryGetVariantCount(out ushort variantCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = construction.TryRequireMathGlyphVariantRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = construction.TryGetRecordsEnd(out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, construction.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        MathGlyphAssemblyTable? assembly = null;
        int assemblyLength = 0;
        if (assemblyOffset != 0)
        {
            status = construction.TryOpenGlyphAssembly(out var assemblyView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadAssembly(assemblyView, position + assemblyOffset, walk,
                out assembly, out assemblyLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        MathGlyphVariantRecord[]? variants = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(4L * variantCount, construction.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            variants = new MathGlyphVariantRecord[variantCount];
        }
        int end = Math.Max(recordsEnd, assemblyOffset + assemblyLength);
        for (int i = 0; i < variantCount; i++)
        {
            status = construction.TryGetMathGlyphVariantRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // The GID domain is the numGlyphs authority (the kern OQ-4 hard half).
            if (record.VariantGlyph >= walk.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    construction.Location.AtLocal(DataField.GlyphId,
                        (ulong)(MathGlyphConstructionLayout.VariantRecordsAt +
                            i * MathLayout.GlyphVariantRecordSize), i),
                    out diagnostic, record.VariantGlyph, (ulong)walk.NumGlyphs);
            if (variants is not null)
                variants[i] = new(record.VariantGlyph, record.AdvanceMeasurement);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, construction.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(assembly, variants!, construction.Location, end);
        }
        return walk.PublishConstruction(position, model, construction.Location, budget, out diagnostic);
    }

    private ReadStatus ReadAssembly(MathGlyphAssemblyView assembly, int position,
        Walk walk, out MathGlyphAssemblyTable? model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        if (walk.TryTakeAssembly(position, out var cached))
        {
            model = cached;
            length = cached?.ConsumedLength ?? 0;
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = assembly.TryGetItalicsCorrectionValue(out short correction, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = assembly.TryGetItalicsCorrectionDeviceOffset(out ushort correctionDeviceOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = assembly.TryGetPartCount(out ushort partCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = assembly.TryRequirePartRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = assembly.TryGetBodyEnd(out ulong bodyEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int bodyEnd = (int)bodyEndExtent;
        if (!budget.Work(bodyEnd, assembly.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        StaticDeviceTable? correctionDevice = null;
        if (correctionDeviceOffset != 0)
        {
            status = assembly.TryOpenItalicsCorrectionDevice(out var deviceView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadDevice(deviceView, position + correctionDeviceOffset, walk,
                out correctionDevice, out _, out diagnostic, budget);  // assembly device length rides the BodyEnd floor
            if (status != ReadStatus.Done) return status;
        }
        GlyphPartRecord[]? parts = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(10L * partCount, assembly.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            parts = new GlyphPartRecord[partCount];
        }
        int end = bodyEnd;
        for (int i = 0; i < partCount; i++)
        {
            status = assembly.TryGetPartRecord(i, out var part, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // The part GID domain is the numGlyphs authority; the flags word is the
            // OQ-5 observation - the raw ridge rides the model, no refusal exists.
            if (part.GlyphId >= walk.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    assembly.Location.AtLocal(DataField.GlyphId,
                        (ulong)(MathGlyphAssemblyLayout.PartRecordsAt + i * MathLayout.GlyphPartSize), i),
                    out diagnostic, part.GlyphId, (ulong)walk.NumGlyphs);
            if (parts is not null)
                parts[i] = new(part.GlyphId, part.StartConnectorLength, part.EndConnectorLength,
                    part.FullAdvance, part.PartFlags);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, assembly.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(new(correction, correctionDeviceOffset), correctionDevice, parts!,
                assembly.Location, end);
        }
        return walk.PublishAssembly(position, model, assembly.Location, budget, out diagnostic);
    }

    // ------------------------------------------------------------ shared helpers

    // The declared-reference device consumption (the SinglePos shape): the generated
    // opener owns zero/floor/bounds, this helper owns the inspection, the expansion
    // over already-inspected facts and the shared identity.
    private ReadStatus ReadDevice(StaticDeviceView device, int position, Walk walk,
        out StaticDeviceTable? model, out int length, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        length = 0;
        if (walk.TryTakeDevice(position, out var fact))
        {
            model = fact.Model;
            length = fact.Length;
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (!budget.Work(1, device.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = device.InspectWords(out int count, out int bits, out length,
            out ushort lastWord, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (walk.Materialize)
        {
            // Expansion consumes the facts InspectWords just proved (spec 6.2).
            status = device.Materialize(count, bits, length, lastWord, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        return walk.PublishDevice(position, model, length, device.Location, budget, out diagnostic);
    }

    // The coverage consumption (the T4-01 contract): inspection in both modes,
    // materialization in read mode, shared identity by wire position.
    private ReadStatus ReadCoverage(CoverageView coverage, int position, Walk walk,
        out CoverageTable? model, out int covered, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (walk.TryTakeCoverage(position, out var fact))
        {
            model = fact.Model;
            covered = fact.Covered;
            length = fact.Length;
            diagnostic = default;
            return ReadStatus.Done;
        }
        model = null;
        covered = 0;
        length = 0;
        ReadStatus status;
        if (walk.Materialize)
        {
            status = coverage.TryRead(walk.NumGlyphs, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            covered = model!.Glyphs.Length;
            length = model.ConsumedLength;
        }
        else
        {
            status = coverage.Inspect(walk.NumGlyphs, out covered, out length, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        return walk.PublishCoverage(position, model, covered, length, budget, out diagnostic);
    }

    /// <summary>Walk state: the materialize flag and the shared-identity caches keyed
    /// by root-relative wire position (validated blocks stay visited in both modes,
    /// so a shared body is proven once - the JSTF walk discipline).</summary>
    private sealed class Walk
    {
        private readonly Dictionary<int, (CoverageTable? Model, int Covered, int Length)> _coverages = new();
        private readonly Dictionary<int, (StaticDeviceTable? Model, int Length)> _devices = new();
        private readonly Dictionary<int, MathKernTable?> _kerns = new();
        private readonly Dictionary<int, MathGlyphConstructionTable?> _constructions = new();
        private readonly Dictionary<int, MathGlyphAssemblyTable?> _assemblies = new();

        internal Walk(int numGlyphs, bool materialize)
        {
            NumGlyphs = numGlyphs;
            Materialize = materialize;
        }

        internal int NumGlyphs { get; }
        internal bool Materialize { get; }

        internal bool TryTakeCoverage(int position, out (CoverageTable? Model, int Covered, int Length) fact) =>
            _coverages.TryGetValue(position, out fact);
        internal bool TryTakeDevice(int position, out (StaticDeviceTable? Model, int Length) fact) =>
            _devices.TryGetValue(position, out fact);
        internal bool TryTakeKern(int position, out MathKernTable? model) =>
            _kerns.TryGetValue(position, out model);
        internal bool TryTakeConstruction(int position, out MathGlyphConstructionTable? model) =>
            _constructions.TryGetValue(position, out model);
        internal bool TryTakeAssembly(int position, out MathGlyphAssemblyTable? model) =>
            _assemblies.TryGetValue(position, out model);

        // Publishing charges the model slot at the physical target exactly like the
        // BASE/JSTF walk discipline: a failed admission publishes nothing, so an
        // unfinished body never owns a cache slot. Validate-mode entries store the
        // null marker - the position is proven visited either way.
        internal ReadStatus PublishCoverage(int position, CoverageTable? model, int covered,
            int length, BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(32, model?.Location ?? default(DataLocation), out diagnostic))
                return ReadStatus.ResourceLimit;
            _coverages.Add(position, (model, covered, length));
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishDevice(int position, StaticDeviceTable? model, int length,
            DataLocation at, BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(model is null ? 16 : 32, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _devices.Add(position, (model, length));
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishKern(int position, MathKernTable? model, DataLocation at,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _kerns.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishConstruction(int position, MathGlyphConstructionTable? model,
            DataLocation at, BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _constructions.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishAssembly(int position, MathGlyphAssemblyTable? model,
            DataLocation at, BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _assemblies.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }
    }
}
