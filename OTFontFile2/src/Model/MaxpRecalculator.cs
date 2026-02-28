using OTFontFile2.Tables;
using System.Buffers;

namespace OTFontFile2;

internal static class MaxpRecalculator
{
    internal readonly struct DerivedFields
    {
        public ushort MaxPoints { get; }
        public ushort MaxContours { get; }
        public ushort MaxCompositePoints { get; }
        public ushort MaxCompositeContours { get; }
        public ushort MaxSizeOfInstructions { get; }
        public ushort MaxComponentElements { get; }
        public ushort MaxComponentDepth { get; }

        public DerivedFields(
            ushort maxPoints,
            ushort maxContours,
            ushort maxCompositePoints,
            ushort maxCompositeContours,
            ushort maxSizeOfInstructions,
            ushort maxComponentElements,
            ushort maxComponentDepth)
        {
            MaxPoints = maxPoints;
            MaxContours = maxContours;
            MaxCompositePoints = maxCompositePoints;
            MaxCompositeContours = maxCompositeContours;
            MaxSizeOfInstructions = maxSizeOfInstructions;
            MaxComponentElements = maxComponentElements;
            MaxComponentDepth = maxComponentDepth;
        }
    }

    public static bool TryRecalculateFromGlyf(GlyfTableBuilder glyf, out DerivedFields fields)
    {
        fields = default;

        if (glyf is null) throw new ArgumentNullException(nameof(glyf));
        if (!glyf.IsLinkedBaseFont)
            return false;

        ushort numGlyphs = glyf.NumGlyphs;

        byte[] state = ArrayPool<byte>.Shared.Rent(numGlyphs); // 0=unvisited, 1=visiting, 2=done
        ushort[] points = ArrayPool<ushort>.Shared.Rent(numGlyphs);
        ushort[] contours = ArrayPool<ushort>.Shared.Rent(numGlyphs);
        ushort[] depth = ArrayPool<ushort>.Shared.Rent(numGlyphs);
        ushort[] componentElements = ArrayPool<ushort>.Shared.Rent(numGlyphs);

        state.AsSpan(0, numGlyphs).Clear();
        points.AsSpan(0, numGlyphs).Clear();
        contours.AsSpan(0, numGlyphs).Clear();
        depth.AsSpan(0, numGlyphs).Clear();
        componentElements.AsSpan(0, numGlyphs).Clear();

        try
        {
            ushort maxPoints = 0;
            ushort maxContours = 0;
            ushort maxCompositePoints = 0;
            ushort maxCompositeContours = 0;
            ushort maxSizeOfInstructions = 0;
            ushort maxComponentElements = 0;
            ushort maxComponentDepth = 0;

            for (ushort gid = 0; gid < numGlyphs; gid++)
            {
                if (!TryGetGlyphData(glyf, gid, out ReadOnlySpan<byte> glyphData))
                    return false;

                if (glyphData.Length == 0)
                    continue;

                if (!GlyfTable.TryReadGlyphHeader(glyphData, out var header))
                    return false;

                if (!header.IsComposite)
                {
                    if (!GlyfTable.TryCreateSimpleGlyphContourEnumerator(glyphData, out var ce))
                        return false;

                    ushort p = ce.PointCount;
                    ushort c = ce.ContourCount;

                    if (p > maxPoints) maxPoints = p;
                    if (c > maxContours) maxContours = c;

                    points[gid] = p;
                    contours[gid] = c;

                    if (!GlyfTable.TryGetSimpleGlyphInstructions(glyphData, out var simpleInstr))
                        return false;

                    if (simpleInstr.Length > ushort.MaxValue)
                        maxSizeOfInstructions = ushort.MaxValue;
                    else if ((ushort)simpleInstr.Length > maxSizeOfInstructions)
                        maxSizeOfInstructions = (ushort)simpleInstr.Length;

                    state[gid] = 2;
                    continue;
                }

                // Composite glyphs: compute expanded point/contour counts and depth with cycle detection.
                if (!TryComputeCompositeMetrics(glyf, gid, state, points, contours, depth, componentElements))
                    return false;

                ushort cp = points[gid];
                ushort cc = contours[gid];

                if (cp > maxCompositePoints) maxCompositePoints = cp;
                if (cc > maxCompositeContours) maxCompositeContours = cc;

                ushort elem = componentElements[gid];
                if (elem > maxComponentElements) maxComponentElements = elem;

                ushort d = depth[gid];
                if (d > maxComponentDepth) maxComponentDepth = d;

                if (!GlyfTable.TryGetCompositeGlyphInstructions(glyphData, out var compositeInstr))
                    return false;

                if (compositeInstr.Length > ushort.MaxValue)
                    maxSizeOfInstructions = ushort.MaxValue;
                else if ((ushort)compositeInstr.Length > maxSizeOfInstructions)
                    maxSizeOfInstructions = (ushort)compositeInstr.Length;
            }

            fields = new DerivedFields(
                maxPoints: maxPoints,
                maxContours: maxContours,
                maxCompositePoints: maxCompositePoints,
                maxCompositeContours: maxCompositeContours,
                maxSizeOfInstructions: maxSizeOfInstructions,
                maxComponentElements: maxComponentElements,
                maxComponentDepth: maxComponentDepth);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(state);
            ArrayPool<ushort>.Shared.Return(points);
            ArrayPool<ushort>.Shared.Return(contours);
            ArrayPool<ushort>.Shared.Return(depth);
            ArrayPool<ushort>.Shared.Return(componentElements);
        }
    }

