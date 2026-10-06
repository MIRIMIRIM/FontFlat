using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct CmapFormat8View
{
    /// <summary>Bounded is32 query: whether the 16-bit value <paramref name="wordIndex"/> is
    /// marked as the first half of a 32-bit code. The bitmap is fixed-header territory, so
    /// IsBound alone guarantees the 8192 bytes are present.</summary>
    public ReadStatus TryGetIs32(int wordIndex, out bool is32, out FontDiagnostic diagnostic)
    {
        is32 = false;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        // The sign check short-circuits before the uint conversion: checked builds must not
        // execute a negative int -> uint conversion.
        if (wordIndex < 0 || (uint)wordIndex > 0xFFFFu)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.CharCode, CmapFormat8Layout.Is32Offset, wordIndex), out diagnostic,
                unchecked((ulong)(long)wordIndex), 0x10000);
        is32 = CmapFormat8Layout.GetIs32Bit(
            _data.Slice(CmapFormat8Layout.Is32Offset, CmapFormat8Layout.Is32Length), (uint)wordIndex);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Strict format 8 validation: reserved/language rules, exact length, ordered
    /// non-overlapping scalar-bounded groups with linear GID bounds, and the three-branch is32
    /// consistency rule: a non-BMP group's start high half must be declared a first half, a
    /// BMP group's high half (0) must not, and a group spanning 0xFFFF is a structural
    /// contradiction. Other bitmap bits are proactive declarations and never asserted.</summary>
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
        ulong expected = CmapFormat8Layout.HeaderLength + 12UL * NumGroups;
        if ((ulong)_data.Length != expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.AtLocal(DataField.Length, 4), out diagnostic, (ulong)_data.Length, expected);
        if (!budget.Work(_data.Length + 32L * NumGroups, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ulong previousEnd = 0;
        bool first = true;
        for (int i = 0; (uint)i < NumGroups; i++)
        {
            status = TryGetGroup(i, out var group, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var location = Location.AtLocal(DataField.CharCode,
                CmapFormat8Layout.HeaderLength + 12UL * (uint)i, i);
            if (group.StartCharCode > group.EndCharCode || group.EndCharCode > 0x10FFFF ||
                (!first && group.StartCharCode <= previousEnd))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    location, out diagnostic, group.StartCharCode, previousEnd);
            if ((ulong)group.StartGlyphID + (group.EndCharCode - group.StartCharCode) >= (ulong)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId,
                        CmapFormat8Layout.HeaderLength + 12UL * (uint)i + 8, i),
                    out diagnostic, group.StartGlyphID, (ulong)numGlyphs);
            if (group.StartCharCode > 0xFFFF)
            {
                status = TryGetIs32((int)(group.StartCharCode >> 16), out bool isStart, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (!isStart)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location, out diagnostic, group.StartCharCode >> 16, 1);
            }
            else if (group.EndCharCode <= 0xFFFF)
            {
                // Every code in a BMP group has high half 0, so the whole branch reduces to
                // the single occurring high value: is32(0) must not declare a first half.
                status = TryGetIs32(0, out bool isZero, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (isZero)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location, out diagnostic, 0, 0);
            }
            else
            {
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location, out diagnostic, group.EndCharCode, 0xFFFF);
            }
            previousEnd = group.EndCharCode;
            first = false;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
