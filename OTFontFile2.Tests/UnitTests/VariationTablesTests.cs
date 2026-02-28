using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class VariationTablesTests
{
    [TestMethod]
    public void SyntheticFvarAvarStatTables_Parse()
    {
        byte[] fvarBytes = BuildFvarTable();
        byte[] avarBytes = BuildAvarTable();
        byte[] statBytes = BuildStatTable();

        var builder = new SfntBuilder { SfntVersion = 0x00010000 };
        builder.SetTable(KnownTags.fvar, fvarBytes);
        builder.SetTable(KnownTags.avar, avarBytes);
        builder.SetTable(KnownTags.STAT, statBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetFvar(out var fvar));
        Assert.AreEqual((ushort)2, fvar.AxisCount);
        Assert.AreEqual((ushort)1, fvar.InstanceCount);
        Assert.AreEqual((ushort)20, fvar.AxisSize);
        Assert.IsTrue(fvar.InstanceSize >= 12);

        Assert.IsTrue(fvar.TryGetAxisRecord(0, out var wghtAxis));
        Assert.AreEqual("wght", wghtAxis.AxisTag.ToString());
        Assert.AreEqual(100u << 16, wghtAxis.MinValue.RawValue);
        Assert.AreEqual(400u << 16, wghtAxis.DefaultValue.RawValue);
        Assert.AreEqual(900u << 16, wghtAxis.MaxValue.RawValue);
        Assert.AreEqual((ushort)256, wghtAxis.AxisNameId);

        Assert.IsTrue(fvar.TryGetAxisRecord(1, out var slntAxis));
        Assert.AreEqual("slnt", slntAxis.AxisTag.ToString());
        Assert.AreEqual(unchecked((uint)(-10 << 16)), slntAxis.MinValue.RawValue);
        Assert.AreEqual(0u, slntAxis.DefaultValue.RawValue);
        Assert.AreEqual(0u, slntAxis.MaxValue.RawValue);

        Assert.IsTrue(fvar.TryGetInstanceRecord(0, out var inst));
        Assert.AreEqual((ushort)300, inst.SubfamilyNameId);
        Assert.IsTrue(inst.TryGetCoordinate(0, out var c0));
        Assert.IsTrue(inst.TryGetCoordinate(1, out var c1));
        Assert.AreEqual(700u << 16, c0.RawValue);
        Assert.AreEqual(unchecked((uint)(-5 << 16)), c1.RawValue);
        Assert.IsTrue(inst.TryGetPostScriptNameId(out ushort psNameId));
        Assert.AreEqual((ushort)301, psNameId);

        Assert.IsTrue(font.TryGetAvar(out var avar));
        Assert.AreEqual((ushort)2, avar.AxisCount);

        Assert.IsTrue(avar.TryGetSegmentMap(0, out var seg0));
        Assert.AreEqual((ushort)3, seg0.PositionMapCount);
        Assert.IsTrue(seg0.TryGetAxisValueMap(0, out var m00));
        Assert.AreEqual(unchecked((short)0xC000), m00.FromCoordinate.RawValue);
        Assert.AreEqual(unchecked((short)0xC000), m00.ToCoordinate.RawValue);

        Assert.IsTrue(avar.TryGetSegmentMap(1, out var seg1));
        Assert.AreEqual((ushort)4, seg1.PositionMapCount);
        Assert.IsTrue(seg1.TryGetAxisValueMap(2, out var m12));
        Assert.AreEqual(0x2000, m12.FromCoordinate.RawValue);
        Assert.AreEqual(0x2666, m12.ToCoordinate.RawValue);

        Assert.IsTrue(font.TryGetStat(out var stat));
        Assert.IsTrue(stat.IsSupported);
        Assert.AreEqual((ushort)1, stat.MajorVersion);
        Assert.AreEqual((ushort)1, stat.MinorVersion);
        Assert.AreEqual((ushort)2, stat.DesignAxisCount);
        Assert.AreEqual((ushort)4, stat.AxisValueCount);
        Assert.AreEqual((ushort)2, stat.ElidedFallbackNameId);

        Assert.IsTrue(stat.TryGetDesignAxisRecord(0, out var da0));
        Assert.AreEqual("wght", da0.AxisTag.ToString());
        Assert.AreEqual((ushort)256, da0.AxisNameId);
        Assert.AreEqual((ushort)0, da0.AxisOrdering);

        Assert.IsTrue(stat.TryGetAxisValueTable(0, out var avt0));
        Assert.IsTrue(avt0.TryGetFormat1(out var f1));
        Assert.AreEqual((ushort)0, f1.AxisIndex);
        Assert.AreEqual((ushort)0, f1.Flags);
        Assert.AreEqual((ushort)300, f1.ValueNameId);
        Assert.AreEqual(700u << 16, f1.Value.RawValue);

        Assert.IsTrue(stat.TryGetAxisValueTable(1, out var avt1));
        Assert.IsTrue(avt1.TryGetFormat2(out var f2));
        Assert.AreEqual((ushort)0, f2.AxisIndex);
        Assert.AreEqual((ushort)1, f2.Flags);
        Assert.AreEqual((ushort)301, f2.ValueNameId);
        Assert.AreEqual(400u << 16, f2.NominalValue.RawValue);
        Assert.AreEqual(300u << 16, f2.RangeMinValue.RawValue);
        Assert.AreEqual(500u << 16, f2.RangeMaxValue.RawValue);

        Assert.IsTrue(stat.TryGetAxisValueTable(2, out var avt2));
        Assert.IsTrue(avt2.TryGetFormat3(out var f3));
        Assert.AreEqual((ushort)1, f3.AxisIndex);
        Assert.AreEqual((ushort)0, f3.Flags);
        Assert.AreEqual((ushort)302, f3.ValueNameId);
        Assert.AreEqual(unchecked((uint)(-10 << 16)), f3.Value.RawValue);
        Assert.AreEqual(0u, f3.LinkedValue.RawValue);

        Assert.IsTrue(stat.TryGetAxisValueTable(3, out var avt3));
        Assert.IsTrue(avt3.TryGetFormat4(out var f4));
        Assert.AreEqual((ushort)0, f4.Flags);
        Assert.AreEqual((ushort)303, f4.ValueNameId);
        Assert.AreEqual((ushort)2, f4.AxisValueRecordCount);
        Assert.IsTrue(f4.TryGetAxisValueRecord(0, out var r0));
        Assert.IsTrue(f4.TryGetAxisValueRecord(1, out var r1));
        Assert.AreEqual((ushort)0, r0.AxisIndex);
        Assert.AreEqual(700u << 16, r0.Value.RawValue);
        Assert.AreEqual((ushort)1, r1.AxisIndex);
        Assert.AreEqual(unchecked((uint)(-5 << 16)), r1.Value.RawValue);
    }

    private static byte[] BuildFvarTable()
    {
        const ushort axisCount = 2;
        const ushort axisSize = 20;
        const ushort instanceCount = 1;
        const ushort instanceSize = 14; // (subfamilyNameID+flags) + 2 coords + postScriptNameID

        const ushort axesArrayOffset = 16;
        int instancesOffset = axesArrayOffset + (axisCount * axisSize);
        int totalLen = instancesOffset + (instanceCount * instanceSize);

        byte[] table = new byte[totalLen];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010000u);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), axesArrayOffset);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 2); // reserved
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), axisCount);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), axisSize);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), instanceCount);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), instanceSize);

        // Axis 0: wght
        int a0 = axesArrayOffset;
        WriteTag(span.Slice(a0, 4), "wght");
        WriteFixed(span.Slice(a0 + 4, 4), 100);
        WriteFixed(span.Slice(a0 + 8, 4), 400);
        WriteFixed(span.Slice(a0 + 12, 4), 900);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(a0 + 16, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(a0 + 18, 2), 256);

        // Axis 1: slnt (-10..0)
        int a1 = axesArrayOffset + axisSize;
        WriteTag(span.Slice(a1, 4), "slnt");
        WriteFixed(span.Slice(a1 + 4, 4), -10);
        WriteFixed(span.Slice(a1 + 8, 4), 0);
        WriteFixed(span.Slice(a1 + 12, 4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(a1 + 16, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(a1 + 18, 2), 257);

        // Instance 0: name 300, coords [700, -5], postScriptNameID 301
        int inst0 = instancesOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(inst0 + 0, 2), 300);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(inst0 + 2, 2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(inst0 + 4, 4), 700u << 16);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(inst0 + 8, 4), unchecked((uint)(-5 << 16)));
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(inst0 + 12, 2), 301);

        return table;
    }

    private static byte[] BuildAvarTable()
    {
        // version(4) + reserved(2) + axisCount(2) + seg0(2+3*4) + seg1(2+4*4) = 40
        byte[] table = new byte[40];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010000u);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 2);

        int pos = 8;

        // Axis 0: 3 maps
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pos, 2), 3);
        pos += 2;
        WriteF2Dot14Map(span.Slice(pos, 4), unchecked((short)0xC000), unchecked((short)0xC000)); // -1 -> -1
        WriteF2Dot14Map(span.Slice(pos + 4, 4), 0, 0); // 0 -> 0
        WriteF2Dot14Map(span.Slice(pos + 8, 4), 0x4000, 0x4000); // 1 -> 1
        pos += 12;

        // Axis 1: 4 maps
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pos, 2), 4);
        pos += 2;
        WriteF2Dot14Map(span.Slice(pos, 4), unchecked((short)0xC000), unchecked((short)0xC000)); // -1 -> -1
        WriteF2Dot14Map(span.Slice(pos + 4, 4), 0, 0); // 0 -> 0
        WriteF2Dot14Map(span.Slice(pos + 8, 4), 0x2000, 0x2666); // 0.5 -> ~0.6
        WriteF2Dot14Map(span.Slice(pos + 12, 4), 0x4000, 0x4000); // 1 -> 1

        return table;
    }

    private static byte[] BuildStatTable()
    {
        // Layout:
        // header(20) + designAxes(16) + axisValueOffsets(8) + axisValueTables(12+20+16+20=68) = 112
        byte[] table = new byte[112];
        var span = table.AsSpan();

        // Header
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1); // major
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 1); // minor
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 8); // designAxisSize
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 2); // designAxisCount
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), 20u); // designAxesOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 4); // axisValueCount
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(14, 4), 36u); // axisValueOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(18, 2), 2); // elidedFallbackNameID

        // Design axes (offset 20)
        int designAxes = 20;
        WriteTag(span.Slice(designAxes + 0, 4), "wght");
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(designAxes + 4, 2), 256);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(designAxes + 6, 2), 0);

        WriteTag(span.Slice(designAxes + 8, 4), "slnt");
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(designAxes + 12, 2), 257);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(designAxes + 14, 2), 1);

        // AxisValueOffsets array (offset 36, 4 entries)
        int axisValueOffset = 36;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(axisValueOffset + 0, 2), 8); // table0 at 44
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(axisValueOffset + 2, 2), 20); // table1 at 56
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(axisValueOffset + 4, 2), 40); // table2 at 76
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(axisValueOffset + 6, 2), 56); // table3 at 92

        // AxisValue tables
        int t0 = axisValueOffset + 8; // 44
        // Format 1: axis 0 value 700 nameID 300
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t0 + 0, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t0 + 2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t0 + 4, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t0 + 6, 2), 300);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(t0 + 8, 4), 700u << 16);

        int t1 = t0 + 12; // 56
        // Format 2: axis 0 nominal 400 range [300,500] nameID 301 flags 1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t1 + 0, 2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t1 + 2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t1 + 4, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t1 + 6, 2), 301);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(t1 + 8, 4), 400u << 16);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(t1 + 12, 4), 300u << 16);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(t1 + 16, 4), 500u << 16);

        int t2 = t1 + 20; // 76
        // Format 3: axis 1 value -10 linked 0 nameID 302
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t2 + 0, 2), 3);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t2 + 2, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t2 + 4, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t2 + 6, 2), 302);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(t2 + 8, 4), unchecked((uint)(-10 << 16)));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(t2 + 12, 4), 0u);

        int t3 = t2 + 16; // 92
        // Format 4: composite (axis0=700, axis1=-5) nameID 303
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t3 + 0, 2), 4);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t3 + 2, 2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t3 + 4, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t3 + 6, 2), 303);

        // AxisValueRecord[0]
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t3 + 8, 2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(t3 + 10, 4), 700u << 16);
        // AxisValueRecord[1]
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(t3 + 14, 2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(t3 + 16, 4), unchecked((uint)(-5 << 16)));

        return table;
    }

    private static void WriteTag(Span<byte> dst, string tag)
    {
        dst[0] = (byte)tag[0];
        dst[1] = (byte)tag[1];
        dst[2] = (byte)tag[2];
        dst[3] = (byte)tag[3];
    }

    private static void WriteFixed(Span<byte> dst, int mantissa)
        => BinaryPrimitives.WriteUInt32BigEndian(dst, unchecked((uint)(mantissa << 16)));

    private static void WriteF2Dot14Map(Span<byte> dst, short from, short to)
    {
        BinaryPrimitives.WriteInt16BigEndian(dst.Slice(0, 2), from);
        BinaryPrimitives.WriteInt16BigEndian(dst.Slice(2, 2), to);
    }
}

