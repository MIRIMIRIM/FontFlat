namespace OTFontFile2.Tables.Cff;

public static class CffType2CharStrings
{
    public static bool TryExpandGlyph(CffTable cff, int glyphId, int maxDepth, out Type2CharStringProgram program)
    {
        program = null!;

        if (!cff.TryGetTopDict(out var top))
            return false;

        if (!top.TryGetCharStringsIndex(out var charStrings))
            return false;

        if (!charStrings.TryGetObjectSpan(glyphId, out var charString))
            return false;

        if (!cff.TryGetGlobalSubrIndex(out var gsubrsIndex))
            return false;

        CffSubrProvider global = new(gsubrsIndex);

        // Determine local subrs based on FDSelect/FDArray if present.
        if (top.FdArrayOffset > 0 && top.FdSelectOffset > 0 && top.TryGetFdSelect(out var fdSelect))
        {
            if (!fdSelect.TryGetFontDictIndex(glyphId, out ushort fdIndex))
                return false;

            if (!top.TryGetFontDict(fdIndex, out var fd))
                return false;

            if (!fd.TryGetPrivateDict(out var priv))
                return false;

            if (priv.TryGetSubrsIndex(out var subrs))
            {
                CffSubrProvider local = new(subrs);
                return Type2Subroutines.TryExpand(charString, global, local, maxDepth, out program);
            }

            return Type2Subroutines.TryExpand(charString, global, new EmptySubrProvider(), maxDepth, out program);
        }

        if (top.TryGetPrivateDict(out var topPriv) && topPriv.TryGetSubrsIndex(out var lsubrs))
        {
            CffSubrProvider local = new(lsubrs);
            return Type2Subroutines.TryExpand(charString, global, local, maxDepth, out program);
        }

        return Type2Subroutines.TryExpand(charString, global, new EmptySubrProvider(), maxDepth, out program);
    }

    public static bool TryExpandGlyph(Cff2Table cff2, int glyphId, int maxDepth, out Type2CharStringProgram program)
    {
        program = null!;

        if (!cff2.TryGetCharStringsIndex(out var charStrings))
            return false;

        if (!charStrings.TryGetObjectSpan(glyphId, out var charString))
            return false;

        if (!cff2.TryGetGlobalSubrIndex(out var gsubrsIndex))
            return false;

        Cff2SubrProvider global = new(gsubrsIndex);

        if (cff2.TryGetFdSelect(out var fdSelect))
        {
            if (!fdSelect.TryGetFontDictIndex(glyphId, out ushort fdIndex))
                return false;

            if (!cff2.TryGetFontDict(fdIndex, out var fd))
                return false;

            if (!fd.TryGetPrivateDictCff2(out var priv))
                return false;

            if (priv.TryGetSubrsIndex(out var lsubrs))
            {
                Cff2SubrProvider local = new(lsubrs);
                return Type2Subroutines.TryExpand(charString, global, local, maxDepth, out program);
            }

            return Type2Subroutines.TryExpand(charString, global, new EmptySubrProvider(), maxDepth, out program);
        }

        return Type2Subroutines.TryExpand(charString, global, new EmptySubrProvider(), maxDepth, out program);
    }
}