    private static bool TryGetGlyphData(GlyfTableBuilder glyf, ushort glyphId, out ReadOnlySpan<byte> glyphData)
    {
        if (!glyf.TryGetGlyphData(glyphId, out glyphData))
            return false;

        return true;
    }

    private static bool TryComputeCompositeMetrics(
        GlyfTableBuilder glyf,
        ushort glyphId,
        byte[] state,
        ushort[] points,
        ushort[] contours,
        ushort[] depth,
        ushort[] componentElements)
    {
        if (state[glyphId] == 2)
            return true;

        if (state[glyphId] == 1)
            return false; // cycle

        state[glyphId] = 1;

        if (!TryGetGlyphData(glyf, glyphId, out ReadOnlySpan<byte> glyphData))
            return false;

        if (glyphData.Length == 0)
        {
            points[glyphId] = 0;
            contours[glyphId] = 0;
            depth[glyphId] = 0;
            componentElements[glyphId] = 0;
            state[glyphId] = 2;
            return true;
        }

        if (!GlyfTable.TryReadGlyphHeader(glyphData, out var header))
            return false;

        if (!header.IsComposite)
        {
            if (!GlyfTable.TryCreateSimpleGlyphContourEnumerator(glyphData, out var ce))
                return false;

            points[glyphId] = ce.PointCount;
            contours[glyphId] = ce.ContourCount;
            depth[glyphId] = 0;
            componentElements[glyphId] = 0;
            state[glyphId] = 2;
            return true;
        }

        if (!GlyfTable.TryCreateCompositeGlyphComponentEnumerator(glyphData, out var e))
            return false;

        int elemCount = 0;
        int totalPoints = 0;
        int totalContours = 0;
        ushort maxChildDepth = 0;

        while (e.MoveNext())
        {
            elemCount++;

            ushort child = e.Current.GlyphIndex;
            if (!TryComputeCompositeMetrics(glyf, child, state, points, contours, depth, componentElements))
                return false;

            totalPoints = SaturatingAddUShort(totalPoints, points[child]);
            totalContours = SaturatingAddUShort(totalContours, contours[child]);

            ushort d = depth[child];
            if (d > maxChildDepth)
                maxChildDepth = d;
        }

        if (!e.IsValid)
            return false;

        points[glyphId] = (ushort)Math.Min(ushort.MaxValue, totalPoints);
        contours[glyphId] = (ushort)Math.Min(ushort.MaxValue, totalContours);
        depth[glyphId] = (ushort)Math.Min(ushort.MaxValue, maxChildDepth + 1);
        componentElements[glyphId] = (ushort)Math.Min(ushort.MaxValue, elemCount);

        state[glyphId] = 2;
        return true;
    }

    private static int SaturatingAddUShort(int acc, ushort value)
    {
        int sum = acc + value;
        return sum > ushort.MaxValue ? ushort.MaxValue : sum;
    }
}
