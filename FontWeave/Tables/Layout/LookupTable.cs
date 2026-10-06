using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public enum LayoutLookupKind : byte { Gsub, Gpos }
public enum EffectiveMarkFilter : byte { None, AttachmentClass, MarkSet, IgnoreMarks }

/// <summary>Ordered Lookup metadata and, when explicitly decoded, supported typed leaves.
/// Null typed children means metadata-only; an empty typed collection is a valid no-op.</summary>
public sealed class LookupTable
{
    private readonly ushort[] _sourceOffsets;

    internal LookupTable(LayoutLookupKind kind, int numGlyphs, ushort lookupType, ushort flags,
        ushort? markFilteringSet, ushort[] sourceOffsets, SingleSubstTable[]? singleSubstitutions,
        DataLocation location, SinglePosTable[]? singlePositions = null,
        MultipleSubstTable[]? multiples = null, AlternateSubstTable[]? alternates = null,
        LigatureSubstTable[]? ligatures = null, ExtensionSubstTable[]? extensions = null)
    {
        Kind = kind;
        NumGlyphs = numGlyphs;
        LookupType = lookupType;
        Flags = flags;
        MarkFilteringSet = markFilteringSet;
        _sourceOffsets = sourceOffsets;
        SingleSubstitutions = singleSubstitutions is null ? null : Array.AsReadOnly(singleSubstitutions);
        SinglePositions = singlePositions is null ? null : Array.AsReadOnly(singlePositions);
        Multiples = multiples is null ? null : Array.AsReadOnly(multiples);
        Alternates = alternates is null ? null : Array.AsReadOnly(alternates);
        Ligatures = ligatures is null ? null : Array.AsReadOnly(ligatures);
        Extensions = extensions is null ? null : Array.AsReadOnly(extensions);
        Location = location;
    }

    public LayoutLookupKind Kind { get; }
    public int NumGlyphs { get; }
    public ushort LookupType { get; }
    public ushort Flags { get; }
    public ushort? MarkFilteringSet { get; }
    public int SubtableCount => SingleSubstitutions?.Count ?? SinglePositions?.Count ?? Multiples?.Count
        ?? Alternates?.Count ?? Ligatures?.Count ?? Extensions?.Count ?? _sourceOffsets.Length;
    public DataLocation Location { get; }
    public DataLocation SourceLocation => Location;
    public ReadOnlySpan<ushort> SourceSubtableOffsets => _sourceOffsets;
    public IReadOnlyList<SingleSubstTable>? SingleSubstitutions { get; }
    public IReadOnlyList<SinglePosTable>? SinglePositions { get; }
    public IReadOnlyList<MultipleSubstTable>? Multiples { get; }
    public IReadOnlyList<AlternateSubstTable>? Alternates { get; }
    public IReadOnlyList<LigatureSubstTable>? Ligatures { get; }
    public IReadOnlyList<ExtensionSubstTable>? Extensions { get; }

    /// <summary>The kind-aware typed read support set (T4-03 section 3.3): GSUB decodes
    /// types 1/2/3/4/7, GPOS stays type 1 (T4-05). Every other value keeps the raw
    /// observe/typed-refuse layering with byte-identical diagnostics.</summary>
    internal static bool IsTypedSupported(LayoutLookupKind kind, ushort lookupType) =>
        kind == LayoutLookupKind.Gsub
            ? lookupType is 1 or 2 or 3 or 4 or 7
            : lookupType == 1;
    public EffectiveMarkFilter EffectiveMarkFilter => (Flags & LookupLayout.IgnoreMarks) != 0
        ? EffectiveMarkFilter.IgnoreMarks : (Flags & LookupLayout.UseMarkFilteringSet) != 0
            ? EffectiveMarkFilter.MarkSet : (Flags & LookupLayout.AttachmentMask) != 0
                ? EffectiveMarkFilter.AttachmentClass : EffectiveMarkFilter.None;

    public ReadStatus ValidateFlags(GdefLookupContext? context, out FontDiagnostic diagnostic) =>
        ValidateFlags(NumGlyphs, Flags, MarkFilteringSet, SubtableCount, context, Location, out diagnostic);

