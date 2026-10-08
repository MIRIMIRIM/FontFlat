using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One MathValueRecord: the FWORD value plus the raw Optional deviceOffset
/// (zero = the spec's "May be NULL"). The device body itself lives in the owner's
/// aligned device slots under the shared-identity rule.</summary>
public readonly record struct MathValueRecord(short Value, ushort DeviceOffset);

/// <summary>One GlyphPart row: the part glyph id (read-time numGlyphs proof), the
/// two connector lengths, the full advance (all UFWORD, unsigned) and the raw flags
/// word - EXTENDER_FLAG 0x0001 is the only defined bit and the 0xFFFE ridge is the
/// OQ-5 observation the model records without judging.</summary>
public readonly record struct GlyphPartRecord(ushort GlyphId, ushort StartConnectorLength,
    ushort EndConnectorLength, ushort FullAdvance, ushort PartFlags)
{
    /// <summary>The spec's only defined part-qualifier bit.</summary>
    public bool IsExtender => (PartFlags & MathLayout.PartExtenderFlag) != 0;
}

/// <summary>One MathGlyphVariantRecord row: the variant glyph id (read-time numGlyphs
/// proof) and the UFWORD advance measurement.</summary>
public readonly record struct MathGlyphVariantRecord(ushort VariantGlyph, ushort AdvanceMeasurement);

/// <summary>The fixed 56-field MathConstants body: the two signed script scale
/// percentages, the two unsigned minimum heights, the 51 value records in wire order
/// and the trailing signed radical degree percentage. The record order is the semantic
/// constant order and is never re-sorted; the per-record device slots keep model
/// identity under the shared-identity rule (a wire position materializes once).</summary>
public sealed class MathConstantsTable
{
    private readonly MathValueRecord[] _values;
    private readonly StaticDeviceTable?[] _devices;

    internal MathConstantsTable(short scriptPercentScaleDown, short scriptScriptPercentScaleDown,
        ushort delimitedSubFormulaMinHeight, ushort displayOperatorMinHeight,
        MathValueRecord[] values, StaticDeviceTable?[] devices, short radicalDegreeBottomRaisePercent,
        DataLocation location, int consumedLength)
    {
        ScriptPercentScaleDown = scriptPercentScaleDown;
        ScriptScriptPercentScaleDown = scriptScriptPercentScaleDown;
        DelimitedSubFormulaMinHeight = delimitedSubFormulaMinHeight;
        DisplayOperatorMinHeight = displayOperatorMinHeight;
        _values = values; _devices = devices;
        RadicalDegreeBottomRaisePercent = radicalDegreeBottomRaisePercent;
        Location = location; ConsumedLength = consumedLength;
    }

