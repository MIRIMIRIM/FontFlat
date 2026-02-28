namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS PairPos subtables (lookup type 2), format 2 (class-based).
/// </summary>
public sealed class GposPairPosClassSubtableBuilder
{
    private readonly CoverageTableBuilder _coverage = new();
    private ClassDefTableBuilder? _classDef1;
    private ClassDefTableBuilder? _classDef2;

    private ushort _class1Count = 1;
    private ushort _class2Count = 1;

    private readonly Dictionary<uint, PairValue> _values = new();

    private bool _dirty = true;
    private byte[]? _built;

    public void Clear()
    {
        _coverage.Clear();
        _classDef1 = null;
        _classDef2 = null;
        _class1Count = 1;
        _class2Count = 1;
        _values.Clear();
        MarkDirty();
    }

    public CoverageTableBuilder Coverage => _coverage;

    public void SetClassDef1(ClassDefTableBuilder classDef)
    {
        _classDef1 = classDef ?? throw new ArgumentNullException(nameof(classDef));
        MarkDirty();
    }

    public void SetClassDef2(ClassDefTableBuilder classDef)
    {
        _classDef2 = classDef ?? throw new ArgumentNullException(nameof(classDef));
        MarkDirty();
    }

    public void SetClassCounts(ushort class1Count, ushort class2Count)
    {
        if (class1Count == 0) throw new ArgumentOutOfRangeException(nameof(class1Count));
        if (class2Count == 0) throw new ArgumentOutOfRangeException(nameof(class2Count));

        if (class1Count == _class1Count && class2Count == _class2Count)
            return;

        _class1Count = class1Count;
        _class2Count = class2Count;
        MarkDirty();
    }

    public void SetPairValue(ushort class1, ushort class2, GposValueRecordBuilder? value1 = null, GposValueRecordBuilder? value2 = null)
    {
        if (class1 >= _class1Count) throw new ArgumentOutOfRangeException(nameof(class1));
        if (class2 >= _class2Count) throw new ArgumentOutOfRangeException(nameof(class2));

        _values[Key(class1, class2)] = new PairValue(CloneOrEmpty(value1), CloneOrEmpty(value2));
        MarkDirty();
    }

    public bool RemovePairValue(ushort class1, ushort class2)
    {
        if (class1 >= _class1Count || class2 >= _class2Count)
            return false;

        bool removed = _values.Remove(Key(class1, class2));
        if (removed)
            MarkDirty();

        return removed;
    }

    public byte[] ToArray()
    {
        EnsureBuilt();
        return _built!;
    }

    public ReadOnlyMemory<byte> ToMemory() => EnsureBuilt();

    private void MarkDirty()
    {
        _dirty = true;
        _built = null;
    }

    private ReadOnlyMemory<byte> EnsureBuilt()
    {
        if (!_dirty && _built is not null)
            return _built;

        _built = BuildBytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildBytes()
    {
        if (_classDef1 is null)
            throw new InvalidOperationException("PairPos format 2 requires ClassDef1.");
        if (_classDef2 is null)
            throw new InvalidOperationException("PairPos format 2 requires ClassDef2.");

        ushort class1Count = _class1Count;
        ushort class2Count = _class2Count;

        ushort valueFormat1 = 0;
        ushort valueFormat2 = 0;
        foreach (var kvp in _values)
        {
            valueFormat1 |= kvp.Value.Value1.GetValueFormat();
            valueFormat2 |= kvp.Value.Value2.GetValueFormat();
        }

        byte[] coverageBytes = _coverage.ToArray();
        byte[] classDef1Bytes = _classDef1.ToArray();
        byte[] classDef2Bytes = _classDef2.ToArray();

        var w = new OTFontFile2.OffsetWriter();
        var devices = new DeviceTablePool();

        var coverageLabel = w.CreateLabel();
        var classDef1Label = w.CreateLabel();
        var classDef2Label = w.CreateLabel();

        w.WriteUInt16(2);
        w.WriteOffset16(coverageLabel, baseOffset: 0);
        w.WriteUInt16(valueFormat1);
        w.WriteUInt16(valueFormat2);
        w.WriteOffset16(classDef1Label, baseOffset: 0);
        w.WriteOffset16(classDef2Label, baseOffset: 0);
        w.WriteUInt16(class1Count);
        w.WriteUInt16(class2Count);

        for (ushort c1 = 0; c1 < class1Count; c1++)
        {
            for (ushort c2 = 0; c2 < class2Count; c2++)
            {
                if (_values.TryGetValue(Key(c1, c2), out var v))
                {
                    v.Value1.WriteTo(w, valueFormat1, posTableBaseOffset: 0, devices);
                    v.Value2.WriteTo(w, valueFormat2, posTableBaseOffset: 0, devices);
                }
                else
                {
                    WriteZeroValueRecord(w, valueFormat1);
                    WriteZeroValueRecord(w, valueFormat2);
                }
            }
        }

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        w.Align2();
        w.DefineLabelHere(classDef1Label);
        w.WriteBytes(classDef1Bytes);

        w.Align2();
        w.DefineLabelHere(classDef2Label);
        w.WriteBytes(classDef2Bytes);

        devices.EmitAllAligned2(w);
        return w.ToArray();
    }

    private static uint Key(ushort class1, ushort class2) => ((uint)class1 << 16) | class2;

    private static void WriteZeroValueRecord(OTFontFile2.OffsetWriter w, ushort valueFormat)
    {
        if ((valueFormat & 0x00FF) == 0)
            return;

        for (int bit = 0; bit < 8; bit++)
        {
            if (((valueFormat >> bit) & 1) != 0)
                w.WriteUInt16(0);
        }
    }

    private static GposValueRecordBuilder CloneOrEmpty(GposValueRecordBuilder? source)
    {
        var b = new GposValueRecordBuilder();
        if (source is null)
            return b;

        if (source.HasXPlacement) b.XPlacement = source.XPlacement;
        if (source.HasYPlacement) b.YPlacement = source.YPlacement;
        if (source.HasXAdvance) b.XAdvance = source.XAdvance;
        if (source.HasYAdvance) b.YAdvance = source.YAdvance;

        b.XPlacementDevice = source.XPlacementDevice;
        b.YPlacementDevice = source.YPlacementDevice;
        b.XAdvanceDevice = source.XAdvanceDevice;
        b.YAdvanceDevice = source.YAdvanceDevice;

        return b;
    }

    private readonly struct PairValue
    {
        public GposValueRecordBuilder Value1 { get; }
        public GposValueRecordBuilder Value2 { get; }

        public PairValue(GposValueRecordBuilder value1, GposValueRecordBuilder value2)
        {
            Value1 = value1;
            Value2 = value2;
        }
    }
}

