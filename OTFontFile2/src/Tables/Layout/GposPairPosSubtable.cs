using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(8)]
[OtField("PosFormat", OtFieldKind.UInt16, 0)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 2)]
[OtField("ValueFormat1", OtFieldKind.UInt16, 4)]
[OtField("ValueFormat2", OtFieldKind.UInt16, 6)]
[OtDiscriminant(nameof(PosFormat))]
[OtCase(1, typeof(GposPairPosSubtable.Format1), Name = "Format1")]
[OtCase(2, typeof(GposPairPosSubtable.Format2), Name = "Format2")]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
public readonly partial struct GposPairPosSubtable
{
    public bool TryGetPairSetCount(out ushort pairSetCount)
    {
        pairSetCount = 0;

        if (!TryGetFormat1(out var format1))
            return false;

        pairSetCount = format1.PairSetCount;
        return true;
    }

    public bool TryGetPairSetOffset(int index, out ushort pairSetOffset)
    {
        pairSetOffset = 0;

        if (!TryGetFormat1(out var format1))
            return false;

        return format1.TryGetPairSetOffset(index, out pairSetOffset);
    }

    public bool TryGetPairSet(int index, out PairSetTable pairSet)
    {
        pairSet = default;

        if (!TryGetPairSetOffset(index, out ushort rel))
            return false;

        int offset = _offset + rel;
        return PairSetTable.TryCreate(_table, offset, _offset, ValueFormat1, ValueFormat2, out pairSet);
    }

    public bool TryGetPairSetForFirstGlyph(ushort firstGlyphId, out bool covered, out PairSetTable pairSet)
    {
        covered = false;
        pairSet = default;

        if (PosFormat != 1)
            return false;

        if (!TryGetCoverage(out var coverageTable))
            return false;

        if (!coverageTable.TryGetCoverage(firstGlyphId, out bool isCovered, out ushort coverageIndex))
            return false;

        if (!isCovered)
            return true;

        if (!TryGetPairSetCount(out ushort pairSetCount))
            return false;

        if (coverageIndex >= pairSetCount)
            return false;

        if (!TryGetPairSet(coverageIndex, out pairSet))
            return false;

        covered = true;
        return true;
    }

    public bool TryGetPairAdjustment(
        ushort firstGlyphId,
        ushort secondGlyphId,
        out bool positioned,
        out GposValueRecord value1,
        out GposValueRecord value2)
    {
        positioned = false;
        value1 = default;
        value2 = default;

        if (TryGetFormat1(out var format1))
        {
            if (!TryGetCoverage(out var coverageTable))
                return false;

            if (!coverageTable.TryGetCoverage(firstGlyphId, out bool covered, out ushort coverageIndex))
                return false;

            if (!covered)
                return true;

            ushort pairSetCount = format1.PairSetCount;
            if (coverageIndex >= pairSetCount)
                return false;

            if (!format1.TryGetPairSetOffset(coverageIndex, out ushort rel))
                return false;

            int offset = _offset + rel;
            if (!PairSetTable.TryCreate(_table, offset, _offset, ValueFormat1, ValueFormat2, out var pairSet))
                return false;

            if (!pairSet.TryFindPairValueRecord(secondGlyphId, out bool found, out var record))
                return false;

            if (!found)
                return true;

            if (!record.TryGetValue1(out value1))
                return false;
            if (!record.TryGetValue2(out value2))
                return false;

            positioned = true;
            return true;
        }

        if (TryGetFormat2(out var format2))
        {
            if (!TryGetCoverage(out var coverageTable))
                return false;

            if (!coverageTable.TryGetCoverage(firstGlyphId, out bool covered, out _))
                return false;

            if (!covered)
                return true;

            if (!format2.TryGetClassDef1(out var classDef1))
                return false;
            if (!format2.TryGetClassDef2(out var classDef2))
                return false;

            if (!classDef1.TryGetClass(firstGlyphId, out ushort class1))
                return false;
            if (!classDef2.TryGetClass(secondGlyphId, out ushort class2))
                return false;

            if (!TryGetClass2Record(format2, class1, class2, out var class2Record))
                return false;

            if (!class2Record.TryGetValue1(out value1))
                return false;
            if (!class2Record.TryGetValue2(out value2))
                return false;

            positioned = true;
            return true;
        }

        return false;
    }

    private bool TryGetClass2Record(Format2 format2, ushort class1, ushort class2, out Class2Record class2Record)
    {
        class2Record = default;

        ushort class1Count = format2.Class1Count;
        ushort class2Count = format2.Class2Count;
        if (class1 >= class1Count || class2 >= class2Count)
            return false;

        int value1Size = GposValueRecord.GetByteLength(ValueFormat1);
        int value2Size = GposValueRecord.GetByteLength(ValueFormat2);
        int class2RecordSize = value1Size + value2Size;

        long class1RecordSize = (long)class2Count * class2RecordSize;
        long start = (long)_offset + 16 + ((long)class1 * class1RecordSize) + ((long)class2 * class2RecordSize);
        long end = start + class2RecordSize;
        if (start < 0 || end < start || end > _table.Length)
            return false;

        class2Record = new Class2Record(_table, (int)start, _offset, ValueFormat1, ValueFormat2, value1Size);
        return true;
    }

    [OtSubTable(2, GenerateTryCreate = false, GenerateStorage = false)]
    [OtField("PairValueCount", OtFieldKind.UInt16, 0)]
    public readonly partial struct PairSetTable
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly int _posTableOffset;
        private readonly ushort _valueFormat1;
        private readonly ushort _valueFormat2;
        private readonly int _value1Size;
        private readonly int _value2Size;
        private readonly int _pairValueRecordSize;

        private PairSetTable(
            TableSlice gpos,
            int offset,
            int posTableOffset,
            ushort valueFormat1,
            ushort valueFormat2)
        {
            _table = gpos;
            _offset = offset;
            _posTableOffset = posTableOffset;
            _valueFormat1 = valueFormat1;
            _valueFormat2 = valueFormat2;

            _value1Size = GposValueRecord.GetByteLength(valueFormat1);
            _value2Size = GposValueRecord.GetByteLength(valueFormat2);
            _pairValueRecordSize = 2 + _value1Size + _value2Size;
        }

        public static bool TryCreate(
            TableSlice gpos,
            int offset,
            int posTableOffset,
            ushort valueFormat1,
            ushort valueFormat2,
            out PairSetTable pairSet)
        {
            if ((uint)offset > (uint)gpos.Length - 2)
            {
                pairSet = default;
                return false;
            }

            pairSet = new PairSetTable(gpos, offset, posTableOffset, valueFormat1, valueFormat2);
            return true;
        }

        public bool TryGetPairValueRecord(int index, out PairValueRecord record)
        {
            record = default;

            ushort count = PairValueCount;
            if ((uint)index >= (uint)count)
                return false;

            long o = (long)_offset + 2 + ((long)index * _pairValueRecordSize);
            if (o < 0 || o > _table.Length - 2)
                return false;

            record = new PairValueRecord(_table, (int)o, _posTableOffset, _valueFormat1, _valueFormat2, _value1Size);
            return true;
        }

        public bool TryFindPairValueRecord(ushort secondGlyphId, out bool found, out PairValueRecord record)
        {
            found = false;
            record = default;

            ushort count = PairValueCount;
            if (count == 0)
                return true;

            var data = _table.Span;

            int lo = 0;
            int hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int o = _offset + 2 + (mid * _pairValueRecordSize);
                if ((uint)o > (uint)_table.Length - 2)
                    return false;

                ushort midSecond = BigEndian.ReadUInt16(data, o);
                if (secondGlyphId < midSecond)
                {
                    hi = mid - 1;
                    continue;
                }

                if (secondGlyphId > midSecond)
                {
                    lo = mid + 1;
                    continue;
                }

                record = new PairValueRecord(_table, o, _posTableOffset, _valueFormat1, _valueFormat2, _value1Size);
                found = true;
                return true;
            }

            return true;
        }
    }

    [OtSubTable(2, GenerateTryCreate = false, GenerateStorage = false)]
    [OtField("SecondGlyphId", OtFieldKind.UInt16, 0)]
    public readonly partial struct PairValueRecord
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly int _posTableOffset;
        private readonly ushort _valueFormat1;
        private readonly ushort _valueFormat2;
        private readonly int _value1Size;

        internal PairValueRecord(
            TableSlice gpos,
            int offset,
            int posTableOffset,
            ushort valueFormat1,
            ushort valueFormat2,
            int value1Size)
        {
            _table = gpos;
            _offset = offset;
            _posTableOffset = posTableOffset;
            _valueFormat1 = valueFormat1;
            _valueFormat2 = valueFormat2;
            _value1Size = value1Size;
        }

        public bool TryGetValue1(out GposValueRecord value1)
        {
            int offset = _offset + 2;
            return GposValueRecord.TryCreate(_table, offset, _posTableOffset, _valueFormat1, out value1);
        }

        public bool TryGetValue2(out GposValueRecord value2)
        {
            int offset = _offset + 2 + _value1Size;
            return GposValueRecord.TryCreate(_table, offset, _posTableOffset, _valueFormat2, out value2);
        }
    }

    public bool TryGetClassDef1(out ClassDefTable classDef)
    {
        classDef = default;

        return TryGetFormat2(out var format2) && format2.TryGetClassDef1(out classDef);
    }

    public bool TryGetClassDef2(out ClassDefTable classDef)
    {
        classDef = default;

        return TryGetFormat2(out var format2) && format2.TryGetClassDef2(out classDef);
    }

    public bool TryGetClassCounts(out ushort class1Count, out ushort class2Count)
    {
        class1Count = 0;
        class2Count = 0;

        if (!TryGetFormat2(out var format2))
            return false;

        class1Count = format2.Class1Count;
        class2Count = format2.Class2Count;
        return true;
    }

    public bool TryGetClass1Record(int index, out Class1Record class1Record)
    {
        class1Record = default;

        if (!TryGetFormat2(out var format2))
            return false;

        ushort class1Count = format2.Class1Count;
        ushort class2Count = format2.Class2Count;

        if ((uint)index >= class1Count)
            return false;

        int value1Size = GposValueRecord.GetByteLength(ValueFormat1);
        int value2Size = GposValueRecord.GetByteLength(ValueFormat2);
        int class2RecordSize = value1Size + value2Size;

        long class1RecordSize = (long)class2Count * class2RecordSize;
        long start = (long)_offset + 16 + ((long)index * class1RecordSize);
        long end = start + class1RecordSize;
        if (start < 0 || end < start || end > _table.Length)
            return false;

        class1Record = new Class1Record(_table, (int)start, _offset, class2Count, ValueFormat1, ValueFormat2);
        return true;
    }

    public bool TryGetClass2Record(ushort class1, ushort class2, out Class2Record class2Record)
    {
        if (!TryGetFormat2(out var format2))
        {
            class2Record = default;
            return false;
        }

        return TryGetClass2Record(format2, class1, class2, out class2Record);
    }

    public readonly struct Class1Record
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly int _posTableOffset;
        private readonly ushort _class2Count;
        private readonly ushort _valueFormat1;
        private readonly ushort _valueFormat2;
        private readonly int _value1Size;
        private readonly int _class2RecordSize;

        internal Class1Record(
            TableSlice gpos,
            int offset,
            int posTableOffset,
            ushort class2Count,
            ushort valueFormat1,
            ushort valueFormat2)
        {
            _table = gpos;
            _offset = offset;
            _posTableOffset = posTableOffset;
            _class2Count = class2Count;
            _valueFormat1 = valueFormat1;
            _valueFormat2 = valueFormat2;
            _value1Size = GposValueRecord.GetByteLength(valueFormat1);
            _class2RecordSize = _value1Size + GposValueRecord.GetByteLength(valueFormat2);
        }

        public TableSlice Table => _table;
        public int Offset => _offset;
        public ushort Class2Count => _class2Count;

        public bool TryGetClass2Record(int index, out Class2Record class2Record)
        {
            class2Record = default;

            if ((uint)index >= (uint)_class2Count)
                return false;

            long start = (long)_offset + ((long)index * _class2RecordSize);
            long end = start + _class2RecordSize;
            if (start < 0 || end < start || end > _table.Length)
                return false;

            class2Record = new Class2Record(_table, (int)start, _posTableOffset, _valueFormat1, _valueFormat2, _value1Size);
            return true;
        }
    }

    public readonly struct Class2Record
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly int _posTableOffset;
        private readonly ushort _valueFormat1;
        private readonly ushort _valueFormat2;
        private readonly int _value1Size;

        internal Class2Record(
            TableSlice gpos,
            int offset,
            int posTableOffset,
            ushort valueFormat1,
            ushort valueFormat2,
            int value1Size)
        {
            _table = gpos;
            _offset = offset;
            _posTableOffset = posTableOffset;
            _valueFormat1 = valueFormat1;
            _valueFormat2 = valueFormat2;
            _value1Size = value1Size;
        }

        public TableSlice Table => _table;
        public int Offset => _offset;

        public bool TryGetValue1(out GposValueRecord value1)
            => GposValueRecord.TryCreate(_table, _offset, _posTableOffset, _valueFormat1, out value1);

        public bool TryGetValue2(out GposValueRecord value2)
        {
            int offset = _offset + _value1Size;
            return GposValueRecord.TryCreate(_table, offset, _posTableOffset, _valueFormat2, out value2);
        }
    }

    [OtSubTable(10)]
    [OtField("PairSetCount", OtFieldKind.UInt16, 8)]
    [OtUInt16Array("PairSetOffset", 10, CountPropertyName = nameof(PairSetCount))]
    public readonly partial struct Format1
    {
    }

    [OtSubTable(16)]
    [OtField("ClassDef1Offset", OtFieldKind.UInt16, 8)]
    [OtField("ClassDef2Offset", OtFieldKind.UInt16, 10)]
    [OtField("Class1Count", OtFieldKind.UInt16, 12)]
    [OtField("Class2Count", OtFieldKind.UInt16, 14)]
    [OtSubTableOffset("ClassDef1", nameof(ClassDef1Offset), typeof(ClassDefTable), OutParameterName = "classDef")]
    [OtSubTableOffset("ClassDef2", nameof(ClassDef2Offset), typeof(ClassDefTable), OutParameterName = "classDef")]
    public readonly partial struct Format2
    {
    }
}
