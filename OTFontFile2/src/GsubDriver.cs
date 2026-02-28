using System.Buffers;
using OTFontFile2.Tables;

namespace OTFontFile2;

/// <summary>
/// Minimal GSUB driver for regression/testing and tooling scenarios.
/// This is not a full shaping engine: it ignores GDEF/mark filtering, ignores feature variations,
/// and only supports a small subset of lookup types.
/// </summary>
public static class GsubDriver
{
    /// <summary>
    /// Applies supported GSUB substitutions to <paramref name="glyphBuffer"/> in place.
    /// Supported lookup types: SingleSubst (1), LigatureSubst (4), and ExtensionSubst (7) wrapping (1/4).
    /// </summary>
    public static bool TryApply(
        in GsubTable gsub,
        Tag scriptTag,
        Tag langSysTag,
        ReadOnlySpan<Tag> enabledFeatures,
        Span<ushort> glyphBuffer,
        ref int glyphCount)
    {
        if ((uint)glyphCount > (uint)glyphBuffer.Length)
            return false;

        if (!gsub.TryGetLookupList(out var lookupList))
            return false;

        if (!gsub.TryGetLookupIndexEnumerator(scriptTag, langSysTag, enabledFeatures, out var lookupEnumerator))
            return false;

        while (lookupEnumerator.MoveNext())
        {
            ushort lookupIndex = lookupEnumerator.Current;
            if (!lookupList.TryGetLookup(lookupIndex, out var lookup))
                return false;

            if (!TryApplyLookup(lookup, glyphBuffer, ref glyphCount))
                return false;
        }

        return true;
    }

    private static bool TryApplyLookup(OtlLayoutTable.Lookup lookup, Span<ushort> glyphBuffer, ref int glyphCount)
    {
        ushort lookupType = lookup.LookupType;
        ushort subtableCount = lookup.SubtableCount;

        if (subtableCount == 0)
            return true;

        // Skip unsupported lookup types.
        if (lookupType is not (1 or 4 or 7))
            return true;

        int capacity = glyphBuffer.Length;
        if (glyphCount > capacity)
            return false;

        SubtableHandle[] rented = ArrayPool<SubtableHandle>.Shared.Rent(subtableCount);
        Span<SubtableHandle> subtables = rented.AsSpan(0, subtableCount);

        int supportedCount = 0;
        try
        {
            for (int i = 0; i < subtableCount; i++)
            {
                if (!lookup.TryGetSubtableOffset(i, out ushort rel))
                    return false;

                int abs = lookup.Offset + rel;
                ushort resolvedType = lookupType;
                int resolvedOffset = abs;

                if (lookupType == 7)
                {
                    if (!GsubExtensionSubstSubtable.TryCreate(lookup.Table, abs, out var ext))
                        return false;

                    if (!ext.TryResolve(out resolvedType, out resolvedOffset))
                        return false;
                }

                if (resolvedType == 1)
                {
                    if (!GsubSingleSubstSubtable.TryCreate(lookup.Table, resolvedOffset, out var single))
                        return false;

                    subtables[supportedCount++] = new SubtableHandle(single);
                }
                else if (resolvedType == 4)
                {
                    if (!GsubLigatureSubstSubtable.TryCreate(lookup.Table, resolvedOffset, out var ligature))
                        return false;

                    subtables[supportedCount++] = new SubtableHandle(ligature);
                }
                else
                {
                    // Ignore unsupported resolved types.
                }
            }

            if (supportedCount == 0)
                return true;

            subtables = subtables.Slice(0, supportedCount);

            // Apply lookup left-to-right.
            for (int pos = 0; pos < glyphCount; pos++)
            {
                ushort g = glyphBuffer[pos];

                for (int s = 0; s < subtables.Length; s++)
                {
                    ref readonly SubtableHandle sub = ref subtables[s];
                    if (sub.Type == SubtableType.Single)
                    {
                        if (!sub.Single.TrySubstituteGlyph(g, out bool substituted, out ushort substitute))
                            return false;

                        if (substituted)
                        {
                            glyphBuffer[pos] = substitute;
                            break;
                        }

                        continue;
                    }

                    if (sub.Type == SubtableType.Ligature)
                    {
                        if (!TryApplyLigatureAt(sub.Ligature, glyphBuffer, pos, ref glyphCount, out bool covered, out bool substituted))
                            return false;

                        if (covered)
                        {
                            // Coverage matched: stop at this subtable regardless of whether a ligature was found.
                            break;
                        }

                        if (substituted)
                            break;
                    }
                }
            }

            return true;
        }
        finally
        {
            rented.AsSpan(0, subtableCount).Clear();
            ArrayPool<SubtableHandle>.Shared.Return(rented);
        }
    }

    private static bool TryApplyLigatureAt(
        in GsubLigatureSubstSubtable liga,
        Span<ushort> glyphBuffer,
        int pos,
        ref int glyphCount,
        out bool covered,
        out bool substituted)
    {
        covered = false;
        substituted = false;

        int remaining = glyphCount - pos;
        if (remaining < 2)
            return true;

        ushort first = glyphBuffer[pos];
        if (!liga.TryGetLigatureSetForGlyph(first, out bool isCovered, out var set))
            return false;

        if (!isCovered)
            return true;

        covered = true;

        int ligatureCount = set.LigatureCount;
        for (int i = 0; i < ligatureCount; i++)
        {
            if (!set.TryGetLigature(i, out var lig))
                return false;

            int componentCount = lig.ComponentCount;
            if (componentCount < 2 || componentCount > remaining)
                continue;

            bool match = true;
            for (int c = 1; c < componentCount; c++)
            {
                if (!lig.TryGetComponentGlyphId(c - 1, out ushort expected))
                    return false;

                if (glyphBuffer[pos + c] != expected)
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;

            // Apply substitution.
            glyphBuffer[pos] = lig.LigGlyph;
            int remove = componentCount - 1;
            if (remove != 0)
                RemoveRange(glyphBuffer, ref glyphCount, pos + 1, remove);

            substituted = true;
            return true;
        }

        return true;
    }

    private static void RemoveRange(Span<ushort> glyphBuffer, ref int glyphCount, int start, int removeCount)
    {
        if (removeCount <= 0)
            return;

        int tailStart = start + removeCount;
        int tailLen = glyphCount - tailStart;
        if (tailLen > 0)
            glyphBuffer.Slice(tailStart, tailLen).CopyTo(glyphBuffer.Slice(start, tailLen));

        glyphCount -= removeCount;
    }

    private readonly struct SubtableHandle
    {
        private readonly GsubSingleSubstSubtable _single;
        private readonly GsubLigatureSubstSubtable _ligature;

        public SubtableType Type { get; }

        public GsubSingleSubstSubtable Single => _single;
        public GsubLigatureSubstSubtable Ligature => _ligature;

        public SubtableHandle(GsubSingleSubstSubtable single)
        {
            Type = SubtableType.Single;
            _single = single;
            _ligature = default;
        }

        public SubtableHandle(GsubLigatureSubstSubtable ligature)
        {
            Type = SubtableType.Ligature;
            _single = default;
            _ligature = ligature;
        }
    }

    private enum SubtableType : byte
    {
        Single = 1,
        Ligature = 4
    }
}
