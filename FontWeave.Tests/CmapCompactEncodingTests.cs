using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class CmapCompactEncodingTests
{
    private static readonly DataLocation Location = DataLocation.Anonymous(0x636D6170);

    [Fact]
    public void AffineRunsInsideANonAffineWindowUseDeltaSegments()
    {
        CmapMapping[] mappings = Enumerable.Range(0, 20)
            .Select(i => new CmapMapping((uint)(65 + i), (ushort)(i < 10 ? 10 + i : 90 + i)))
            .ToArray();
        byte[] bytes = Encode(mappings);
        Assert.Equal(40, bytes.Length);
        Assert.Equal(3, U16(bytes, 6) / 2);
        Assert.Equal(0, U16(bytes, 16 + 6 * 3));
        Assert.Equal(0, U16(bytes, 16 + 6 * 3 + 2));
        AssertMappings(bytes, mappings, 65536);
    }

    [Fact]
    public void SparseWindowsUseZeroHolesWithoutAddingMappings()
    {
        CmapMapping[] mappings = [new(65, 1), new(67, 9), new(69, 2)];
        byte[] bytes = Encode(mappings);
        Assert.Equal(42, bytes.Length);
        Assert.Equal(2, U16(bytes, 6) / 2);
        Assert.Equal(4, U16(bytes, 28));
        Assert.Equal(new ushort[] { 1, 0, 9, 0, 2 },
            Enumerable.Range(0, 5).Select(i => U16(bytes, 32 + 2 * i)).ToArray());
        AssertMappings(bytes, mappings, 65536);
    }

    [Fact]
    public void SmallMappingPartitionsMatchAnIndependentMinimumSizeOracle()
    {
        for (int mask = 0; mask < 256; mask++)
        for (int variant = 0; variant < 2; variant++)
        {
            CmapMapping[] mappings = Enumerable.Range(0, 8)
                .Where(i => (mask & (1 << i)) != 0)
                .Select(i => new CmapMapping((uint)(65 + i),
                    (ushort)(variant == 0 ? i + 1 : (i * 7) % 11 + 1)))
                .ToArray();
            byte[] bytes = Encode(mappings);
            Assert.Equal(24 + MinimumCost(mappings, 0), bytes.Length);
            AssertMappings(bytes, mappings, 80);
            Assert.Equal(0, Decode(bytes, 65535));
        }
    }

    [Theory]
    [InlineData(32751, true)]
    [InlineData(32752, false)]
    public void Format4LengthBoundaryIsCheckedAfterMinimumSizePartitioning(int count, bool fits)
    {
        CmapMapping[] mappings = Enumerable.Range(0, count)
            .Select(i => new CmapMapping((uint)(32 + i), (ushort)(i % 2 + 1))).ToArray();
        ReadStatus status = CmapTableCompiler.EncodeFormat4(0, mappings, Location,
            new BuildBudget(), out var output, out var diagnostic);
        if (fits)
        {
            Assert.Equal(ReadStatus.Done, status);
            Assert.Equal(65534, output!.Length);
            AssertMappings(output, mappings, 65536);
        }
        else
        {
            Assert.Equal(ReadStatus.InvalidFormat, status);
            Assert.Null(output);
            Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
            Assert.Equal(DataField.Length, diagnostic.Location.Field);
            Assert.Equal(65536UL, diagnostic.Detail);
            Assert.Equal(65535UL, diagnostic.Expected);
        }
    }

    [Fact]
    public void SparseMappingCountDoesNotImposeAnArtificialSegmentLimit()
    {
        CmapMapping[] mappings = Enumerable.Range(0, 8190)
            .Select(i => new CmapMapping((uint)(2 * i), (ushort)(i % 2 + 1))).ToArray();
        var budget = new BuildBudget(new BuildLimits
        {
            MaxWorkUnits = 250_000,
            MaxTemporaryBytes = 250_000
        });
        Assert.Equal(ReadStatus.Done, CmapTableCompiler.EncodeFormat4(0, mappings,
            Location, budget, out var output, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.Equal(32790, output!.Length);
        AssertMappings(output, mappings, 65536);
        Assert.InRange(budget.Usage.WorkUnits, 1, 250_000);
        Assert.InRange(budget.Usage.TemporaryBytes, 1, 250_000);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompactEncodingHonorsWorkAndTemporaryLimitsAtomically(bool workLimit)
    {
        var limits = workLimit ? new BuildLimits { MaxWorkUnits = 0 }
            : new BuildLimits { MaxTemporaryBytes = 0 };
        Assert.Equal(ReadStatus.ResourceLimit, CmapTableCompiler.EncodeFormat4(0,
            [new(65, 10), new(67, 4)], Location, new BuildBudget(limits),
            out var output, out var diagnostic));
        Assert.Null(output);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(workLimit ? DataField.WorkUnits : DataField.TemporaryBytes,
            diagnostic.Location.Field);
    }

    [Fact]
    public void TableCompilationPreservesEncodingRecordsSharingAndAllMappings()
    {
        ushort[] glyphs = Enumerable.Range(0, 20)
            .Select(i => (ushort)(i < 10 ? 10 + i : 90 + i)).ToArray();
        var format4 = new CmapFormat4Subtable(0,
            [new(65, 84, 0, 4), new(65535, 65535, 1, 0)], glyphs, 0, Location);
        CmapFormat12Group[] groups = Enumerable.Range(0, 20)
            .Select(i => new CmapFormat12Group((uint)(65 + i), (uint)(65 + i), glyphs[i]))
            .Append(new(0x1F600, 0x1F600, 120)).ToArray();
        var format12 = new CmapFormat12Subtable(groups, 0, Location);
        CmapRecord[] records = [new(0, 3, 0, 0), new(3, 1, 0, 0), new(3, 10, 0, 1)];
        var model = new CmapTable(records, [format4, format12], 128, Location, default);
        Assert.Equal(ReadStatus.Done, CmapTableCompiler.TryCompile(model,
            out var bytes, out var diagnostic));
        Assert.Equal(120, bytes!.Length);
        Assert.Equal(3, U16(bytes, 2));
        Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)),
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16)));
        Assert.Equal(ReadStatus.Done, CmapView.TryOpen(bytes, out var view, out diagnostic));
        Assert.Equal(ReadStatus.Done, CmapContext.TryCreate(128, out var context, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryRead(context!, out var reread, out diagnostic));
        for (int r = 0; r < 3; r++)
        {
            Assert.Equal(records[r].PlatformId, reread!.Records[r].PlatformId);
            Assert.Equal(records[r].EncodingId, reread.Records[r].EncodingId);
            Assert.Equal(ReadStatus.Done, reread.TryGetSubtable(r, out var table, out diagnostic));
            for (uint code = 0; code < 65536; code++)
            {
                uint expected = code is >= 65 and <= 84 ? glyphs[code - 65] : 0u;
                Assert.Equal(ReadStatus.Done, table!.TryMapCharacter(code, out bool mapped,
                    out uint actual, out diagnostic));
                Assert.Equal(expected != 0, mapped);
                Assert.Equal(expected, actual);
            }
            Assert.Equal(ReadStatus.Done, table!.TryMapCharacter(0x1F600,
                out bool nonBmp, out uint nonBmpGlyph, out diagnostic));
            Assert.Equal(r == 2, nonBmp);
            Assert.Equal(r == 2 ? 120u : 0u, nonBmpGlyph);
        }
    }

    private static byte[] Encode(CmapMapping[] mappings)
    {
        Assert.Equal(ReadStatus.Done, CmapTableCompiler.EncodeFormat4(0, mappings,
            Location, new BuildBudget(), out var bytes, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.Equal(ReadStatus.Done, CmapFormat4View.TryOpen(bytes!, out var view, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.Validate(true, 256, out diagnostic));
        return bytes!;
    }

    private static void AssertMappings(byte[] bytes, CmapMapping[] mappings, int count)
    {
        var expected = mappings.ToDictionary(m => (int)m.CharacterCode, m => m.GlyphId);
        for (int code = 0; code < count; code++)
            Assert.Equal(expected.GetValueOrDefault(code), Decode(bytes, code));
    }

    private static ushort Decode(byte[] bytes, int code)
    {
        int count = U16(bytes, 6) / 2;
        for (int s = 0; s < count; s++)
        {
            int end = U16(bytes, 14 + 2 * s);
            if (code > end) continue;
            int start = U16(bytes, 16 + 2 * count + 2 * s);
            if (code < start) return 0;
            int delta = U16(bytes, 16 + 4 * count + 2 * s);
            int at = 16 + 6 * count + 2 * s;
            int offset = U16(bytes, at);
            if (offset == 0) return unchecked((ushort)(code + delta));
            int glyph = U16(bytes, at + offset + 2 * (code - start));
            return glyph == 0 ? (ushort)0 : unchecked((ushort)(glyph + delta));
        }
        return 0;
    }

    private static ushort U16(byte[] bytes, int at) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at));

    private static int MinimumCost(CmapMapping[] mappings, int start)
    {
        if (start == mappings.Length) return 0;
        int minimum = int.MaxValue;
        bool affine = true;
        for (int end = start; end < mappings.Length; end++)
        {
            affine &= mappings[end].CharacterCode == mappings[start].CharacterCode + end - start
                && mappings[end].GlyphId == mappings[start].GlyphId + end - start;
            int cost = affine ? 8 : 8 + 2 * (int)(mappings[end].CharacterCode
                - mappings[start].CharacterCode + 1);
            minimum = Math.Min(minimum, cost + MinimumCost(mappings, end + 1));
        }
        return minimum;
    }
}
