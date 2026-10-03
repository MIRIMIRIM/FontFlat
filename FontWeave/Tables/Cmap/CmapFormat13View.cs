using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct CmapFormat13View
{
    /// <summary>Bounded local group read; ordering and glyph bounds are checked by Validate.</summary>
    public ReadStatus TryGetGroup(int ordinal, out GroupData group, out FontDiagnostic diagnostic)
    {
        group = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (ordinal < 0 || (uint)ordinal >= NumGroups)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 12, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), NumGroups);
        return TryReadGroupAt(CmapFormat13Layout.HeaderLength + 12UL * (uint)ordinal, ordinal,
            out group, out diagnostic);
    }

    /// <summary>Strict format 13 validation: reserved/language zero, exact length, ordered
    /// non-overlapping scalar-bounded groups and the constant start GID against numGlyphs.</summary>
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (Reserved != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 2), out diagnostic, Reserved, 0);
        if (Language != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.LanguageId, 8), out diagnostic, Language, 0);
        ulong expected = CmapFormat13Layout.HeaderLength + 12UL * NumGroups;
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
                CmapFormat13Layout.HeaderLength + 12UL * (uint)i, i);
            if (group.StartCharCode > group.EndCharCode || group.EndCharCode > 0x10FFFF ||
                (!first && group.StartCharCode <= previousEnd))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    location, out diagnostic, group.StartCharCode, previousEnd);
            // Every code in the group resolves to the same GID, so only the start GID is bound.
            if (group.StartGlyphID >= (ulong)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId,
                        CmapFormat13Layout.HeaderLength + 12UL * (uint)i + 8, i),
                    out diagnostic, group.StartGlyphID, (ulong)numGlyphs);
            previousEnd = group.EndCharCode;
            first = false;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
