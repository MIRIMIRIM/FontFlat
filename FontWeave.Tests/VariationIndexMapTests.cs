using FontWeave.Core;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-03 commit 1's Variation-domain expansion: the DeltaSetIndexMap's
/// format 1 (u32 mapCount) read face, the HVAR domain's format-0 re-anchor with the
/// version-gate diagnostic held byte for byte (OQ-T6-03-4), and the LONG_WORDS
/// parameterized gate with the frozen default refusal kept (OQ-T6-03-5). Each
/// refusal is keyed on status/code/field/localOffset/Detail/Expected.</summary>
public sealed class VariationIndexMapTests
{
    private static readonly DataLocation Source = new(7, 0, 0x48564152u, -1,
        DataField.None, OffsetBasis.Table, 0);

    /// <summary>A format-1 map wire: format 1, entryFormat 0 (entrySize 1, innerBits 1),
    /// a u32 mapCount and the packed entries.</summary>
    private static byte[] Format1Map(uint mapCount, params byte[] packedEntries)
    {
        var wire = new List<byte> { 1, 0 };
        wire.AddRange([(byte)(mapCount >> 24), (byte)((mapCount >> 16) & 0xFF),
            (byte)((mapCount >> 8) & 0xFF), (byte)(mapCount & 0xFF)]);
        wire.AddRange(packedEntries);
        return [.. wire];
    }

    [Fact]
    public void FormatOneMapReadsWithTheWideCount()
    {
        // format 1, entryFormat 0 (1 byte per entry, innerBits 1): entries
        // 0x02=(1,0), 0x01=(0,1), 0x03=(1,1) - behind the u32 count, at byte 6.
        byte[] wire = Format1Map(3, 0x02, 0x01, 0x03);
        ReadStatus status = DeltaSetIndexMapView.TryOpen(wire, out var view, out _,
            Source.AtLocal(DataField.None, 40));
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.Validate(out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryRead(out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(1, model!.Format);
        Assert.Equal(3, model.MapCount);
        Assert.True(model.TryGetMapping(0, out var entry));
        Assert.Equal((ushort)1, entry.Outer);
        Assert.Equal((ushort)0, entry.Inner);
        Assert.True(model.TryGetMapping(9, out entry));   // last-entry fallback
        Assert.Equal((ushort)1, entry.Outer);
        Assert.Equal(wire.Length, model.ConsumedLength);
    }

    [Fact]
    public void FormatOneMapCountReadsAllThirtyTwoBits()
    {
        // mapCount 0x00010002 - beyond the u16 domain, read through the wide arm.
        const uint count = 0x00010002;
        byte[] wire = Format1Map(count, new byte[count]);
        ReadStatus status = DeltaSetIndexMapView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryRead(out var model, out _));
        Assert.NotNull(model);
        Assert.Equal(1, model!.Format);
        Assert.Equal((int)count, model.MapCount);
    }

    [Fact]
    public void FormatZeroMapKeepsItsFrozenFace()
    {
        byte[] wire = [0, 0, 0, 2, 0x02, 0x01];
        ReadStatus status = DeltaSetIndexMapView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryRead(out var model, out _));
        Assert.NotNull(model);
        Assert.Equal(0, model!.Format);
        Assert.Equal(2, model.MapCount);
    }

    // ---- the HVAR domain's format-0 re-anchor (OQ-T6-03-4) ----