    public short ScriptPercentScaleDown { get; }
    public short ScriptScriptPercentScaleDown { get; }
    /// <summary>UFWORD read: unsigned, no value-domain check (the section 3 face).</summary>
    public ushort DelimitedSubFormulaMinHeight { get; }
    /// <summary>UFWORD read: unsigned, no value-domain check (the section 3 face).</summary>
    public ushort DisplayOperatorMinHeight { get; }
    /// <summary>The 51 value records in wire order (the semantic constant order).</summary>
    public IReadOnlyList<MathValueRecord> Values => Array.AsReadOnly(_values);
    /// <summary>The 51 device slots aligned with <see cref="Values"/>; null is the
    /// zero deviceOffset ("May be NULL"). Shared wire positions materialize once.</summary>
    public IReadOnlyList<StaticDeviceTable?> Devices => Array.AsReadOnly(_devices);
    public short RadicalDegreeBottomRaisePercent { get; }
    /// <summary>The fixed body is the whole constants leaf: the declared length.</summary>
    public int RecordCount => _values.Length;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>Atomically moves one of the 51 value records (the card's constant
    /// edit witness). The position is the wire-order ordinal (the semantic constant
    /// order); the replacement value answers the FWORD domain. The fixed layout has
    /// no structural face: record additions or removals stay outside the surface
    /// (OQ-9); consumers re-root through MathTableCompiler.TryCreate.</summary>
    public ReadStatus TryWithConstant(int ordinal, short value,
        out MathConstantsTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if ((uint)ordinal >= (uint)_values.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload,
                    (ulong)(MathLayout.ConstantsValueRecordsAt +
                        ordinal * MathLayout.ValueRecordSize), ordinal),
                out diagnostic, unchecked((ulong)ordinal), (ulong)_values.Length);
        var values = (MathValueRecord[])_values.Clone();
        values[ordinal] = new(value, values[ordinal].DeviceOffset);
        updated = new(ScriptPercentScaleDown, ScriptScriptPercentScaleDown,
            DelimitedSubFormulaMinHeight, DisplayOperatorMinHeight, values,
            (StaticDeviceTable?[])_devices.Clone(), RadicalDegreeBottomRaisePercent,
            Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>One MathItalicsCorrectionInfo: the Optional Coverage and the counted
/// MathValueRecord array with aligned device slots. The count/coverage coincidence is
/// the spec's "Should" modality - the observation the consumer reads from
/// <see cref="Coverage"/> against <see cref="Records"/>, never a refusal here.</summary>
public sealed class MathItalicsCorrectionInfoTable
{
    private readonly MathValueRecord[] _records;
    private readonly StaticDeviceTable?[] _devices;

    internal MathItalicsCorrectionInfoTable(CoverageTable? coverage, MathValueRecord[] records,
        StaticDeviceTable?[] devices, DataLocation location, int consumedLength)
    {
        Coverage = coverage; _records = records; _devices = devices;
        Location = location; ConsumedLength = consumedLength;
    }

    public CoverageTable? Coverage { get; }
    public int RecordCount => _records.Length;
    public IReadOnlyList<MathValueRecord> Records => Array.AsReadOnly(_records);
    public IReadOnlyList<StaticDeviceTable?> Devices => Array.AsReadOnly(_devices);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One MathTopAccentAttachment: the MathItalicsCorrectionInfo wire twin whose
/// count/coverage coincidence carries the spec's "Must" modality - enforced as the
/// OQ-6 hard face at the read chain, so a constructed model always satisfies it.</summary>
public sealed class MathTopAccentAttachmentTable
{
    private readonly MathValueRecord[] _records;
    private readonly StaticDeviceTable?[] _devices;

    internal MathTopAccentAttachmentTable(CoverageTable? coverage, MathValueRecord[] records,
        StaticDeviceTable?[] devices, DataLocation location, int consumedLength)
    {
        Coverage = coverage; _records = records; _devices = devices;
        Location = location; ConsumedLength = consumedLength;
    }

    public CoverageTable? Coverage { get; }
    public int RecordCount => _records.Length;
    public IReadOnlyList<MathValueRecord> Records => Array.AsReadOnly(_records);
    public IReadOnlyList<StaticDeviceTable?> Devices => Array.AsReadOnly(_devices);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One MathKernInfoRecord row: the four corner references resolved to the
/// shared kern bodies (a null corner is the spec's per-slot "May be NULL" - the kerning
/// amount of zero the spec itself states). Repeated corner offsets materialize one
/// shared instance (the BASE R7 identity rule).</summary>
public readonly record struct MathKernInfoRecord(MathKernTable? TopRight, MathKernTable? TopLeft,
    MathKernTable? BottomRight, MathKernTable? BottomLeft);

/// <summary>One MathKernInfo: the Optional Coverage over the stride-8 record array.
/// The count==coverage coincidence is the OQ-6 hard face at the read chain.</summary>
public sealed class MathKernInfoTable
{
    private readonly MathKernInfoRecord[] _records;

    internal MathKernInfoTable(CoverageTable? coverage, MathKernInfoRecord[] records,
        DataLocation location, int consumedLength)
    {
        Coverage = coverage; _records = records;
        Location = location; ConsumedLength = consumedLength;
    }

    public CoverageTable? Coverage { get; }
    public int RecordCount => _records.Length;
    public IReadOnlyList<MathKernInfoRecord> Records => Array.AsReadOnly(_records);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One MathKern body: the correction heights and the derived kernValues array
/// (length = heightCount + 1, OQ-3's computed count) with aligned per-record device
/// slots. The heights' increasing order is the spec's "sorted" wording - the OQ-5
/// family observation the model records without judging; the wire order is kept.</summary>
public sealed class MathKernTable
{
    private readonly MathValueRecord[] _heights;
    private readonly StaticDeviceTable?[] _heightDevices;
    private readonly MathValueRecord[] _kernValues;
    private readonly StaticDeviceTable?[] _kernValueDevices;

    internal MathKernTable(MathValueRecord[] heights, StaticDeviceTable?[] heightDevices,
        MathValueRecord[] kernValues, StaticDeviceTable?[] kernValueDevices,
        DataLocation location, int consumedLength)
    {
        _heights = heights; _heightDevices = heightDevices;
        _kernValues = kernValues; _kernValueDevices = kernValueDevices;
        Location = location; ConsumedLength = consumedLength;
    }

    /// <summary>The raw heightCount: the correction heights' length.</summary>
    public int HeightCount => _heights.Length;
    public IReadOnlyList<MathValueRecord> CorrectionHeights => Array.AsReadOnly(_heights);
    public IReadOnlyList<StaticDeviceTable?> HeightDevices => Array.AsReadOnly(_heightDevices);
    /// <summary>The derived kernValues length is <see cref="HeightCount"/> + 1 by
    /// declaration, so this array is never empty.</summary>
    public int KernValueCount => _kernValues.Length;
    public IReadOnlyList<MathValueRecord> KernValues => Array.AsReadOnly(_kernValues);
    public IReadOnlyList<StaticDeviceTable?> KernValueDevices => Array.AsReadOnly(_kernValueDevices);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One MathGlyphConstruction: the Optional assembly slot over the counted
/// variant record array (coverage order is the semantic order, never re-sorted).
/// A zero variantCount with no assembly is the OQ-7 legal positive (Partial3).</summary>
public sealed class MathGlyphConstructionTable
{
    private readonly MathGlyphVariantRecord[] _variants;

    internal MathGlyphConstructionTable(MathGlyphAssemblyTable? assembly,
        MathGlyphVariantRecord[] variants, DataLocation location, int consumedLength)
    {
        Assembly = assembly; _variants = variants;
        Location = location; ConsumedLength = consumedLength;
    }

    public MathGlyphAssemblyTable? Assembly { get; }
    public int VariantCount => _variants.Length;
    public IReadOnlyList<MathGlyphVariantRecord> Variants => Array.AsReadOnly(_variants);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>Atomically moves one variant record (the card's construction edit
    /// witness). The position is the wire-order ordinal; the replacement glyph
    /// answers the caller's explicit numGlyphs authority here and is re-answered
    /// again by the root factory on re-rooting; the advance answers the UFWORD
    /// domain. Structural changes - variant additions or removals, reordering -
    /// stay outside the surface (OQ-9); consumers re-root through
    /// MathTableCompiler.TryCreate.</summary>
    public ReadStatus TryWithVariant(int position, ushort variantGlyph, ushort advance,
        int numGlyphs, out MathGlyphConstructionTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if ((uint)position >= (uint)_variants.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId,
                    (ulong)(MathGlyphConstructionLayout.VariantRecordsAt +
                        position * MathLayout.GlyphVariantRecordSize), position),
                out diagnostic, unchecked((ulong)position), (ulong)_variants.Length);
        if (variantGlyph >= (ushort)numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId,
                    (ulong)(MathGlyphConstructionLayout.VariantRecordsAt +
                        position * MathLayout.GlyphVariantRecordSize), position),
                out diagnostic, variantGlyph, (ulong)numGlyphs);
        var variants = (MathGlyphVariantRecord[])_variants.Clone();
        variants[position] = new(variantGlyph, advance);
        updated = new(Assembly, variants, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>One GlyphAssembly: the italics correction record (with its Optional device
/// slot) over the counted GlyphPart array. partCount=0 is the OQ-7 legal positive
/// (Partial4); a non-empty parts list with no extender flag is the OQ-8 observation -
/// the assembly algorithm's termination concern, not a structural face.</summary>
public sealed class MathGlyphAssemblyTable
{
    private readonly GlyphPartRecord[] _parts;

    internal MathGlyphAssemblyTable(MathValueRecord italicsCorrection,
        StaticDeviceTable? italicsCorrectionDevice, GlyphPartRecord[] parts,
        DataLocation location, int consumedLength)
    {
        ItalicsCorrection = italicsCorrection;
        ItalicsCorrectionDevice = italicsCorrectionDevice;
        _parts = parts;
        Location = location; ConsumedLength = consumedLength;
    }

    public MathValueRecord ItalicsCorrection { get; }
    /// <summary>null is the zero deviceOffset ("May be NULL").</summary>
    public StaticDeviceTable? ItalicsCorrectionDevice { get; }
    public int PartCount => _parts.Length;
    public IReadOnlyList<GlyphPartRecord> Parts => Array.AsReadOnly(_parts);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    private ReadStatus TryWithPart(int position, GlyphPartRecord part, out GlyphPartRecord[] parts,
        out FontDiagnostic diagnostic)
    {
        parts = Array.Empty<GlyphPartRecord>();
        if ((uint)position >= (uint)_parts.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId,
                    (ulong)(MathGlyphAssemblyLayout.PartRecordsAt +
                        position * MathLayout.GlyphPartSize), position),
                out diagnostic, unchecked((ulong)position), (ulong)_parts.Length);
        var clone = (GlyphPartRecord[])_parts.Clone();
        clone[position] = part;
        parts = clone;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one part glyph id (the card's assembly edit
    /// witness). The position is the wire-order ordinal; the replacement id answers
    /// the caller's explicit numGlyphs authority here and is re-answered again by
    /// the root factory on re-rooting. Structural changes - part additions,
    /// removals or reordering - stay outside the surface (OQ-9); consumers re-root
    /// through MathTableCompiler.TryCreate.</summary>
    public ReadStatus TryWithPartGlyph(int position, ushort glyphId, int numGlyphs,
        out MathGlyphAssemblyTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if ((uint)position >= (uint)_parts.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId,
                    (ulong)(MathGlyphAssemblyLayout.PartRecordsAt +
                        position * MathLayout.GlyphPartSize), position),
                out diagnostic, unchecked((ulong)position), (ulong)_parts.Length);
        if (glyphId >= (ushort)numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId,
                    (ulong)(MathGlyphAssemblyLayout.PartRecordsAt +
                        position * MathLayout.GlyphPartSize), position),
                out diagnostic, glyphId, (ulong)numGlyphs);
        ReadStatus status = TryWithPart(position,
            new GlyphPartRecord(glyphId, _parts[position].StartConnectorLength,
                _parts[position].EndConnectorLength, _parts[position].FullAdvance,
                _parts[position].PartFlags), out var parts, out diagnostic);
        if (status != ReadStatus.Done) return status;
        updated = new(ItalicsCorrection, ItalicsCorrectionDevice, parts, Location,
            ConsumedLength);
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one part flags word. The reserved ridge 0xFFFE is
    /// the OQ-5 observation - the raw word is recorded, never judged, so any value
    /// within the u16 domain lands.</summary>
    public ReadStatus TryWithPartFlags(int position, ushort partFlags,
        out MathGlyphAssemblyTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = TryWithPart(position,
            new GlyphPartRecord(_parts[position].GlyphId, _parts[position].StartConnectorLength,
                _parts[position].EndConnectorLength, _parts[position].FullAdvance,
                partFlags), out var parts, out diagnostic);
        if (status != ReadStatus.Done) return status;
        updated = new(ItalicsCorrection, ItalicsCorrectionDevice, parts, Location,
            ConsumedLength);
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one part advance measurement (the UFWORD domain).</summary>
    public ReadStatus TryWithPartAdvance(int position, ushort fullAdvance,
        out MathGlyphAssemblyTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = TryWithPart(position,
            new GlyphPartRecord(_parts[position].GlyphId, _parts[position].StartConnectorLength,
                _parts[position].EndConnectorLength, fullAdvance,
                _parts[position].PartFlags), out var parts, out diagnostic);
        if (status != ReadStatus.Done) return status;
        updated = new(ItalicsCorrection, ItalicsCorrectionDevice, parts, Location,
            ConsumedLength);
        return ReadStatus.Done;
    }
}

/// <summary>One MathVariants: the minConnectorOverlap word, the two Optional coverages,
/// the two counts and the two construction arrays in coverage order. The count==
/// coverage coincidence on both directions is the OQ-6 hard face at the read chain;
/// a count=0 direction keeps both the NULL-coverage and the present-empty-coverage
/// wire forms legal (OQ-7, Partial1 vs 8d9c4b19).</summary>
public sealed class MathVariantsTable
{
    private readonly MathGlyphConstructionTable[] _vertConstructions;
    private readonly MathGlyphConstructionTable[] _horizConstructions;

    internal MathVariantsTable(ushort minConnectorOverlap, CoverageTable? vertGlyphCoverage,
        CoverageTable? horizGlyphCoverage, MathGlyphConstructionTable[] vertConstructions,
        MathGlyphConstructionTable[] horizConstructions, DataLocation location, int consumedLength)
    {
        MinConnectorOverlap = minConnectorOverlap;
        VertGlyphCoverage = vertGlyphCoverage;
        HorizGlyphCoverage = horizGlyphCoverage;
        _vertConstructions = vertConstructions;
        _horizConstructions = horizConstructions;
        Location = location; ConsumedLength = consumedLength;
    }

    public ushort MinConnectorOverlap { get; }
    public CoverageTable? VertGlyphCoverage { get; }
    public CoverageTable? HorizGlyphCoverage { get; }
    public int VertGlyphCount => _vertConstructions.Length;
    public int HorizGlyphCount => _horizConstructions.Length;
    public IReadOnlyList<MathGlyphConstructionTable> VertConstructions => Array.AsReadOnly(_vertConstructions);
    public IReadOnlyList<MathGlyphConstructionTable> HorizConstructions => Array.AsReadOnly(_horizConstructions);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One MathGlyphInfo: the four per-glyph family slots, each the spec's
/// presence-or-absence pointer (the OQ-1 lean: a null slot means that family's
/// information is absent - zero corrections, no extended shapes). DejaVuSans's
/// present-but-all-null table is the real shipping form of the all-null positive.</summary>
public sealed class MathGlyphInfoTable
{
    internal MathGlyphInfoTable(MathItalicsCorrectionInfoTable? italicsCorrectionInfo,
        MathTopAccentAttachmentTable? topAccentAttachment, CoverageTable? extendedShapeCoverage,
        MathKernInfoTable? kernInfo, DataLocation location, int consumedLength)
    {
        ItalicsCorrectionInfo = italicsCorrectionInfo;
        TopAccentAttachment = topAccentAttachment;
        ExtendedShapeCoverage = extendedShapeCoverage;
        KernInfo = kernInfo;
        Location = location; ConsumedLength = consumedLength;
    }

    public MathItalicsCorrectionInfoTable? ItalicsCorrectionInfo { get; }
    public MathTopAccentAttachmentTable? TopAccentAttachment { get; }
    /// <summary>The shared extended-shapes Coverage (reuse chain; T4-01 contract).</summary>
    public CoverageTable? ExtendedShapeCoverage { get; }
    public MathKernInfoTable? KernInfo { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>Owned whole-MATH slice (version 1.0): the constants body, the glyph-info
/// four-family subtree, the variants subtree (constructions, assemblies and parts under
/// the shared-identity rule - a wire position materializes once and keeps model
/// identity for Coverage, MathKern, Construction, Assembly and Device blocks) and the
/// consumed accounting over the whole-tree DFS (the BASE R9/JSTF R convention: the max
/// end over every walked block). numGlyphs is the explicit authority input the three
/// GID domains (variant glyph, part glyph, every reused coverage) were answered with.
/// Math layout application - variant selection, assembly overlap, kern lookups, device
/// delta application - stays outside this model (the T0 Operations ban).</summary>
public sealed class MathTable
{
    internal MathTable(uint version, int numGlyphs,
        ushort constantsOffset, ushort glyphInfoOffset, ushort variantsOffset,
        MathConstantsTable? constants, MathGlyphInfoTable? glyphInfo, MathVariantsTable? variants,
        DataLocation location, int consumedLength)
    {
        Version = version; NumGlyphs = numGlyphs;
        ConstantsOffset = constantsOffset; GlyphInfoOffset = glyphInfoOffset;
        VariantsOffset = variantsOffset;
        Constants = constants; GlyphInfo = glyphInfo; Variants = variants;
        Location = location; ConsumedLength = consumedLength;
    }

    public uint Version { get; }
    /// <summary>The explicit numGlyphs input every GID domain was answered with.</summary>
    public int NumGlyphs { get; }
    /// <summary>The raw root pointer words as read (zero = the absent head).</summary>
    public ushort ConstantsOffset { get; }
    public ushort GlyphInfoOffset { get; }
    public ushort VariantsOffset { get; }
    public MathConstantsTable? Constants { get; }
    public MathGlyphInfoTable? GlyphInfo { get; }
    public MathVariantsTable? Variants { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}
