using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The MATH root writer: one planned, atomically published 1.0-only emission
/// whose canonical layout is the kind-grouped DFS shape - the header, the constants
/// body, the glyph-info head, the family bodies (italics, topAccent, kernInfo in
/// slot order), every unique glyph-info coverage, the kern bodies, the variants
/// head, the variant coverages, the construction bodies, the assembly bodies and
/// finally every unique device body in first-visit order (the BASE/JSTF kind-grouped
/// precedent, scaled to MATH's four-layer DAG). Each kind group references only
/// later groups, so every edge stays strictly forward on the wire even when one
/// block is shared - the shared kern bodies (STIX's 16 positions over 932 corners)
/// and the shared assembly dedupe inside their own groups, and a coverage
/// content-shared across the two subtrees resolves to the earlier glyph-info
/// representative, which the later variants head still references forward. Family
/// bodies never merge across kinds (the OQ-1 presence semantics differ), so a
/// source like Partial3's byte-shared italics/topAccent body re-emits as two
/// bodies - the canonical re-layout is wire-equivalent, never a refusal. The one
/// bounded refusal class is an offset whose final target escapes its slot's
/// Offset16 window: every declared slot preflights through its generated helper
/// and the constants device slots (OQ-2's lean form has no declared reference)
/// preflight by hand, both refusing the whole plan as UnsupportedOperation with
/// the slot's Detail/Expected.
///
/// TryCreate admission mirrors the GDEF/BASE/JSTF roots: 1.0 only, the numGlyphs
/// authority re-answered against THIS root's explicit input (a subtree read under
/// another authority does not carry its proof over - every coverage's numGlyphs,
/// the variant and part GID domains, the four MUST relations and the kernValues
/// length re-answer here), and SameSource over every subtree. The edit witnesses
/// re-root here: TryWithConstant, TryWithVariant and the TryWithPart* faces
/// produce updated bodies, the leaf factories rebuild their parents, TryCreate
/// rebuilds the root and TryCompile re-emits byte-identically for unedited
/// isomorphic sources (the write-read-write fixed point).</summary>
public static class MathTableCompiler
{
    // ------------------------------------------------------------------ factory

