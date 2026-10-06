using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct CmapFormat6View
{
    /// <summary>Strict format 6 validation: exact length, window inside the code space, the
    /// language rule, empty tables allowed and every nonzero glyph id against numGlyphs.
    /// The public form applies the strict non-Mac language rule; the table dispatch passes
    /// the Mac-only exemption.</summary>
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
                Location.AtLocal(DataField.LanguageId, 4), out diagnostic, Language, 0);
        ulong expected = CmapFormat6Layout.HeaderLength + 2UL * EntryCount;
        if ((ulong)_data.Length != expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.AtLocal(DataField.Length, 2), out diagnostic, (ulong)_data.Length, expected);
        if (FirstCode + EntryCount > 0x10000)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.CharCode, 6), out diagnostic,
                (uint)FirstCode + EntryCount, 0x10000);
        if (!budget.Work(_data.Length + 2L * EntryCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; (uint)i < EntryCount; i++)
        {
            status = TryGetGlyphEntry(i, out var entry, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (entry.Value != 0 && entry.Value >= (uint)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId, CmapFormat6Layout.HeaderLength + 2UL * (uint)i, i),
                    out diagnostic, entry.Value, (uint)numGlyphs);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
