using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class FontModelTests
{
    [TestMethod]
    public void FontModel_CanEditNameAndWriteBack()
    {
        var nameBuilder = new NameTableBuilder(format: 0);
        nameBuilder.AddOrReplaceString(
            platformId: (ushort)NameTable.PlatformId.Windows,
            encodingId: 1,
            languageId: 0x0409,
            nameId: (ushort)NameTable.NameId.FullName,
            value: "OldFamily Regular");

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(nameBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<NameTableBuilder>(out var edit));
        edit.AddOrReplaceString(
            platformId: (ushort)NameTable.PlatformId.Windows,
            encodingId: 1,
            languageId: 0x0409,
            nameId: (ushort)NameTable.NameId.FullName,
            value: "NewFamily Regular");

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetName(out var editedName));
        Assert.AreEqual("NewFamily Regular", editedName.GetFullNameString());
    }

    [TestMethod]
    public void FontModel_CanEditHdmx_WithMaxpDependency()
    {
        const ushort numGlyphs = 4;

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);
        byte[] maxp = BuildMaxpTable(numGlyphs);

        var hdmxBuilder = new HdmxTableBuilder(numGlyphs) { Version = 0 };
        hdmxBuilder.AddOrReplaceRecord(pixelSize: 12, widths: new byte[] { 1, 2, 3, 4 });

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(KnownTags.maxp, maxp);
        sfnt.SetTable(hdmxBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<HdmxTableBuilder>(out var edit));
        edit.AddOrReplaceRecord(pixelSize: 12, widths: new byte[] { 9, 9, 9, 9 });

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.AreEqual(numGlyphs, editedMaxp.NumGlyphs);

        Assert.IsTrue(editedFont.TryGetHdmx(out var editedHdmx));
        Assert.IsTrue(editedHdmx.TryGetDeviceRecord(0, out var record));
        Assert.IsTrue(record.TryGetWidths(numGlyphs, out var widths));
        CollectionAssert.AreEqual(new byte[] { 9, 9, 9, 9 }, widths.ToArray());
    }

    [TestMethod]
    public void FontModel_CanEditVorgAndWriteBack()
    {
        var vorgBuilder = new VorgTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0,
            DefaultVertOriginY = 100
        };
        vorgBuilder.AddOrReplaceMetric(glyphIndex: 5, vertOriginY: 200);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x4F54544Fu }; // 'OTTO'
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(vorgBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<VorgTableBuilder>(out var edit));
        edit.DefaultVertOriginY = 123;
        edit.AddOrReplaceMetric(glyphIndex: 5, vertOriginY: 222);
        edit.AddOrReplaceMetric(glyphIndex: 20, vertOriginY: 333);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetVorg(out var editedVorg));
        Assert.AreEqual((short)123, editedVorg.DefaultVertOriginY);
        Assert.AreEqual((ushort)2, editedVorg.MetricCount);
        Assert.IsTrue(editedVorg.TryGetVertOriginY(glyphIndex: 5, out short y5));
        Assert.AreEqual((short)222, y5);
        Assert.IsTrue(editedVorg.TryGetVertOriginY(glyphIndex: 20, out short y20));
        Assert.AreEqual((short)333, y20);
    }

    [TestMethod]
    public void FontModel_CanEditFvarAndWriteBack()
    {
        Assert.IsTrue(Tag.TryParse("wght", out var wght));

        var fvarBuilder = new FvarTableBuilder
        {
            Version = new Fixed1616(0x00010000u),
            WritePostScriptNameId = false
        };

        fvarBuilder.AddAxis(
            axisTag: wght,
            minValue: new Fixed1616(100u << 16),
            defaultValue: new Fixed1616(400u << 16),
            maxValue: new Fixed1616(900u << 16),
            flags: 0,
            axisNameId: 256);

        fvarBuilder.AddInstance(
            subfamilyNameId: 300,
            flags: 0,
            coordinates: new[] { new Fixed1616(400u << 16) });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(fvarBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<FvarTableBuilder>(out var edit));
        edit.WritePostScriptNameId = true;
        edit.ClearInstances();
        edit.AddInstance(
            subfamilyNameId: 310,
            flags: 0,
            coordinates: new[] { new Fixed1616(700u << 16) },
            postScriptNameId: 311);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetFvar(out var editedFvar));
        Assert.IsTrue(editedFvar.TryGetInstanceRecord(0, out var inst0));
        Assert.AreEqual((ushort)310, inst0.SubfamilyNameId);
        Assert.IsTrue(inst0.TryGetPostScriptNameId(out ushort psNameId));
        Assert.AreEqual((ushort)311, psNameId);
    }

    [TestMethod]
    public void FontModel_CanEditAvarAndWriteBack()
    {
        var avarBuilder = new AvarTableBuilder();
        avarBuilder.AddAxis();

        var neg1 = new F2Dot14(-16384);
        var zero = new F2Dot14(0);
        var pos1 = new F2Dot14(16384);

        avarBuilder.AddMap(axisIndex: 0, fromCoordinate: neg1, toCoordinate: neg1);
        avarBuilder.AddMap(axisIndex: 0, fromCoordinate: zero, toCoordinate: zero);
        avarBuilder.AddMap(axisIndex: 0, fromCoordinate: pos1, toCoordinate: pos1);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(avarBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<AvarTableBuilder>(out var edit));
        edit.ClearAxisMaps(0);

        var pos08 = new F2Dot14(13107); // ~0.8
        edit.AddMap(axisIndex: 0, fromCoordinate: neg1, toCoordinate: neg1);
        edit.AddMap(axisIndex: 0, fromCoordinate: zero, toCoordinate: zero);
        edit.AddMap(axisIndex: 0, fromCoordinate: pos1, toCoordinate: pos08);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetAvar(out var editedAvar));
        Assert.IsTrue(editedAvar.TryGetSegmentMap(0, out var seg0));
        Assert.IsTrue(seg0.TryGetAxisValueMap(2, out var last));
        Assert.AreEqual(pos08.RawValue, last.ToCoordinate.RawValue);
    }

    [TestMethod]
    public void FontModel_CanEditStatAndWriteBack()
    {
        Assert.IsTrue(Tag.TryParse("wght", out var wght));

        var statBuilder = new StatTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0,
            ElidedFallbackNameId = 0
        };
        statBuilder.AddDesignAxis(axisTag: wght, axisNameId: 256, axisOrdering: 0);
        statBuilder.AddAxisValueFormat1(
            axisIndex: 0,
            flags: 0,
            valueNameId: 300,
            value: new Fixed1616(400u << 16));

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(statBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<StatTableBuilder>(out var edit));
        edit.ElidedFallbackNameId = 1234;
        edit.ClearAxisValues();
        edit.AddAxisValueFormat2(
            axisIndex: 0,
            flags: 0,
            valueNameId: 301,
            nominalValue: new Fixed1616(500u << 16),
            rangeMinValue: new Fixed1616(400u << 16),
            rangeMaxValue: new Fixed1616(600u << 16));

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetStat(out var editedStat));
        Assert.AreEqual((ushort)1234, editedStat.ElidedFallbackNameId);
        Assert.IsTrue(editedStat.TryGetAxisValueTable(0, out var v0));
        Assert.AreEqual((ushort)2, v0.Format);
        Assert.IsTrue(v0.TryGetFormat2(out var f2));
        Assert.AreEqual((500u << 16), f2.NominalValue.RawValue);
    }

    [TestMethod]
    public void FontModel_CanEditMvarAndWriteBack()
    {
        Assert.IsTrue(Tag.TryParse("test", out var test));

        var mvarBuilder = new MvarTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0
        };
        mvarBuilder.AddValueRecord(test, new VarIdx(outerIndex: 0, innerIndex: 1));
        mvarBuilder.SetMinimalItemVariationStore(axisCount: 0);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(mvarBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<MvarTableBuilder>(out var edit));
        edit.ClearValueRecords();
        edit.AddValueRecord(test, new VarIdx(outerIndex: 2, innerIndex: 3));
        edit.SetMinimalItemVariationStore(axisCount: 1);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetMvar(out var editedMvar));
        Assert.IsTrue(editedMvar.TryGetValueRecord(0, out var record0));
        Assert.AreEqual(new VarIdx(2, 3), record0.DeltaSetIndex);
    }

    [TestMethod]
    public void FontModel_CanEditHvarAndWriteBack()
    {
        byte[] mapBytes = new byte[6];
        mapBytes[0] = 0; // format
        mapBytes[1] = 0x10; // entrySize=2, innerIndexBitCount=1
        BinaryPrimitives.WriteUInt16BigEndian(mapBytes.AsSpan(2, 2), 1); // mapCount

        var hvarBuilder = new HvarTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0
        };
        hvarBuilder.SetMinimalItemVariationStore(axisCount: 0);
        hvarBuilder.SetAdvanceWidthMapping(mapBytes);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(hvarBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<HvarTableBuilder>(out var edit));
        edit.ClearAdvanceWidthMapping();
        edit.SetRsbMapping(mapBytes);
        edit.SetMinimalItemVariationStore(axisCount: 1);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetHvar(out var editedHvar));
        Assert.IsFalse(editedHvar.TryGetAdvanceWidthMapping(out _));
        Assert.IsTrue(editedHvar.TryGetRsbMapping(out var rsbMap));
        Assert.AreEqual((ushort)1, rsbMap.MapCount);
    }

    [TestMethod]
    public void FontModel_CanEditVvarAndWriteBack()
    {
        byte[] mapBytes = new byte[6];
        mapBytes[0] = 0; // format
        mapBytes[1] = 0x10; // entrySize=2, innerIndexBitCount=1
        BinaryPrimitives.WriteUInt16BigEndian(mapBytes.AsSpan(2, 2), 1); // mapCount

        var vvarBuilder = new VvarTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0
        };
        vvarBuilder.SetMinimalItemVariationStore(axisCount: 0);
        vvarBuilder.SetAdvanceHeightMapping(mapBytes);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(vvarBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<VvarTableBuilder>(out var edit));
        edit.ClearAdvanceHeightMapping();
        edit.SetTsbMapping(mapBytes);
        edit.SetMinimalItemVariationStore(axisCount: 1);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetVvar(out var editedVvar));
        Assert.IsFalse(editedVvar.TryGetAdvanceHeightMapping(out _));
        Assert.IsTrue(editedVvar.TryGetTsbMapping(out var tsbMap));
        Assert.AreEqual((ushort)1, tsbMap.MapCount);
    }

    [TestMethod]
    public void FontModel_CanEditSvgAndWriteBack()
    {
        var svgBuilder = new SvgTableBuilder
        {
            Version = 0,
            Reserved = 0
        };
        svgBuilder.AddDocument(startGlyphId: 5, endGlyphId: 5, documentBytes: new byte[] { 1, 2, 3 });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(svgBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<SvgTableBuilder>(out var edit));
        edit.Clear();
        edit.Reserved = 999;
        edit.AddDocument(startGlyphId: 10, endGlyphId: 20, documentBytes: new byte[] { 9 });

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetSvg(out var editedSvg));
        Assert.AreEqual((uint)999, editedSvg.Reserved);
        Assert.IsTrue(editedSvg.TryGetDocumentSpan(0, out var docBytes));
        CollectionAssert.AreEqual(new byte[] { 9 }, docBytes.ToArray());
    }

    [TestMethod]
    public void FontModel_CanEditEbscAndWriteBack()
    {
        var ebscBuilder = new EbscTableBuilder
        {
            Version = new Fixed1616(0x00020000u)
        };

        ebscBuilder.AddScale(
            hori: new SbitLineMetricsData(1, -2, 3, 4, 5, 6, -7, -8, 9, -10),
            vert: new SbitLineMetricsData(11, -12, 13, 14, 15, 16, -17, -18, 19, -20),
            ppemX: 12,
            ppemY: 13,
            substitutePpemX: 14,
            substitutePpemY: 15);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(ebscBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<EbscTableBuilder>(out var edit));
        edit.Clear();
        edit.AddScale(
            hori: new SbitLineMetricsData(21, -22, 23, 24, 25, 26, -27, -28, 29, -30),
            vert: new SbitLineMetricsData(31, -32, 33, 34, 35, 36, -37, -38, 39, -40),
            ppemX: 22,
            ppemY: 23,
            substitutePpemX: 24,
            substitutePpemY: 25);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetEbsc(out var editedEbsc));
        Assert.IsTrue(editedEbsc.TryGetBitmapScale(0, out var scale));
        Assert.AreEqual((sbyte)21, scale.Hori.Ascender);
        Assert.AreEqual((byte)22, scale.PpemX);
    }

    [TestMethod]
    public void FontModel_CanEditEbdtAndWriteBack()
    {
        var ebdtBuilder = new EbdtTableBuilder
        {
            Version = new Fixed1616(0x00020000u)
        };
        ebdtBuilder.SetPayload(new byte[] { 0xAA, 0xBB });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(ebdtBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<EbdtTableBuilder>(out var edit));
        edit.SetPayload(new byte[] { 1, 2, 3, 4 });

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetEbdt(out var ebdt));
        Assert.IsTrue(ebdt.TryGetGlyphSpan(offset: 4, length: 4, out var glyphBytes));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, glyphBytes.ToArray());
    }

    [TestMethod]
    public void FontModel_CanEditEblcAndWriteBack()
    {
        var eblcBuilder = new EblcTableBuilder
        {
            Version = new Fixed1616(0x00020000u),
            BitmapSizeTableCount = 0
        };

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(eblcBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<EblcTableBuilder>(out var edit));
        edit.SetBody(bitmapSizeTableCount: 0, bodyBytes: new byte[] { 9, 8, 7 });

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetTableSlice(KnownTags.EBLC, out var slice));
        CollectionAssert.AreEqual(edit.ToArray(), slice.Span.ToArray());
    }

    [TestMethod]
    public void FontModel_CanEditZapfAndWriteBack_WithMaxpDependency()
    {
        const ushort numGlyphs = 3;

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);
        byte[] maxp = BuildMaxpTable(numGlyphs);

        var zapfBuilder = new ZapfTableBuilder(numGlyphs)
        {
            Version = new Fixed1616(0x00010000u),
            ExtraInfo = 0
        };
        zapfBuilder.SetGlyphInfoOffset(glyphId: 1, glyphInfoOffset: 123u);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(KnownTags.maxp, maxp);
        sfnt.SetTable(zapfBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<ZapfTableBuilder>(out var edit));
        edit.ExtraInfo = 999;
        edit.SetGlyphInfoOffset(glyphId: 2, glyphInfoOffset: 456u);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetZapf(out var zapf));
        Assert.AreEqual(999u, zapf.ExtraInfo);
        Assert.IsTrue(zapf.TryGetGlyphInfoOffset(glyphId: 2, glyphCount: numGlyphs, out uint off2));
        Assert.AreEqual(456u, off2);
    }

    [TestMethod]
    public void FontModel_CanEditBaseAndWriteBack()
    {
        var baseBuilder = new BaseTableBuilder
        {
            Version = new Fixed1616(0x00010000u),
            HorizAxisOffset = 0,
            VertAxisOffset = 0
        };

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(baseBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<BaseTableBuilder>(out var edit));
        edit.Version = new Fixed1616(0x00020000u);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetBase(out var editedBase));
        Assert.AreEqual(0x00020000u, editedBase.Version.RawValue);
    }

    [TestMethod]
    public void FontModel_CanEditJstfAndWriteBack()
    {
        var jstfBuilder = new JstfTableBuilder();

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(jstfBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<JstfTableBuilder>(out var edit));

        byte[] bytes = edit.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 0x00020000u);
        edit.SetTableData(bytes);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetJstf(out var editedJstf));
        Assert.AreEqual(0x00020000u, editedJstf.Version.RawValue);
    }

    [TestMethod]
    public void FontModel_StaleDependencies_AreRejectedByDefault()
    {
        string path = GetFontPath("small.ttf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        var model = new FontModel(font);

        // Changing glyf without rebuilding loca would produce an inconsistent font.
        model.SetTable(KnownTags.glyf, new byte[] { 0, 1, 2, 3 });

        var ex = Assert.ThrowsException<InvalidOperationException>(() => model.ToArray());
        StringAssert.Contains(ex.Message, "loca");
    }

    private static byte[] BuildMaxpTable(ushort numGlyphs)
    {
        byte[] maxp = new byte[6];
        var span = maxp.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010000u); // version 1.0
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), numGlyphs);

        return maxp;
    }

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);
}