    /// <summary>A minimal HVAR wire: header (20), the IVS (format 1, one one-axis
    /// region, one VarData row) and an optional advance map at `mapFormat`.</summary>
    private static byte[] HvarWire(byte mapFormat)
    {
        var w = new List<byte>();
        void U16(ushort v) { w.AddRange([(byte)(v >> 8), (byte)(v & 0xFF)]); }
        void U32(uint v) { w.AddRange([(byte)(v >> 24), (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]); }
        // header: version 1.0, store@20, maps NULL except the advance map below.
        U32(0x00010000);
        U32(20);
        U32(0);
        U32(0); // lsb
        U32(0); // rsb
        // IVS at 20: format 1, regionList@12 (IVS-relative = HVAR 32), dataCount 1,
        // dataOffset[0]@22 (IVS-relative = HVAR 42).
        U16(1); U32(12); U16(1); U32(22);
        // region list at 32: axisCount 1, regionCount 1, one axis triple
        // (start 0, peak +1.0, end +1.0 - the sign rule wants start<=0<=end) - 10
        // bytes (32..41).
        U16(1); U16(1); U16(0x0000); U16(0x4000); U16(0x4000);
        // VarData at 42: itemCount 1, wordDeltaCount 0, regionIndexCount 1,
        // regionIndexes [0], one int16 row - 10 bytes (42..51).
        U16(1); U16(0); U16(1); U16(0); U16(0);
        if (mapFormat != 0)
        {
            uint advanceMapAt = (uint)w.Count;
            // Patch the advance-map offset word (header position 8).
            w[8] = (byte)(advanceMapAt >> 24);
            w[9] = (byte)((advanceMapAt >> 16) & 0xFF);
            w[10] = (byte)((advanceMapAt >> 8) & 0xFF);
            w[11] = (byte)(advanceMapAt & 0xFF);
            // format 1 map: u32 count 1 + one packed entry (innerBits 1, entrySize 1).
            w.Add(mapFormat);
            w.Add(0);
            w.AddRange([0, 0, 0, 1]);
            w.Add(0x02);
        }
        return [.. w];
    }

    [Fact]
    public void HvarFormatOneMapKeepsTheVersionGateDiagnostic()
    {
        byte[] wire = HvarWire(1);
        ReadStatus status = HvarView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.UnsupportedFormat,
            view.ValidateTable(axisCount: 1, numGlyphs: 8, out var diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal(DataField.Version, diagnostic.Location.Field);
        // The frozen n17 shape: the offset names the map's own start (52 in this wire),
        // not the HVAR header.
        Assert.Equal(52UL, diagnostic.Location.Offset);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Equal(0UL, diagnostic.Expected);
        Assert.Equal(ReadStatus.UnsupportedFormat,
            view.TryReadTable(1, 8, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal(1UL, diagnostic.Detail);
    }

    [Fact]
    public void HvarFormatZeroMapStillReads()
    {
        // The same wire without the advance map rides the implicit domain:
        // VarData[0].itemCount (1) must cover numGlyphs - 8 refuses, 1 passes.
        byte[] wire = HvarWire(0);
        ReadStatus status = HvarView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidIndex,
            view.ValidateTable(1, 8, out var diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(1, 1, out diagnostic));
    }

    // ---- the LONG_WORDS parameterized gate (OQ-T6-03-5) ----

    /// <summary>One VarData subtable with a LONG_WORDS row: wordDeltaCount 0x8001,
    /// one region index, one int32 delta of 100000.</summary>
    private static byte[] LongWordsVarData() =>
        [0, 1,                     // itemCount 1
         0x80, 0x01,               // wordDeltaCount: LONG_WORDS | 1
         0, 1,                     // regionIndexCount 1
         0, 0,                     // regionIndexes[0] = 0
         0x00, 0x01, 0x86, 0xA0];  // int32 row delta 100000 big-endian

    [Fact]
    public void LongWordsTypedReadRefusesByDefaultAndReadsWhenAllowed()
    {
        byte[] wire = LongWordsVarData();
        ReadStatus status = ItemVariationDataView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.UnsupportedFormat,
            view.TryRead(out var model, out var diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(DataField.Flags, diagnostic.Location.Field);
        Assert.Equal(0x8001UL, diagnostic.Detail);
        Assert.Equal(0UL, diagnostic.Expected);
        Assert.Equal(ReadStatus.Done,
            view.TryRead(out model, out diagnostic, limits: null, allowLongWords: true));
        Assert.NotNull(model);
        Assert.True(model!.LongWords);
        Assert.Equal(1, model.WordDeltaCountValue);
        Assert.Equal(100000, model.Rows[0][0]);
        Assert.Equal(wire.Length, model.ConsumedLength);
    }
}
