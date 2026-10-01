using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct CmapFormat0View
{
    /// <summary>Bounded local glyph entry read; positions are subtable-relative (6 + ordinal).</summary>
    public ReadStatus TryGetGlyphEntry(int ordinal, out GlyphEntryData entry, out FontDiagnostic diagnostic)
    {
        entry = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (ordinal < 0 || ordinal >= CmapFormat0Layout.GlyphCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, CmapFormat0Layout.HeaderLength, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), CmapFormat0Layout.GlyphCount);
        return TryReadGlyphEntryAt(CmapFormat0Layout.HeaderLength + (ulong)(uint)ordinal, ordinal,
            out entry, out diagnostic);
    }

    /// <summary>Strict format 0 validation: exact 262-byte subtable, the language rule and
    /// every nonzero glyph id against numGlyphs. The public form applies the strict
    /// non-Mac language rule; the table dispatch passes the Mac-only exemption.</summary>
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(requireZeroLanguage: true, numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(bool requireZeroLanguage, int numGlyphs, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (requireZeroLanguage && Language != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.LanguageId, 4), out diagnostic, Language, 0);
        if ((ulong)_data.Length != CmapFormat0Layout.SubtableLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, 2), out diagnostic, (ulong)_data.Length,
                CmapFormat0Layout.SubtableLength);
        if (!budget.Work(_data.Length + 2L * CmapFormat0Layout.GlyphCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < CmapFormat0Layout.GlyphCount; i++)
        {
            status = TryGetGlyphEntry(i, out var entry, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (entry.Value != 0 && entry.Value >= (uint)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.At(DataField.GlyphId, CmapFormat0Layout.HeaderLength + (ulong)(uint)i, i),
                    out diagnostic, entry.Value, (uint)numGlyphs);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