    // One flags contract body shared by the owned model and the view-level typed
    // validation (T4-03 section 3); every diagnostic coordinate derives from location.
    internal static ReadStatus ValidateFlags(int numGlyphs, ushort flags, ushort? markFilteringSet,
        int subtableCount, GdefLookupContext? context, DataLocation location, out FontDiagnostic diagnostic)
    {
        if ((flags & LookupLayout.ReservedMask) != 0 ||
            ((flags & LookupLayout.UseMarkFilteringSet) != 0) != markFilteringSet.HasValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, LookupLayout.FlagsAt), out diagnostic, flags, 0xFF1F);
        // Cross-table identity: GDEF and GSUB/GPOS tags are deliberately different.
        // Source 0 is caller-owned, not a wildcard for a known source or another face.
        if (context is not null && (context.NumGlyphs != numGlyphs ||
            context.Location.SourceId != location.SourceId || context.Location.FaceIndex != location.FaceIndex))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0), out diagnostic);
        if ((flags & LookupLayout.IgnoreMask) != 0 && context?.GlyphClasses is null ||
            (flags & LookupLayout.AttachmentMask) != 0 && context?.MarkAttachmentClasses is null ||
            (flags & LookupLayout.UseMarkFilteringSet) != 0 &&
                (context is null || context.Version < GdefLayout.Version12 || context.MarkGlyphSets is null))
            return ReadWire.Fail(ReadStatus.MissingTable, DiagnosticCode.TableNotFound,
                location.AtLocal(DataField.Flags, LookupLayout.FlagsAt), out diagnostic);
        if (markFilteringSet is ushort set && set >= context!.MarkGlyphSets!.Sets.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.AtLocal(DataField.Count, LookupLayout.HeaderLength +
                    (ulong)subtableCount * LookupLayout.OffsetStride),
                out diagnostic, set, (ulong)context.MarkGlyphSets.Sets.Count);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus ValidateFlags(GdefLookupContext? context, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateFlags(context, out diagnostic);
    }

    // A known source binds this typed consumer to GSUB. Anonymous caller-owned
    // models keep their existing tag-0 contract; GDEF context tags are unrelated.
    internal static ReadStatus ValidateGsubSource(DataLocation location, out FontDiagnostic diagnostic)
    {
        if (location.SourceId != 0 && location.TableTag != SingleSubstLayout.Tag)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0), out diagnostic, location.TableTag, SingleSubstLayout.Tag);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal LookupTable WithDecodedSubtables(SingleSubstTable[] subtables) =>
        new(Kind, NumGlyphs, LookupType, Flags, MarkFilteringSet, _sourceOffsets, subtables, Location);

    internal LookupTable WithDecodedPositions(SinglePosTable[] subtables) =>
        new(Kind, NumGlyphs, LookupType, Flags, MarkFilteringSet, _sourceOffsets, null, Location, subtables);

    internal LookupTable WithDecodedMultiples(MultipleSubstTable[] subtables) =>
        new(Kind, NumGlyphs, LookupType, Flags, MarkFilteringSet, _sourceOffsets, null, Location,
            multiples: subtables);

    internal LookupTable WithDecodedAlternates(AlternateSubstTable[] subtables) =>
        new(Kind, NumGlyphs, LookupType, Flags, MarkFilteringSet, _sourceOffsets, null, Location,
            alternates: subtables);

    internal LookupTable WithDecodedLigatures(LigatureSubstTable[] subtables) =>
        new(Kind, NumGlyphs, LookupType, Flags, MarkFilteringSet, _sourceOffsets, null, Location,
            ligatures: subtables);

    internal LookupTable WithDecodedExtensions(ExtensionSubstTable[] subtables) =>
        new(Kind, NumGlyphs, LookupType, Flags, MarkFilteringSet, _sourceOffsets, null, Location,
            extensions: subtables);

    internal static ReadStatus ValidateGposSource(DataLocation location, out FontDiagnostic diagnostic)
    {
        if (location.SourceId != 0 && location.TableTag != SinglePosLayout.Tag)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0), out diagnostic, location.TableTag, SinglePosLayout.Tag);
        diagnostic = default; return ReadStatus.Done;
    }

    public ReadStatus TryWithSinglePositions(IReadOnlyList<SinglePosTable> subtables,
        GdefLookupContext? context, out LookupTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        if (Kind != LayoutLookupKind.Gpos || LookupType != 1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, LookupType);
        return LookupTableCompiler.TryCreateSinglePos(NumGlyphs, subtables, Flags, MarkFilteringSet,
            context, out updated, out diagnostic, limits, Location);
    }

    public ReadStatus TryWithSingleSubstitutions(IReadOnlyList<SingleSubstTable> subtables,
        GdefLookupContext? context, out LookupTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        if (Kind != LayoutLookupKind.Gsub || LookupType != 1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, LookupType);
        return LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, subtables,
            Flags, MarkFilteringSet, context, out updated, out diagnostic, limits, Location);
    }
}
