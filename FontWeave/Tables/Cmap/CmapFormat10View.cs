using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct CmapFormat10View
{
    /// <summary>Bounded local glyph entry read; positions are subtable-relative (20 + 2*ordinal).</summary>
    public ReadStatus TryGetGlyphEntry(int ordinal, out GlyphEntryData entry, out FontDiagnostic diagnostic)
    {
        entry = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (ordinal < 0 || (uint)ordinal >= NumChars)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 16, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), NumChars);
        return TryReadGlyphEntryAt(CmapFormat10Layout.HeaderLength + 2UL * (uint)ordinal, ordinal,
            out entry, out diagnostic);
    }

    /// <summary>Strict format 10 validation: reserved/language rules, exact length, empty tables
    /// allowed, the window [startCharCode, startCharCode + numChars) inside the scalar domain
    /// and disjoint from the surrogate block, and every nonzero glyph id against numGlyphs.
    /// The public form applies the strict non-Mac language rule; the table dispatch passes the
    /// Mac-only exemption.</summary>
    public ReadStatus Validate(bool requireZeroLanguage, int numGlyphs, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(requireZeroLanguage, numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(bool requireZeroLanguage, int numGlyphs, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (Reserved != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 2), out diagnostic, Reserved, 0);
        if (requireZeroLanguage && Language != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.LanguageId, 8), out diagnostic, Language, 0);
        ulong expected = CmapFormat10Layout.HeaderLength + 2UL * NumChars;
        if ((ulong)_data.Length != expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.AtLocal(DataField.Length, 4), out diagnostic, (ulong)_data.Length, expected);
        // Window-endpoint arithmetic in ulong: start and numChars are independent uint32.
        if (NumChars > 0)
        {
            ulong last = (ulong)StartCharCode + NumChars - 1;
            if (last > 0x10FFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    Location.AtLocal(DataField.CharCode, 12), out diagnostic, last, 0x10FFFF);
            if (StartCharCode <= 0xDFFF && last >= 0xD800)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    Location.AtLocal(DataField.CharCode, 12), out diagnostic, StartCharCode, 0xD800);
        }
        if (!budget.Work(_data.Length + 2L * NumChars, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; (uint)i < NumChars; i++)
        {
            status = TryGetGlyphEntry(i, out var entry, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (entry.Value != 0 && entry.Value >= (uint)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId, CmapFormat10Layout.HeaderLength + 2UL * (uint)i, i),
                    out diagnostic, entry.Value, (uint)numGlyphs);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