    /// <summary>Explicit root-model factory: the version, the numGlyphs authority and
    /// the three Optional subtrees are caller-supplied and never inferred. All-null
    /// subtrees are the legal empty table (the MathTestFontEmpty witness); the
    /// consumed length of a factory model is zero (no source extent).</summary>
    public static ReadStatus TryCreate(uint version, int numGlyphs,
        MathConstantsTable? constants, MathGlyphInfoTable? glyphInfo,
        MathVariantsTable? variants, out MathTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(0, location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (version != MathLayout.Version10)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, version);
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (constants is not null && !SameSource(location, constants.Location) ||
            glyphInfo is not null && !SameSource(location, glyphInfo.Location) ||
            variants is not null && !SameSource(location, variants.Location))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Offset, 0), out diagnostic);
        if (constants is { } c &&
            (status = AnswerConstants(c, location, out diagnostic)) != ReadStatus.Done)
            return status;
        if (glyphInfo is { } g &&
            (status = AnswerGlyphInfo(g, location, numGlyphs, out diagnostic)) != ReadStatus.Done)
            return status;
        if (variants is { } v &&
            (status = AnswerVariants(v, location, numGlyphs, out diagnostic)) != ReadStatus.Done)
            return status;
        if (!budget.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(version, numGlyphs, 0, 0, 0, constants, glyphInfo, variants,
            location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, DataLocation child) =>
        location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
        (location.SourceId == 0 || location.TableTag == child.TableTag);

    // The authority re-answer: a model read under another context carries its proof
    // as a value, never as a transfer (the kern root's re-answer rule). Coverages
    // re-answer their numGlyphs, the GID domains re-answer against this root's
    // input, the four MUST relations and the kernValues length re-derive.
    private static ReadStatus AnswerCoverage(CoverageTable? coverage, DataLocation location,
        int numGlyphs, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (coverage is null) return ReadStatus.Done;
        if (coverage.NumGlyphs != numGlyphs || !SameSource(location, coverage.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                coverage.Location, out diagnostic, (ulong)coverage.NumGlyphs, (ulong)numGlyphs);
        return ReadStatus.Done;
    }

    private static ReadStatus AnswerConstants(MathConstantsTable constants,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (constants.RecordCount != MathLayout.ConstantsRecordCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                constants.Location.AtLocal(DataField.Count, MathLayout.ConstantsValueRecordsAt),
                out diagnostic, (ulong)constants.RecordCount,
                (ulong)MathLayout.ConstantsRecordCount);
        return AnswerDevices(constants.Devices, location, out diagnostic);
    }

    private static ReadStatus AnswerDevices(IReadOnlyList<StaticDeviceTable?> devices,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        foreach (var device in devices)
        {
            if (device is null) continue;
            if (!SameSource(location, device.Location))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    device.Location, out diagnostic);
        }
        return ReadStatus.Done;
    }

    private static ReadStatus AnswerGlyphInfo(MathGlyphInfoTable glyphInfo,
        DataLocation location, int numGlyphs, out FontDiagnostic diagnostic)
    {
        ReadStatus status;
        diagnostic = default;
        if (glyphInfo.ItalicsCorrectionInfo is { } italics)
        {
            // The "Should" family: a count/coverage mismatch is the legal observation
            // face, so only the coverage's own authority re-answers here.
            if ((status = AnswerCoverage(italics.Coverage, location, numGlyphs,
                out diagnostic)) != ReadStatus.Done) return status;
            if ((status = AnswerDevices(italics.Devices, location, out diagnostic)) != ReadStatus.Done)
                return status;
        }
        if (glyphInfo.TopAccentAttachment is { } topAccent)
        {
            if ((status = AnswerFamily(topAccent.Coverage, topAccent.RecordCount,
                topAccent.Devices, location, numGlyphs, out diagnostic)) != ReadStatus.Done)
                return status;
        }
        if ((status = AnswerCoverage(glyphInfo.ExtendedShapeCoverage, location, numGlyphs,
            out diagnostic)) != ReadStatus.Done) return status;
        if (glyphInfo.KernInfo is { } kernInfo)
        {
            if ((status = AnswerCoverage(kernInfo.Coverage, location, numGlyphs,
                out diagnostic)) != ReadStatus.Done) return status;
            int covered = kernInfo.Coverage?.Glyphs.Length ?? 0;
            if (kernInfo.RecordCount != covered)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    (kernInfo.Coverage?.Location ?? kernInfo.Location).AtLocal(DataField.Count,
                        MathKernInfoLayout.CountAt), out diagnostic,
                    (ulong)kernInfo.RecordCount, (ulong)covered);
            if (!SameSource(location, kernInfo.Location))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    kernInfo.Location, out diagnostic);
            foreach (var record in kernInfo.Records)
            {
                foreach (var corner in new[] { record.TopRight, record.TopLeft,
                    record.BottomRight, record.BottomLeft })
                {
                    if (corner is null) continue;
                    if ((status = AnswerKern(corner, location, numGlyphs,
                        out diagnostic)) != ReadStatus.Done) return status;
                }
            }
        }
        return ReadStatus.Done;
    }

    // One "Must" family: the count==coverage coincidence re-derives here (the OQ-6
    // hard face at the factory; a factory model always satisfies it).
    private static ReadStatus AnswerFamily(CoverageTable? coverage, int count,
        IReadOnlyList<StaticDeviceTable?> devices, DataLocation location, int numGlyphs,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = AnswerCoverage(coverage, location, numGlyphs, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int covered = coverage?.Glyphs.Length ?? 0;
        if (count != covered)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                (coverage?.Location ?? location).AtLocal(DataField.Count, 2),
                out diagnostic, (ulong)count, (ulong)covered);
        return AnswerDevices(devices, location, out diagnostic);
    }

    private static ReadStatus AnswerKern(MathKernTable kern, DataLocation location,
        int numGlyphs, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (kern.KernValueCount != kern.HeightCount + 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                kern.Location.AtLocal(DataField.Count, MathKernLayout.HeaderLength),
                out diagnostic, (ulong)kern.KernValueCount, (ulong)(kern.HeightCount + 1));
        if (!SameSource(location, kern.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                kern.Location, out diagnostic);
        return ReadStatus.Done;
    }

    private static ReadStatus AnswerVariants(MathVariantsTable variants,
        DataLocation location, int numGlyphs, out FontDiagnostic diagnostic)
    {
        ReadStatus status = AnswerCoverage(variants.VertGlyphCoverage, location, numGlyphs,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = AnswerCoverage(variants.HorizGlyphCoverage, location, numGlyphs, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int vertCovered = variants.VertGlyphCoverage?.Glyphs.Length ?? 0;
        if (variants.VertGlyphCount != vertCovered)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                variants.Location.AtLocal(DataField.Count, MathVariantsLayout.VertGlyphCountAt),
                out diagnostic, (ulong)variants.VertGlyphCount, (ulong)vertCovered);
        int horizCovered = variants.HorizGlyphCoverage?.Glyphs.Length ?? 0;
        if (variants.HorizGlyphCount != horizCovered)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                variants.Location.AtLocal(DataField.Count, MathVariantsLayout.HorizGlyphCountAt),
                out diagnostic, (ulong)variants.HorizGlyphCount, (ulong)horizCovered);
        foreach (var construction in variants.VertConstructions)
        {
            if ((status = AnswerConstruction(construction, location, numGlyphs,
                out diagnostic)) != ReadStatus.Done) return status;
        }
        foreach (var construction in variants.HorizConstructions)
        {
            if ((status = AnswerConstruction(construction, location, numGlyphs,
                out diagnostic)) != ReadStatus.Done) return status;
        }
        return ReadStatus.Done;
    }

    private static ReadStatus AnswerConstruction(MathGlyphConstructionTable construction,
        DataLocation location, int numGlyphs, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (!SameSource(location, construction.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                construction.Location, out diagnostic);
        foreach (var variant in construction.Variants)
        {
            if (variant.VariantGlyph >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    construction.Location.AtLocal(DataField.GlyphId,
                        MathGlyphConstructionLayout.VariantRecordsAt),
                    out diagnostic, variant.VariantGlyph, (ulong)numGlyphs);
        }
        if (construction.Assembly is { } assembly && !SameSource(location, assembly.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                assembly.Location, out diagnostic);
        return ReadStatus.Done;
    }

    // ------------------------------------------------------------- leaf factories

    /// <summary>Builds the fixed constants body over the 51 value records; the device
    /// slots align with the records (null is the zero offset).</summary>
    public static ReadStatus TryCreate(short scriptPercentScaleDown,
        short scriptScriptPercentScaleDown, ushort delimitedSubFormulaMinHeight,
        ushort displayOperatorMinHeight, IReadOnlyList<MathValueRecord> values,
        short radicalDegreeBottomRaisePercent, IReadOnlyList<StaticDeviceTable?>? devices,
        out MathConstantsTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(values);
        var budget = new BuildBudget(limits);
        if (!budget.Input(values.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (values.Count != MathLayout.ConstantsRecordCount ||
            devices is { } d && d.Count != values.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MathLayout.ConstantsValueRecordsAt),
                out diagnostic, (ulong)values.Count, (ulong)MathLayout.ConstantsRecordCount);
        var owned = new MathValueRecord[values.Count];
        var ownedDevices = new StaticDeviceTable?[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            owned[i] = values[i];
            if (devices is not null) ownedDevices[i] = devices[i];
        }
        if (!budget.Buffer(8L * values.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(scriptPercentScaleDown, scriptScriptPercentScaleDown,
            delimitedSubFormulaMinHeight, displayOperatorMinHeight, owned, ownedDevices,
            radicalDegreeBottomRaisePercent, location, MathLayout.ConstantsSize);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus TryCreateRecords(IReadOnlyList<MathValueRecord> records,
        IReadOnlyList<StaticDeviceTable?>? devices, out MathValueRecord[] owned,
        out StaticDeviceTable?[] ownedDevices, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location)
    {
        owned = Array.Empty<MathValueRecord>();
        ownedDevices = Array.Empty<StaticDeviceTable?>();
        if (!budget.Input(records.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        owned = new MathValueRecord[records.Count];
        ownedDevices = new StaticDeviceTable?[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            owned[i] = records[i];
            if (devices is not null)
            {
                if (devices.Count != records.Count)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Count, 2), out diagnostic,
                        (ulong)devices.Count, (ulong)records.Count);
                ownedDevices[i] = devices[i];
            }
        }
        if (!budget.Buffer(8L * records.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one italics family (the "Should" modality: a count/coverage
    /// mismatch is the legal observation face here, never a refusal).</summary>
    public static ReadStatus TryCreate(CoverageTable? coverage,
        IReadOnlyList<MathValueRecord> records, IReadOnlyList<StaticDeviceTable?>? devices,
        out MathItalicsCorrectionInfoTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(records);
        var budget = new BuildBudget(limits ?? new BuildLimits());
        ReadStatus status = TryCreateRecords(records, devices, out var owned,
            out var ownedDevices, out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        model = new(coverage, owned, ownedDevices, location,
            MathItalicsCorrectionInfoLayout.HeaderLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one topAccent family; the "Must" coincidence re-derives here,
    /// so a mismatching count refuses (the OQ-6 hard face at the factory).</summary>
    public static ReadStatus TryCreate(CoverageTable? coverage,
        IReadOnlyList<MathValueRecord> records, IReadOnlyList<StaticDeviceTable?>? devices,
        out MathTopAccentAttachmentTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(records);
        var budget = new BuildBudget(limits ?? new BuildLimits());
        ReadStatus status = AnswerFamily(coverage, records.Count,
            devices ?? Array.Empty<StaticDeviceTable?>(), location, coverage?.NumGlyphs ?? 0,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryCreateRecords(records, devices, out var owned, out var ownedDevices,
            out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        model = new(coverage, owned, ownedDevices, location,
            MathTopAccentAttachmentLayout.HeaderLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one kernInfo over the corner records; the "Must" coincidence
    /// re-derives here.</summary>
    public static ReadStatus TryCreate(CoverageTable? coverage,
        IReadOnlyList<MathKernInfoRecord> records, out MathKernInfoTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(records);
        var budget = new BuildBudget(limits);
        if (!budget.Input(records.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = AnswerCoverage(coverage, location, coverage?.NumGlyphs ?? 0,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        int covered = coverage?.Glyphs.Length ?? 0;
        if (records.Count != covered)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                (coverage?.Location ?? location).AtLocal(DataField.Count, 2),
                out diagnostic, (ulong)records.Count, (ulong)covered);
        var owned = new MathKernInfoRecord[records.Count];
        for (int i = 0; i < records.Count; i++) owned[i] = records[i];
        if (!budget.Buffer(8L * records.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(coverage, owned, location, MathKernInfoLayout.HeaderLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one kern body; the kernValues length re-derives as
    /// heightCount + 1 here (the OQ-3 computed count at the factory).</summary>
    public static ReadStatus TryCreate(IReadOnlyList<MathValueRecord> heights,
        IReadOnlyList<MathValueRecord> kernValues, out MathKernTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(kernValues);
        var budget = new BuildBudget(limits);
        if (!budget.Input(heights.Count + kernValues.Count, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (kernValues.Count != heights.Count + 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MathKernLayout.HeaderLength),
                out diagnostic, (ulong)kernValues.Count, (ulong)(heights.Count + 1));
        var ownedHeights = new MathValueRecord[heights.Count];
        for (int i = 0; i < heights.Count; i++) ownedHeights[i] = heights[i];
        var ownedValues = new MathValueRecord[kernValues.Count];
        for (int i = 0; i < kernValues.Count; i++) ownedValues[i] = kernValues[i];
        if (!budget.Buffer(8L * (heights.Count + kernValues.Count), location, out diagnostic))
            return ReadStatus.ResourceLimit;
        model = new(ownedHeights, new StaticDeviceTable?[heights.Count], ownedValues,
            new StaticDeviceTable?[kernValues.Count], location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one variants body; both "Must" coincidences re-derive here.</summary>
    public static ReadStatus TryCreate(ushort minConnectorOverlap,
        CoverageTable? vertGlyphCoverage, CoverageTable? horizGlyphCoverage,
        IReadOnlyList<MathGlyphConstructionTable> vertConstructions,
        IReadOnlyList<MathGlyphConstructionTable> horizConstructions,
        out MathVariantsTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(vertConstructions);
        ArgumentNullException.ThrowIfNull(horizConstructions);
        var budget = new BuildBudget(limits);
        if (!budget.Input(vertConstructions.Count + horizConstructions.Count, location,
            out diagnostic)) return ReadStatus.ResourceLimit;
        int vertCovered = vertGlyphCoverage?.Glyphs.Length ?? 0;
        if (vertConstructions.Count != vertCovered)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MathVariantsLayout.VertGlyphCountAt),
                out diagnostic, (ulong)vertConstructions.Count, (ulong)vertCovered);
        int horizCovered = horizGlyphCoverage?.Glyphs.Length ?? 0;
        if (horizConstructions.Count != horizCovered)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MathVariantsLayout.HorizGlyphCountAt),
                out diagnostic, (ulong)horizConstructions.Count, (ulong)horizCovered);
        var vert = new MathGlyphConstructionTable[vertConstructions.Count];
        for (int i = 0; i < vertConstructions.Count; i++) vert[i] = vertConstructions[i];
        var horiz = new MathGlyphConstructionTable[horizConstructions.Count];
        for (int i = 0; i < horizConstructions.Count; i++) horiz[i] = horizConstructions[i];
        if (!budget.Buffer(8L * (vertConstructions.Count + horizConstructions.Count),
            location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(minConnectorOverlap, vertGlyphCoverage, horizGlyphCoverage,
            vert, horiz, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one construction; the variant GID domain re-answers here.</summary>
    public static ReadStatus TryCreate(MathGlyphAssemblyTable? assembly,
        IReadOnlyList<MathGlyphVariantRecord> variants, int numGlyphs,
        out MathGlyphConstructionTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(variants);
        var budget = new BuildBudget(limits);
        if (!budget.Input(variants.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var owned = new MathGlyphVariantRecord[variants.Count];
        for (int i = 0; i < variants.Count; i++)
        {
            var variant = variants[i];
            if (variant.VariantGlyph >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId,
                        (ulong)(MathGlyphConstructionLayout.VariantRecordsAt +
                            i * MathLayout.GlyphVariantRecordSize), i),
                    out diagnostic, variant.VariantGlyph, (ulong)numGlyphs);
            owned[i] = variant;
        }
        if (!budget.Buffer(4L * variants.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(assembly, owned, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one assembly; the part GID domain re-answers here.</summary>
    public static ReadStatus TryCreate(MathValueRecord italicsCorrection,
        StaticDeviceTable? italicsCorrectionDevice, IReadOnlyList<GlyphPartRecord> parts,
        int numGlyphs, out MathGlyphAssemblyTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(parts);
        var budget = new BuildBudget(limits);
        if (!budget.Input(parts.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var owned = new GlyphPartRecord[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.GlyphId >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId,
                        (ulong)(MathGlyphAssemblyLayout.PartRecordsAt +
                            i * MathLayout.GlyphPartSize), i),
                    out diagnostic, part.GlyphId, (ulong)numGlyphs);
            owned[i] = part;
        }
        if (!budget.Buffer(10L * parts.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(italicsCorrection, italicsCorrectionDevice, owned, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ------------------------------------------------------------------ plan

    /// <summary>The dedupe-aware placement: the representatives in first-visit order
    /// per kind group with their planned positions. Collection runs in the canonical
    /// DFS order so every content comparison sees its children's representatives;
    /// position assignment runs in group order (each group after every group that
    /// can reference it).</summary>
    private sealed class Placement
    {
        internal int Length;
        internal MathConstantsTable? Constants;
        internal MathGlyphInfoTable? GlyphInfo;
        internal MathItalicsCorrectionInfoTable? Italics;
        internal MathTopAccentAttachmentTable? TopAccent;
        internal MathKernInfoTable? KernInfo;
        internal List<CoverageTable> GlyphInfoCoverages = new();
        internal List<MathKernTable> Kerns = new();
        internal MathVariantsTable? Variants;
        internal List<CoverageTable> VariantCoverages = new();
        internal List<MathGlyphConstructionTable> Constructions = new();
        internal List<MathGlyphAssemblyTable> Assemblies = new();
        internal List<StaticDeviceTable> Devices = new();

        internal Dictionary<CoverageTable, CoverageTable> GlyphInfoCoverageRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<MathKernTable, MathKernTable> KernRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<CoverageTable, CoverageTable> VariantCoverageRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<MathGlyphConstructionTable, MathGlyphConstructionTable> ConstructionRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<MathGlyphAssemblyTable, MathGlyphAssemblyTable> AssemblyRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<StaticDeviceTable, StaticDeviceTable> DeviceRep = new(ReferenceEqualityComparer.Instance);

        internal Dictionary<CoverageTable, int> CoverageAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<MathKernTable, int> KernAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<MathGlyphConstructionTable, int> ConstructionAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<MathGlyphAssemblyTable, int> AssemblyAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<StaticDeviceTable, int> DeviceAt = new(ReferenceEqualityComparer.Instance);
    }

    public static ReadStatus TryCompile(MathTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MathTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan!.Length, model.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MathTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Every plan admission completes before the destination capacity check (spec
        // 7.3; the A6 1.5 O4 order); the destination is untouched on refusal.
        if (!budget.Work(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (destination.Length < plan.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The one deterministic plan: collection in the canonical DFS order (constants,
    // glyph-info families in slot order, their coverages, the kern bodies, variants,
    // the variant coverages, constructions, assemblies, devices), then position
    // assignment in group order.
    private static ReadStatus TryPlan(MathTable model, out Placement? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        var placement = new Placement();
        placement.Constants = model.Constants;
        if (model.Constants is { } constants)
        {
            foreach (var device in constants.Devices) CollectDevice(device, placement);
        }
        placement.GlyphInfo = model.GlyphInfo;
        CollectGlyphInfo(model.GlyphInfo, placement);
        placement.Variants = model.Variants;
        CollectVariants(model.Variants, placement);

        int cursor = MathLayout.Header10;
        if (!budget.Work(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (placement.Constants is not null)
        {
            if (!budget.Work(MathLayout.ConstantsSize, placement.Constants.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            cursor += MathLayout.ConstantsSize;
        }
        if (placement.GlyphInfo is not null)
        {
            if (!budget.Work(MathGlyphInfoLayout.HeaderLength, placement.GlyphInfo.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            cursor += MathGlyphInfoLayout.HeaderLength;
            cursor += FamilyBodyLength(placement.Italics);
            cursor += FamilyBodyLength(placement.TopAccent);
            if (placement.KernInfo is { } kernInfo)
            {
                cursor += MathKernInfoLayout.HeaderLength +
                    MathLayout.KernInfoRecordSize * kernInfo.RecordCount;
            }
            foreach (var coverage in placement.GlyphInfoCoverages)
            {
                int length = CoverageTableCompiler.Measure(coverage);
                if (!budget.Work(length, coverage.Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                placement.CoverageAt[coverage] = cursor;
                cursor += length;
            }
            foreach (var kern in placement.Kerns)
            {
                int length = KernLength(kern);
                if (!budget.Work(length, kern.Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                placement.KernAt[kern] = cursor;
                cursor += length;
            }
        }
        if (placement.Variants is not null)
        {
            int length = MathVariantsLayout.ConstructionOffsetsAt +
                MathVariantsLayout.ConstructionOffsetStride *
                (placement.Variants.VertGlyphCount + placement.Variants.HorizGlyphCount);
            if (!budget.Work(length, placement.Variants.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            cursor += length;
            foreach (var coverage in placement.VariantCoverages)
            {
                if (placement.CoverageAt.ContainsKey(coverage)) continue;
                int span = CoverageTableCompiler.Measure(coverage);
                if (!budget.Work(span, coverage.Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                placement.CoverageAt[coverage] = cursor;
                cursor += span;
            }
            foreach (var construction in placement.Constructions)
            {
                int span = MathGlyphConstructionLayout.HeaderLength +
                    MathLayout.GlyphVariantRecordSize * construction.VariantCount;
                if (!budget.Work(span, construction.Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                placement.ConstructionAt[construction] = cursor;
                cursor += span;
            }
            foreach (var assembly in placement.Assemblies)
            {
                int span = MathGlyphAssemblyLayout.PartRecordsAt +
                    MathLayout.GlyphPartSize * assembly.PartCount;
                if (!budget.Work(span, assembly.Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                placement.AssemblyAt[assembly] = cursor;
                cursor += span;
            }
        }
        // The glyph-info coverages close the plan: every family body and both subtree
        // heads precede them, so a coverage instance shared across the two subtrees
        // (Partial2's extended/variant wire form) still resolves forward from every
        // referrer.
        foreach (var coverage in placement.GlyphInfoCoverages)
        {
            int length = CoverageTableCompiler.Measure(coverage);
            if (!budget.Work(length, coverage.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            placement.CoverageAt[coverage] = cursor;
            cursor += length;
        }
        foreach (var device in placement.Devices)
        {
            int length = StaticDeviceTableCompiler.Measure(device);
            if (!budget.Work(length, device.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            placement.DeviceAt[device] = cursor;
            cursor += length;
        }
        placement.Length = cursor;
        plan = placement;
        return Preflight(model, placement, out diagnostic);
    }

    private static void CollectGlyphInfo(MathGlyphInfoTable? glyphInfo, Placement placement)
    {
        if (glyphInfo is null) return;
        if (glyphInfo.ItalicsCorrectionInfo is { } italics)
        {
            placement.Italics = italics;
            CollectCoverage(italics.Coverage, placement.GlyphInfoCoverageRep,
                placement.GlyphInfoCoverages);
            foreach (var device in italics.Devices) CollectDevice(device, placement);
        }
        if (glyphInfo.TopAccentAttachment is { } topAccent)
        {
            placement.TopAccent = topAccent;
            CollectCoverage(topAccent.Coverage, placement.GlyphInfoCoverageRep,
                placement.GlyphInfoCoverages);
            foreach (var device in topAccent.Devices) CollectDevice(device, placement);
        }
        CollectCoverage(glyphInfo.ExtendedShapeCoverage, placement.GlyphInfoCoverageRep,
            placement.GlyphInfoCoverages);
        if (glyphInfo.KernInfo is { } kernInfo)
        {
            placement.KernInfo = kernInfo;
            CollectCoverage(kernInfo.Coverage, placement.GlyphInfoCoverageRep,
                placement.GlyphInfoCoverages);
            foreach (var record in kernInfo.Records)
            {
                CollectKern(record.TopRight, placement);
                CollectKern(record.TopLeft, placement);
                CollectKern(record.BottomRight, placement);
                CollectKern(record.BottomLeft, placement);
            }
        }
    }

    private static void CollectKern(MathKernTable? kern, Placement placement)
    {
        if (kern is null || placement.KernRep.ContainsKey(kern)) return;
        // The body's device slots collect first so the identity comparison sees the
        // children's representatives (the JSTF collection order).
        foreach (var device in kern.HeightDevices) CollectDevice(device, placement);
        foreach (var device in kern.KernValueDevices) CollectDevice(device, placement);
        var representative = Find(placement.Kerns, kern, (a, b) => SameKern(a, b, placement));
        placement.KernRep[kern] = representative ?? kern;
        if (representative is null) placement.Kerns.Add(kern);
    }

    private static void CollectCoverage(CoverageTable? coverage,
        Dictionary<CoverageTable, CoverageTable> reps, List<CoverageTable> list)
    {
        if (coverage is null || reps.ContainsKey(coverage)) return;
        var representative = Find(list, coverage, SameCoverage);
        reps[coverage] = representative ?? coverage;
        if (representative is null) list.Add(coverage);
    }

    private static void CollectVariants(MathVariantsTable? variants, Placement placement)
    {
        if (variants is null) return;
        CollectCoverage(variants.VertGlyphCoverage, placement.VariantCoverageRep,
            placement.VariantCoverages);
        CollectCoverage(variants.HorizGlyphCoverage, placement.VariantCoverageRep,
            placement.VariantCoverages);
        foreach (var construction in variants.VertConstructions)
            CollectConstruction(construction, placement);
        foreach (var construction in variants.HorizConstructions)
            CollectConstruction(construction, placement);
    }

    private static void CollectConstruction(MathGlyphConstructionTable construction,
        Placement placement)
    {
        if (placement.ConstructionRep.ContainsKey(construction)) return;
        if (construction.Assembly is { } assembly)
            CollectAssembly(assembly, placement);
        var representative = Find(placement.Constructions, construction,
            (a, b) => SameConstruction(a, b, placement));
        placement.ConstructionRep[construction] = representative ?? construction;
        if (representative is null) placement.Constructions.Add(construction);
    }

    private static void CollectAssembly(MathGlyphAssemblyTable assembly, Placement placement)
    {
        if (placement.AssemblyRep.ContainsKey(assembly)) return;
        CollectDevice(assembly.ItalicsCorrectionDevice, placement);
        var representative = Find(placement.Assemblies, assembly, (a, b) => SameAssembly(a, b, placement));
        placement.AssemblyRep[assembly] = representative ?? assembly;
        if (representative is null) placement.Assemblies.Add(assembly);
    }

    private static void CollectDevice(StaticDeviceTable? device, Placement placement)
    {
        if (device is null || placement.DeviceRep.ContainsKey(device)) return;
        var representative = Find(placement.Devices, device, SameDevice);
        placement.DeviceRep[device] = representative ?? device;
        if (representative is null) placement.Devices.Add(device);
    }

    private static T? Find<T>(List<T> bodies, T candidate, Func<T, T, bool> same) where T : class
    {
        foreach (var body in bodies)
            if (same(body, candidate)) return body;
        return null;
    }

    private static bool SameCoverage(CoverageTable a, CoverageTable b)
    {
        if (a.Format != b.Format || a.Glyphs.Length != b.Glyphs.Length) return false;
        for (int i = 0; i < a.Glyphs.Length; i++)
            if (a.Glyphs[i] != b.Glyphs[i]) return false;
        return true;
    }

    private static bool SameKern(MathKernTable a, MathKernTable b, Placement placement)
    {
        if (a.HeightCount != b.HeightCount || a.KernValueCount != b.KernValueCount) return false;
        for (int i = 0; i < a.HeightCount; i++)
        {
            if (a.CorrectionHeights[i].Value != b.CorrectionHeights[i].Value) return false;
            if (!SameDeviceSlot(a.HeightDevices[i], b.HeightDevices[i], placement)) return false;
        }
        for (int i = 0; i < a.KernValueCount; i++)
        {
            if (a.KernValues[i].Value != b.KernValues[i].Value) return false;
            if (!SameDeviceSlot(a.KernValueDevices[i], b.KernValueDevices[i], placement)) return false;
        }
        return true;
    }

    // One device slot's identity: both null, or one shared representative (the raw
    // offsets are layout, never content - the two-level dedup reaches its fixed
    // point in one plan).
    private static bool SameDeviceSlot(StaticDeviceTable? a, StaticDeviceTable? b, Placement placement) =>
        a is null ? b is null
            : b is not null && ReferenceEquals(placement.DeviceRep[a], placement.DeviceRep[b]);

    private static bool SameConstruction(MathGlyphConstructionTable a, MathGlyphConstructionTable b,
        Placement placement)
    {
        if ((a.Assembly is null) != (b.Assembly is null)) return false;
        if (a.Assembly is { } x && b.Assembly is { } y &&
            !ReferenceEquals(placement.AssemblyRep[x], placement.AssemblyRep[y])) return false;
        if (a.VariantCount != b.VariantCount) return false;
        for (int i = 0; i < a.VariantCount; i++)
            if (a.Variants[i] != b.Variants[i]) return false;
        return true;
    }

    private static bool SameAssembly(MathGlyphAssemblyTable a, MathGlyphAssemblyTable b,
        Placement placement)
    {
        if (a.ItalicsCorrection != b.ItalicsCorrection || a.PartCount != b.PartCount) return false;
        if (!SameDeviceSlot(a.ItalicsCorrectionDevice, b.ItalicsCorrectionDevice, placement))
            return false;
        for (int i = 0; i < a.PartCount; i++)
            if (a.Parts[i] != b.Parts[i]) return false;
        return true;
    }

    private static bool SameDevice(StaticDeviceTable a, StaticDeviceTable b)
    {
        if (a.StartSize != b.StartSize || a.EndSize != b.EndSize ||
            a.DeltaFormat != b.DeltaFormat || a.Deltas.Length != b.Deltas.Length) return false;
        for (int i = 0; i < a.Deltas.Length; i++)
            if (a.Deltas[i] != b.Deltas[i]) return false;
        return true;
    }

    private static int KernLength(MathKernTable kern) =>
        MathKernLayout.HeaderLength +
        MathLayout.ValueRecordSize * (kern.HeightCount + kern.KernValueCount);

    private static int FamilyBodyLength<T>(T? family) where T : class =>
        family switch
        {
            MathItalicsCorrectionInfoTable f => MathItalicsCorrectionInfoLayout.HeaderLength +
                MathLayout.ValueRecordSize * f.RecordCount,
            MathTopAccentAttachmentTable f => MathTopAccentAttachmentLayout.HeaderLength +
                MathLayout.ValueRecordSize * f.RecordCount,
            _ => 0,
        };

    private static int GlyphInfoLength(Placement plan)
    {
        int length = MathGlyphInfoLayout.HeaderLength +
            FamilyBodyLength(plan.Italics) + FamilyBodyLength(plan.TopAccent);
        if (plan.KernInfo is { } kernInfo)
            length += MathKernInfoLayout.HeaderLength +
                MathLayout.KernInfoRecordSize * kernInfo.RecordCount;
        foreach (var coverage in plan.GlyphInfoCoverages)
            length += CoverageTableCompiler.Measure(coverage);
        foreach (var kern in plan.Kerns)
            length += KernLength(kern);
        return length;
    }

    // ------------------------------------------------------------------ preflight

    // Every declared offset slot preflights its final target through its generated
    // helper (a null Optional slot encodes zero through the same helper); a
    // too-narrow target refuses the whole plan (UnsupportedOperation with the slot's
    // Detail/Expected). The constants device slots have no declared reference
    // (OQ-2's lean), so their domain preflights by hand (the JstfMax OQ-6 shape).
    private static ReadStatus Preflight(MathTable model, Placement plan, out FontDiagnostic diagnostic)
    {
        var location = model.Location;
        ReadStatus status;
        int constantsAt = MathLayout.Header10;
        int glyphInfoAt = constantsAt + (model.Constants is null ? 0 : MathLayout.ConstantsSize);
        int variantsAt = glyphInfoAt + (model.GlyphInfo is null ? 0 : GlyphInfoLength(plan));
        if ((status = MathView.TryGetConstantsOffsetValue(model.Constants is not null, 0UL,
            (ulong)(model.Constants is null ? 0 : constantsAt), out _, out diagnostic,
            location.AtLocal(DataField.Offset, MathLayout.ConstantsOffsetAt))) != ReadStatus.Done ||
            (status = MathView.TryGetGlyphInfoOffsetValue(model.GlyphInfo is not null, 0UL,
            (ulong)(model.GlyphInfo is null ? 0 : glyphInfoAt), out _, out diagnostic,
            location.AtLocal(DataField.Offset, MathLayout.GlyphInfoOffsetAt))) != ReadStatus.Done ||
            (status = MathView.TryGetVariantsOffsetValue(model.Variants is not null, 0UL,
            (ulong)(model.Variants is null ? 0 : variantsAt), out _, out diagnostic,
            location.AtLocal(DataField.Offset, MathLayout.VariantsOffsetAt))) != ReadStatus.Done)
            return status;
        if (model.Constants is { } constants)
        {
            for (int i = 0; i < constants.RecordCount; i++)
            {
                var device = constants.Devices[i];
                if (device is null) continue;
                int delta = plan.DeviceAt[plan.DeviceRep[device]] - constantsAt;
                if (delta is < 1 or > ushort.MaxValue)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                        constants.Location.AtLocal(DataField.Offset,
                            (ulong)(MathLayout.ConstantsValueRecordsAt +
                                i * MathLayout.ValueRecordSize + MathLayout.ValueDeviceOffsetAt), i),
                        out diagnostic, (ulong)delta, (ulong)ushort.MaxValue);
            }
        }
        if (model.GlyphInfo is { } glyphInfo)
        {
            int italicsAt = glyphInfoAt + MathGlyphInfoLayout.HeaderLength;
            int topAccentAt = italicsAt + FamilyBodyLength(plan.Italics);
            int kernInfoAt = topAccentAt + FamilyBodyLength(plan.TopAccent);
            if ((status = MathGlyphInfoView.TryGetItalicsCorrectionInfoOffsetValue(
                glyphInfo.ItalicsCorrectionInfo is not null, 0UL,
                (ulong)(glyphInfo.ItalicsCorrectionInfo is null ? 0 : italicsAt - glyphInfoAt),
                out _, out diagnostic,
                glyphInfo.Location.AtLocal(DataField.Offset,
                    MathGlyphInfoLayout.ItalicsCorrectionInfoOffsetAt))) != ReadStatus.Done ||
                (status = MathGlyphInfoView.TryGetTopAccentAttachmentOffsetValue(
                glyphInfo.TopAccentAttachment is not null, 0UL,
                (ulong)(glyphInfo.TopAccentAttachment is null ? 0 : topAccentAt - glyphInfoAt),
                out _, out diagnostic,
                glyphInfo.Location.AtLocal(DataField.Offset,
                    MathGlyphInfoLayout.TopAccentAttachmentOffsetAt))) != ReadStatus.Done ||
                (status = MathGlyphInfoView.TryGetExtendedShapeCoverageOffsetValue(
                glyphInfo.ExtendedShapeCoverage is not null, 0UL,
                (ulong)(glyphInfo.ExtendedShapeCoverage is null ? 0
                    : plan.CoverageAt[plan.GlyphInfoCoverageRep[glyphInfo.ExtendedShapeCoverage]] - glyphInfoAt),
                out _, out diagnostic,
                glyphInfo.Location.AtLocal(DataField.Offset,
                    MathGlyphInfoLayout.ExtendedShapeCoverageOffsetAt))) != ReadStatus.Done ||
                (status = MathGlyphInfoView.TryGetKernInfoOffsetValue(
                glyphInfo.KernInfo is not null, 0UL,
                (ulong)(glyphInfo.KernInfo is null ? 0 : kernInfoAt - glyphInfoAt),
                out _, out diagnostic,
                glyphInfo.Location.AtLocal(DataField.Offset, MathGlyphInfoLayout.KernInfoOffsetAt)))
                != ReadStatus.Done)
                return status;
            if (glyphInfo.ItalicsCorrectionInfo is { } italics)
            {
                var coverage = italics.Coverage;
                if ((status = MathItalicsCorrectionInfoView.TryGetCoverageOffsetValue(
                    coverage is not null, 0UL,
                    (ulong)(coverage is null ? 0
                        : plan.CoverageAt[plan.GlyphInfoCoverageRep[coverage]] - italicsAt),
                    out _, out diagnostic,
                    italics.Location.AtLocal(DataField.Offset,
                        MathItalicsCorrectionInfoLayout.CoverageOffsetAt))) != ReadStatus.Done)
                    return status;
                if ((status = PreflightDevices(italics.Devices, plan, italicsAt,
                    italics.Location, MathItalicsCorrectionInfoLayout.HeaderLength,
                    MathLayout.ValueDeviceOffsetAt, out diagnostic)) != ReadStatus.Done)
                    return status;
            }
            if (glyphInfo.TopAccentAttachment is { } topAccent)
            {
                var coverage = topAccent.Coverage;
                if ((status = MathTopAccentAttachmentView.TryGetCoverageOffsetValue(
                    coverage is not null, 0UL,
                    (ulong)(coverage is null ? 0
                        : plan.CoverageAt[plan.GlyphInfoCoverageRep[coverage]] - topAccentAt),
                    out _, out diagnostic,
                    topAccent.Location.AtLocal(DataField.Offset,
                        MathTopAccentAttachmentLayout.CoverageOffsetAt))) != ReadStatus.Done)
                    return status;
                if ((status = PreflightDevices(topAccent.Devices, plan, topAccentAt,
                    topAccent.Location, MathTopAccentAttachmentLayout.HeaderLength,
                    MathLayout.ValueDeviceOffsetAt, out diagnostic)) != ReadStatus.Done)
                    return status;
            }
            if (glyphInfo.KernInfo is { } kernInfo)
            {
                var coverage = kernInfo.Coverage;
                if ((status = MathKernInfoView.TryGetCoverageOffsetValue(
                    coverage is not null, 0UL,
                    (ulong)(coverage is null ? 0
                        : plan.CoverageAt[plan.GlyphInfoCoverageRep[coverage]] - kernInfoAt),
                    out _, out diagnostic,
                    kernInfo.Location.AtLocal(DataField.Offset,
                        MathKernInfoLayout.CoverageOffsetAt))) != ReadStatus.Done)
                    return status;
                for (int i = 0; i < kernInfo.RecordCount; i++)
                {
                    var record = kernInfo.Records[i];
                    ulong rowAt = MathKernInfoLayout.HeaderLength +
                        (ulong)i * MathLayout.KernInfoRecordSize;
                    if ((status = PreflightCorner(record.TopRight, i, rowAt,
                        MathKernInfoLayout.RecordTopRightAt, kernInfoAt, plan,
                        MathKernInfoView.TryGetTopRightMathKernOffsetValue,
                        glyphInfo.Location, out diagnostic)) != ReadStatus.Done ||
                        (status = PreflightCorner(record.TopLeft, i, rowAt,
                        MathKernInfoLayout.RecordTopLeftAt, kernInfoAt, plan,
                        MathKernInfoView.TryGetTopLeftMathKernOffsetValue,
                        glyphInfo.Location, out diagnostic)) != ReadStatus.Done ||
                        (status = PreflightCorner(record.BottomRight, i, rowAt,
                        MathKernInfoLayout.RecordBottomRightAt, kernInfoAt, plan,
                        MathKernInfoView.TryGetBottomRightMathKernOffsetValue,
                        glyphInfo.Location, out diagnostic)) != ReadStatus.Done ||
                        (status = PreflightCorner(record.BottomLeft, i, rowAt,
                        MathKernInfoLayout.RecordBottomLeftAt, kernInfoAt, plan,
                        MathKernInfoView.TryGetBottomLeftMathKernOffsetValue,
                        glyphInfo.Location, out diagnostic)) != ReadStatus.Done)
                        return status;
                }
                foreach (var kern in plan.Kerns)
                {
                    int kernAt = plan.KernAt[kern];
                    if ((status = PreflightDevices(kern.HeightDevices, plan, kernAt,
                        kern.Location, MathKernLayout.CorrectionHeightsAt,
                        MathLayout.ValueDeviceOffsetAt, out diagnostic)) != ReadStatus.Done)
                        return status;
                    if ((status = PreflightDevices(kern.KernValueDevices, plan, kernAt,
                        kern.Location, MathKernLayout.CorrectionHeightsAt +
                            MathLayout.ValueRecordSize * kern.HeightCount,
                        MathLayout.ValueDeviceOffsetAt, out diagnostic)) != ReadStatus.Done)
                        return status;
                }
            }
        }
        if (model.Variants is { } variants)
        {
            var vertCoverage = variants.VertGlyphCoverage;
            var horizCoverage = variants.HorizGlyphCoverage;
            if ((status = MathVariantsView.TryGetVertGlyphCoverageOffsetValue(
                vertCoverage is not null, 0UL,
                (ulong)(vertCoverage is null ? 0
                    : plan.CoverageAt[CoverageRep(plan, vertCoverage)] - variantsAt),
                out _, out diagnostic,
                variants.Location.AtLocal(DataField.Offset,
                    MathVariantsLayout.VertGlyphCoverageOffsetAt))) != ReadStatus.Done)
                return status;
            if ((status = MathVariantsView.TryGetHorizGlyphCoverageOffsetValue(
                horizCoverage is not null, 0UL,
                (ulong)(horizCoverage is null ? 0
                    : plan.CoverageAt[CoverageRep(plan, horizCoverage)] - variantsAt),
                out _, out diagnostic,
                variants.Location.AtLocal(DataField.Offset,
                    MathVariantsLayout.HorizGlyphCoverageOffsetAt))) != ReadStatus.Done)
                return status;
            for (int i = 0; i < variants.VertGlyphCount; i++)
            {
                int delta = plan.ConstructionAt[plan.ConstructionRep[variants.VertConstructions[i]]] - variantsAt;
                if ((status = MathVariantsView.TryGetVertGlyphConstructionOffsetValue(
                    present: true, 0UL, (ulong)delta, out _, out diagnostic,
                    variants.Location.AtLocal(DataField.Offset,
                        (ulong)(MathVariantsLayout.ConstructionOffsetsAt +
                            i * MathVariantsLayout.ConstructionOffsetStride), i))) != ReadStatus.Done)
                    return status;
            }
            for (int i = 0; i < variants.HorizGlyphCount; i++)
            {
                int delta = plan.ConstructionAt[plan.ConstructionRep[variants.HorizConstructions[i]]] - variantsAt;
                if ((status = MathVariantsView.TryGetHorizGlyphConstructionOffsetValue(
                    present: true, 0UL, (ulong)delta, out _, out diagnostic,
                    variants.Location.AtLocal(DataField.Offset,
                        (ulong)(MathVariantsLayout.ConstructionOffsetsAt +
                            MathVariantsLayout.ConstructionOffsetStride * variants.VertGlyphCount +
                            i * MathVariantsLayout.ConstructionOffsetStride), i))) != ReadStatus.Done)
                    return status;
            }
            foreach (var construction in plan.Constructions)
            {
                int constructionAt = plan.ConstructionAt[construction];
                var assembly = construction.Assembly;
                if ((status = MathGlyphConstructionView.TryGetGlyphAssemblyOffsetValue(
                    assembly is not null, 0UL,
                    (ulong)(assembly is null ? 0
                        : plan.AssemblyAt[plan.AssemblyRep[assembly]] - constructionAt),
                    out _, out diagnostic,
                    construction.Location.AtLocal(DataField.Offset,
                        MathGlyphConstructionLayout.GlyphAssemblyOffsetAt))) != ReadStatus.Done)
                    return status;
            }
            foreach (var assembly in plan.Assemblies)
            {
                int assemblyAt = plan.AssemblyAt[assembly];
                var device = assembly.ItalicsCorrectionDevice;
                if (device is null) continue;
                int delta = plan.DeviceAt[plan.DeviceRep[device]] - assemblyAt;
                if ((status = MathGlyphAssemblyView.TryGetItalicsCorrectionDeviceOffsetValue(
                    present: true, 0UL, (ulong)delta, out _, out diagnostic,
                    assembly.Location.AtLocal(DataField.Offset,
                        MathGlyphAssemblyLayout.ItalicsCorrectionDeviceOffsetAt))) != ReadStatus.Done)
                    return status;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static CoverageTable CoverageRep(Placement plan, CoverageTable coverage) =>
        plan.GlyphInfoCoverageRep.TryGetValue(coverage, out var rep) ? rep
            : plan.VariantCoverageRep[coverage];

    private delegate ReadStatus CornerSlotValue(bool present, ulong origin, ulong target,
        out ushort encoded, out FontDiagnostic diagnostic, DataLocation location);

    private static ReadStatus PreflightCorner(MathKernTable? corner, int ordinal, ulong rowAt,
        int cornerFieldAt, int kernInfoAt, Placement plan, CornerSlotValue encode,
        DataLocation location, out FontDiagnostic diagnostic) =>
        encode(corner is not null, 0UL,
            (ulong)(corner is null ? 0 : plan.KernAt[plan.KernRep[corner]] - kernInfoAt),
            out _, out diagnostic,
            location.AtLocal(DataField.Offset, rowAt + (ulong)cornerFieldAt, ordinal));

    private static ReadStatus PreflightDevices(IReadOnlyList<StaticDeviceTable?> devices,
        Placement placement, int baseAt, DataLocation location, int firstRowAt,
        int deviceFieldStride, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        for (int i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            if (device is null) continue;
            int delta = placement.DeviceAt[placement.DeviceRep[device]] - baseAt;
            if (delta is < 1 or > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Offset,
                        (ulong)(firstRowAt + i * MathLayout.ValueRecordSize + deviceFieldStride), i),
                    out diagnostic, (ulong)delta, (ulong)ushort.MaxValue);
        }
        return ReadStatus.Done;
    }

    // ------------------------------------------------------------------ emission

    // Only a fully preflighted plan and destination reach this path; each body
    // writes exactly once at its position (content duplicates share the position,
    // and equal content makes either instance's bytes identical).
    private static void WritePlanned(MathTable model, Placement plan, Span<byte> destination)
    {
        var location = model.Location;
        int constantsAt = MathLayout.Header10;
        int glyphInfoAt = constantsAt + (model.Constants is null ? 0 : MathLayout.ConstantsSize);
        int variantsAt = glyphInfoAt + (model.GlyphInfo is null ? 0 : GlyphInfoLength(plan));
        CoverageTableCompiler.RequireWritten(MathView.TryWriteVersion(destination, model.Version,
            out _, location));
        CoverageTableCompiler.RequireWritten(MathView.TryWriteConstantsOffset(destination,
            model.Constants is null ? (ushort)0 : (ushort)constantsAt, out _, location));
        CoverageTableCompiler.RequireWritten(MathView.TryWriteGlyphInfoOffset(destination,
            model.GlyphInfo is null ? (ushort)0 : (ushort)glyphInfoAt, out _, location));
        CoverageTableCompiler.RequireWritten(MathView.TryWriteVariantsOffset(destination,
            model.Variants is null ? (ushort)0 : (ushort)variantsAt, out _, location));
        if (model.Constants is { } constants)
        {
            var slice = destination.Slice(constantsAt, MathLayout.ConstantsSize);
            WriteConstants(slice, constants, plan);
        }
        if (model.GlyphInfo is { } glyphInfo)
        {
            WriteGlyphInfo(glyphInfo, plan, destination.Slice(glyphInfoAt, GlyphInfoLength(plan)),
                glyphInfoAt);
        }
        // The glyph-info coverages close the plan (their group follows the variants
        // block so cross-subtree shared instances resolve forward).
        if (model.GlyphInfo is not null)
        {
            foreach (var coverage in plan.GlyphInfoCoverages)
                CoverageTableCompiler.RequireWritten(CoverageTableCompiler.TryWrite(coverage,
                    destination.Slice(plan.CoverageAt[coverage],
                        CoverageTableCompiler.Measure(coverage)), out _));
        }
        if (model.Variants is { } variants)
        {
            WriteVariants(variants, plan, destination.Slice(variantsAt, plan.Length - variantsAt),
                variantsAt);
        }
        foreach (var device in plan.Devices)
            StaticDeviceTableCompiler.WritePlanned(device,
                destination.Slice(plan.DeviceAt[device], StaticDeviceTableCompiler.Measure(device)));
    }

    private static void WriteConstants(Span<byte> slice, MathConstantsTable constants,
        Placement plan)
    {
        var location = constants.Location;
        CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteScriptPercentScaleDown(slice,
            constants.ScriptPercentScaleDown, out _, location));
        CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteScriptScriptPercentScaleDown(slice,
            constants.ScriptScriptPercentScaleDown, out _, location));
        CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteDelimitedSubFormulaMinHeight(slice,
            constants.DelimitedSubFormulaMinHeight, out _, location));
        CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteDisplayOperatorMinHeight(slice,
            constants.DisplayOperatorMinHeight, out _, location));
        for (int i = 0; i < constants.RecordCount; i++)
        {
            var record = constants.Values[i];
            var device = constants.Devices[i];
            ushort deviceOffset = device is null ? (ushort)0
                : (ushort)(plan.DeviceAt[plan.DeviceRep[device]] - MathLayout.Header10);
            WriteConstantRow(slice, i, record, deviceOffset, location);
        }
        CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalDegreeBottomRaisePercent(slice,
            constants.RadicalDegreeBottomRaisePercent, out _, location));
    }


    // One fixed constants record row: the generated per-record scalar writers own
    // the layout; this dispatch only pairs each record's value with its device slot.
    private static void WriteConstantRow(Span<byte> slice, int ordinal, MathValueRecord record,
        ushort deviceOffset, DataLocation location)
    {
        switch (ordinal)
        {
            case 0:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteMathLeadingValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteMathLeadingDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 1:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteAxisHeightValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteAxisHeightDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 2:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteAccentBaseHeightValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteAccentBaseHeightDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 3:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFlattenedAccentBaseHeightValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFlattenedAccentBaseHeightDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 4:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSubscriptShiftDownValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSubscriptShiftDownDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 5:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSubscriptTopMaxValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSubscriptTopMaxDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 6:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSubscriptBaselineDropMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSubscriptBaselineDropMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 7:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptShiftUpValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptShiftUpDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 8:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptShiftUpCrampedValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptShiftUpCrampedDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 9:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptBottomMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptBottomMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 10:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptBaselineDropMaxValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptBaselineDropMaxDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 11:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSubSuperscriptGapMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSubSuperscriptGapMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 12:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptBottomMaxWithSubscriptValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSuperscriptBottomMaxWithSubscriptDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 13:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSpaceAfterScriptValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSpaceAfterScriptDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 14:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUpperLimitGapMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUpperLimitGapMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 15:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUpperLimitBaselineRiseMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUpperLimitBaselineRiseMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 16:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteLowerLimitGapMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteLowerLimitGapMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 17:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteLowerLimitBaselineDropMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteLowerLimitBaselineDropMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 18:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackTopShiftUpValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackTopShiftUpDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 19:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackTopDisplayStyleShiftUpValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackTopDisplayStyleShiftUpDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 20:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackBottomShiftDownValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackBottomShiftDownDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 21:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackBottomDisplayStyleShiftDownValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackBottomDisplayStyleShiftDownDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 22:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackGapMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackGapMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 23:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackDisplayStyleGapMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStackDisplayStyleGapMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 24:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStretchStackTopShiftUpValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStretchStackTopShiftUpDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 25:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStretchStackBottomShiftDownValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStretchStackBottomShiftDownDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 26:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStretchStackGapAboveMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStretchStackGapAboveMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 27:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStretchStackGapBelowMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteStretchStackGapBelowMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 28:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionNumeratorShiftUpValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionNumeratorShiftUpDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 29:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionNumeratorDisplayStyleShiftUpValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionNumeratorDisplayStyleShiftUpDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 30:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionDenominatorShiftDownValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionDenominatorShiftDownDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 31:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionDenominatorDisplayStyleShiftDownValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionDenominatorDisplayStyleShiftDownDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 32:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionNumeratorGapMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionNumeratorGapMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 33:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionNumDisplayStyleGapMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionNumDisplayStyleGapMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 34:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionRuleThicknessValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionRuleThicknessDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 35:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionDenominatorGapMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionDenominatorGapMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 36:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionDenomDisplayStyleGapMinValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteFractionDenomDisplayStyleGapMinDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 37:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSkewedFractionHorizontalGapValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSkewedFractionHorizontalGapDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 38:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSkewedFractionVerticalGapValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteSkewedFractionVerticalGapDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 39:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteOverbarVerticalGapValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteOverbarVerticalGapDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 40:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteOverbarRuleThicknessValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteOverbarRuleThicknessDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 41:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteOverbarExtraAscenderValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteOverbarExtraAscenderDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 42:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUnderbarVerticalGapValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUnderbarVerticalGapDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 43:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUnderbarRuleThicknessValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUnderbarRuleThicknessDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 44:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUnderbarExtraDescenderValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteUnderbarExtraDescenderDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 45:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalVerticalGapValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalVerticalGapDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 46:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalDisplayStyleVerticalGapValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalDisplayStyleVerticalGapDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 47:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalRuleThicknessValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalRuleThicknessDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 48:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalExtraAscenderValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalExtraAscenderDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 49:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalKernBeforeDegreeValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalKernBeforeDegreeDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
            case 50:
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalKernAfterDegreeValue(slice,
                    record.Value, out _, location));
                CoverageTableCompiler.RequireWritten(MathConstantsView.TryWriteRadicalKernAfterDegreeDeviceOffset(slice,
                    deviceOffset, out _, location));
                return;
        }
    }

    private static void WriteGlyphInfo(MathGlyphInfoTable glyphInfo, Placement plan,
        Span<byte> slice, int glyphInfoAt)
    {
        var location = glyphInfo.Location;
        int italicsAt = MathGlyphInfoLayout.HeaderLength;
        int topAccentAt = italicsAt + FamilyBodyLength(plan.Italics);
        int kernInfoAt = topAccentAt + FamilyBodyLength(plan.TopAccent);
        CoverageTableCompiler.RequireWritten(MathGlyphInfoView.TryWriteItalicsCorrectionInfoOffset(slice,
            glyphInfo.ItalicsCorrectionInfo is null ? (ushort)0 : (ushort)italicsAt, out _, location));
        CoverageTableCompiler.RequireWritten(MathGlyphInfoView.TryWriteTopAccentAttachmentOffset(slice,
            glyphInfo.TopAccentAttachment is null ? (ushort)0 : (ushort)topAccentAt, out _, location));
        CoverageTableCompiler.RequireWritten(MathGlyphInfoView.TryWriteExtendedShapeCoverageOffset(slice,
            glyphInfo.ExtendedShapeCoverage is null ? (ushort)0
                : (ushort)(plan.CoverageAt[plan.GlyphInfoCoverageRep[glyphInfo.ExtendedShapeCoverage]] - glyphInfoAt),
            out _, location));
        CoverageTableCompiler.RequireWritten(MathGlyphInfoView.TryWriteKernInfoOffset(slice,
            glyphInfo.KernInfo is null ? (ushort)0 : (ushort)kernInfoAt, out _, location));
        if (glyphInfo.ItalicsCorrectionInfo is { } italics)
            WriteValueFamily(slice.Slice(italicsAt, FamilyBodyLength(italics)),
                italics.Records, italics.Devices, italics.Coverage, plan,
                glyphInfoAt + italicsAt, italics.Location,
                MathItalicsCorrectionInfoLayout.HeaderLength, italics: true);
        if (glyphInfo.TopAccentAttachment is { } topAccent)
            WriteValueFamily(slice.Slice(topAccentAt, FamilyBodyLength(topAccent)),
                topAccent.Records, topAccent.Devices, topAccent.Coverage, plan,
                glyphInfoAt + topAccentAt, topAccent.Location,
                MathTopAccentAttachmentLayout.HeaderLength, italics: false);
        if (glyphInfo.KernInfo is { } kernInfo)
        {
            var body = slice.Slice(kernInfoAt,
                MathKernInfoLayout.HeaderLength + MathLayout.KernInfoRecordSize * kernInfo.RecordCount);
            var kernLocation = kernInfo.Location;
            CoverageTableCompiler.RequireWritten(MathKernInfoView.TryWriteCoverageOffset(body,
                (ushort)(kernInfo.Coverage is null ? 0
                    : plan.CoverageAt[plan.GlyphInfoCoverageRep[kernInfo.Coverage]] - (glyphInfoAt + kernInfoAt)),
                out _, kernLocation));
            CoverageTableCompiler.RequireWritten(MathKernInfoView.TryWriteMathKernCount(body,
                (ushort)kernInfo.RecordCount, out _, kernLocation));
            for (int i = 0; i < kernInfo.RecordCount; i++)
            {
                var record = kernInfo.Records[i];
                CoverageTableCompiler.RequireWritten(MathKernInfoView.TryWriteMathKernInfoRecordAt(body,
                    (ulong)(MathKernInfoLayout.HeaderLength + i * MathLayout.KernInfoRecordSize),
                    new MathKernInfoView.MathKernInfoRecordData(i,
                        CornerOffset(record.TopRight, plan, glyphInfoAt + kernInfoAt),
                        CornerOffset(record.TopLeft, plan, glyphInfoAt + kernInfoAt),
                        CornerOffset(record.BottomRight, plan, glyphInfoAt + kernInfoAt),
                        CornerOffset(record.BottomLeft, plan, glyphInfoAt + kernInfoAt)),
                    out _, kernLocation));
            }
        }
        foreach (var kern in plan.Kerns)
            WriteKern(kern, plan, slice.Slice(plan.KernAt[kern] - glyphInfoAt, KernLength(kern)),
                plan.KernAt[kern]);
    }

    private static ushort CornerOffset(MathKernTable? corner, Placement plan, int kernInfoAt) =>
        corner is null ? (ushort)0 : (ushort)(plan.KernAt[plan.KernRep[corner]] - kernInfoAt);

    // One counted MathValueRecord array: the count word plus the generated row
    // writers, each record's device slot resolved through the plan.
    private static void WriteValueFamily(Span<byte> body,
        IReadOnlyList<MathValueRecord> records, IReadOnlyList<StaticDeviceTable?> devices,
        CoverageTable? coverage, Placement plan, int familyAt, DataLocation location,
        int headerLength, bool italics)
    {
        // The family's own coverage slot: zero is the absent coverage, the plan
        // position relative to this family's start otherwise.
        ushort coverageOffset = coverage is null ? (ushort)0
            : (ushort)(plan.CoverageAt[plan.GlyphInfoCoverageRep[coverage]] - familyAt);
        if (italics)
        {
            CoverageTableCompiler.RequireWritten(
                MathItalicsCorrectionInfoView.TryWriteCoverageOffset(body, coverageOffset,
                    out _, location));
        }
        else
        {
            CoverageTableCompiler.RequireWritten(
                MathTopAccentAttachmentView.TryWriteCoverageOffset(body, coverageOffset,
                    out _, location));
        }
        ushort count = (ushort)records.Count;
        if (italics)
        {
            CoverageTableCompiler.RequireWritten(
                MathItalicsCorrectionInfoView.TryWriteItalicsCorrectionCount(body, count,
                    out _, location));
            for (int i = 0; i < records.Count; i++)
            {
                CoverageTableCompiler.RequireWritten(MathItalicsCorrectionInfoView.TryWriteItalicsCorrectionAt(body,
                    (ulong)(headerLength + i * MathLayout.ValueRecordSize),
                    new MathItalicsCorrectionInfoView.ItalicsCorrectionData(i, records[i].Value,
                        DeviceOffset(devices[i], plan, familyAt)), out _, location));
            }
        }
        else
        {
            CoverageTableCompiler.RequireWritten(
                MathTopAccentAttachmentView.TryWriteTopAccentAttachmentCount(body, count,
                    out _, location));
            for (int i = 0; i < records.Count; i++)
            {
                CoverageTableCompiler.RequireWritten(MathTopAccentAttachmentView.TryWriteTopAccentAttachmentAt(body,
                    (ulong)(headerLength + i * MathLayout.ValueRecordSize),
                    new MathTopAccentAttachmentView.TopAccentAttachmentData(i, records[i].Value,
                        DeviceOffset(devices[i], plan, familyAt)), out _, location));
            }
        }
    }

    private static ushort DeviceOffset(StaticDeviceTable? device, Placement plan, int baseAt) =>
        device is null ? (ushort)0 : (ushort)(plan.DeviceAt[plan.DeviceRep[device]] - baseAt);

    private static void WriteKern(MathKernTable kern, Placement plan, Span<byte> body, int kernAt)
    {
        var location = kern.Location;
        CoverageTableCompiler.RequireWritten(MathKernView.TryWriteHeightCount(body,
            (ushort)kern.HeightCount, out _, location));
        for (int i = 0; i < kern.HeightCount; i++)
            CoverageTableCompiler.RequireWritten(MathKernView.TryWriteCorrectionHeightAt(body,
                (ulong)(MathKernLayout.CorrectionHeightsAt + i * MathLayout.ValueRecordSize),
                new MathKernView.CorrectionHeightData(i, kern.CorrectionHeights[i].Value,
                    DeviceOffset(kern.HeightDevices[i], plan, kernAt)), out _, location));
        int valuesAt = MathKernLayout.CorrectionHeightsAt +
            MathLayout.ValueRecordSize * kern.HeightCount;
        for (int i = 0; i < kern.KernValueCount; i++)
            CoverageTableCompiler.RequireWritten(MathKernView.TryWriteKernValueAt(body,
                (ulong)(valuesAt + i * MathLayout.ValueRecordSize),
                new MathKernView.KernValueData(i, kern.KernValues[i].Value,
                    DeviceOffset(kern.KernValueDevices[i], plan, kernAt)), out _, location));
    }

    private static void WriteVariants(MathVariantsTable variants, Placement plan,
        Span<byte> slice, int variantsAt)
    {
        var location = variants.Location;
        int headLength = MathVariantsLayout.ConstructionOffsetsAt +
            MathVariantsLayout.ConstructionOffsetStride *
            (variants.VertGlyphCount + variants.HorizGlyphCount);
        var head = slice.Slice(0, headLength);
        CoverageTableCompiler.RequireWritten(MathVariantsView.TryWriteMinConnectorOverlap(head,
            variants.MinConnectorOverlap, out _, location));
        CoverageTableCompiler.RequireWritten(MathVariantsView.TryWriteVertGlyphCoverageOffset(head,
            variants.VertGlyphCoverage is null ? (ushort)0
                : (ushort)(plan.CoverageAt[CoverageRep(plan, variants.VertGlyphCoverage)] - variantsAt),
            out _, location));
        CoverageTableCompiler.RequireWritten(MathVariantsView.TryWriteHorizGlyphCoverageOffset(head,
            variants.HorizGlyphCoverage is null ? (ushort)0
                : (ushort)(plan.CoverageAt[CoverageRep(plan, variants.HorizGlyphCoverage)] - variantsAt),
            out _, location));
        CoverageTableCompiler.RequireWritten(MathVariantsView.TryWriteVertGlyphCount(head,
            (ushort)variants.VertGlyphCount, out _, location));
        CoverageTableCompiler.RequireWritten(MathVariantsView.TryWriteHorizGlyphCount(head,
            (ushort)variants.HorizGlyphCount, out _, location));
        for (int i = 0; i < variants.VertGlyphCount; i++)
            CoverageTableCompiler.RequireWritten(MathVariantsView.TryWriteVertGlyphConstructionOffsetAt(head,
                (ulong)(MathVariantsLayout.ConstructionOffsetsAt +
                    i * MathVariantsLayout.ConstructionOffsetStride),
                new MathVariantsView.VertGlyphConstructionOffsetData(i,
                    (ushort)(plan.ConstructionAt[plan.ConstructionRep[variants.VertConstructions[i]]] - variantsAt)),
                out _, location));
        for (int i = 0; i < variants.HorizGlyphCount; i++)
            CoverageTableCompiler.RequireWritten(MathVariantsView.TryWriteHorizGlyphConstructionOffsetAt(head,
                (ulong)(MathVariantsLayout.ConstructionOffsetsAt +
                    MathVariantsLayout.ConstructionOffsetStride * variants.VertGlyphCount +
                    i * MathVariantsLayout.ConstructionOffsetStride),
                new MathVariantsView.HorizGlyphConstructionOffsetData(i,
                    (ushort)(plan.ConstructionAt[plan.ConstructionRep[variants.HorizConstructions[i]]] - variantsAt)),
                out _, location));
        foreach (var coverage in plan.VariantCoverages)
        {
            int at = plan.CoverageAt[coverage];
            CoverageTableCompiler.RequireWritten(CoverageTableCompiler.TryWrite(coverage,
                slice.Slice(at - variantsAt, CoverageTableCompiler.Measure(coverage)),
                out _));
        }
        foreach (var construction in plan.Constructions)
        {
            int at = plan.ConstructionAt[construction];
            var body = slice.Slice(at - variantsAt,
                MathGlyphConstructionLayout.HeaderLength +
                MathLayout.GlyphVariantRecordSize * construction.VariantCount);
            var constructionLocation = construction.Location;
            CoverageTableCompiler.RequireWritten(MathGlyphConstructionView.TryWriteGlyphAssemblyOffset(body,
                construction.Assembly is null ? (ushort)0
                    : (ushort)(plan.AssemblyAt[plan.AssemblyRep[construction.Assembly]] - at),
                out _, constructionLocation));
            CoverageTableCompiler.RequireWritten(MathGlyphConstructionView.TryWriteVariantCount(body,
                (ushort)construction.VariantCount, out _, constructionLocation));
            for (int i = 0; i < construction.VariantCount; i++)
            {
                var variant = construction.Variants[i];
                CoverageTableCompiler.RequireWritten(MathGlyphConstructionView.TryWriteMathGlyphVariantRecordAt(body,
                    (ulong)(MathGlyphConstructionLayout.VariantRecordsAt +
                        i * MathLayout.GlyphVariantRecordSize),
                    new MathGlyphConstructionView.MathGlyphVariantRecordData(i,
                        variant.VariantGlyph, variant.AdvanceMeasurement), out _,
                    constructionLocation));
            }
        }
        foreach (var assembly in plan.Assemblies)
        {
            int at = plan.AssemblyAt[assembly];
            var body = slice.Slice(at - variantsAt,
                MathGlyphAssemblyLayout.PartRecordsAt + MathLayout.GlyphPartSize * assembly.PartCount);
            var assemblyLocation = assembly.Location;
            var device = assembly.ItalicsCorrectionDevice;
            CoverageTableCompiler.RequireWritten(MathGlyphAssemblyView.TryWriteItalicsCorrectionValue(body,
                assembly.ItalicsCorrection.Value, out _, assemblyLocation));
            CoverageTableCompiler.RequireWritten(MathGlyphAssemblyView.TryWriteItalicsCorrectionDeviceOffset(body,
                device is null ? (ushort)0 : (ushort)(plan.DeviceAt[plan.DeviceRep[device]] - at),
                out _, assemblyLocation));
            CoverageTableCompiler.RequireWritten(MathGlyphAssemblyView.TryWritePartCount(body,
                (ushort)assembly.PartCount, out _, assemblyLocation));
            for (int i = 0; i < assembly.PartCount; i++)
            {
                var part = assembly.Parts[i];
                CoverageTableCompiler.RequireWritten(MathGlyphAssemblyView.TryWritePartRecordAt(body,
                    (ulong)(MathGlyphAssemblyLayout.PartRecordsAt + i * MathLayout.GlyphPartSize),
                    new MathGlyphAssemblyView.PartRecordData(i, part.GlyphId, part.StartConnectorLength,
                        part.EndConnectorLength, part.FullAdvance, part.PartFlags), out _,
                    assemblyLocation));
            }
        }
    }
}
