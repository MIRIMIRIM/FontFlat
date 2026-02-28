using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>fvar</c> table.
/// </summary>
[OtTableBuilder("fvar")]
public sealed partial class FvarTableBuilder : ISfntTableSource
{
    private readonly List<AxisRecord> _axes = new();
    private readonly List<InstanceRecord> _instances = new();

    private Fixed1616 _version = new(0x00010000u); // 1.0
    private bool _writePostScriptNameId;

    public Fixed1616 Version
    {
        get => _version;
        set
        {
            if (value.RawValue == _version.RawValue)
                return;

            _version = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// If true, write the optional <c>postScriptNameID</c> field in each instance record.
    /// </summary>
    public bool WritePostScriptNameId
    {
        get => _writePostScriptNameId;
        set
        {
            if (value == _writePostScriptNameId)
                return;

            _writePostScriptNameId = value;
            MarkDirty();
        }
    }

    public int AxisCount => _axes.Count;

    public IReadOnlyList<AxisRecord> Axes => _axes;

    public int InstanceCount => _instances.Count;

    public IReadOnlyList<InstanceRecord> Instances => _instances;

    public void ClearAxes()
    {
        if (_axes.Count == 0)
            return;

        _axes.Clear();
        MarkDirty();
    }

    public void AddAxis(Tag axisTag, Fixed1616 minValue, Fixed1616 defaultValue, Fixed1616 maxValue, ushort flags, ushort axisNameId)
    {
        _axes.Add(new AxisRecord(axisTag, minValue, defaultValue, maxValue, flags, axisNameId));
        MarkDirty();
    }

    public void ClearInstances()
    {
        if (_instances.Count == 0)
            return;

        _instances.Clear();
        MarkDirty();
    }

    public void AddInstance(ushort subfamilyNameId, ushort flags, ReadOnlySpan<Fixed1616> coordinates, ushort postScriptNameId = 0)
    {
        int axisCount = _axes.Count;
        if (coordinates.Length != axisCount)
            throw new ArgumentException($"Coordinates length ({coordinates.Length}) must match axisCount ({axisCount}).", nameof(coordinates));

        var coords = new Fixed1616[axisCount];
        for (int i = 0; i < axisCount; i++)
            coords[i] = coordinates[i];

        _instances.Add(new InstanceRecord(subfamilyNameId, flags, coords, postScriptNameId));
        MarkDirty();
    }

    public static bool TryFrom(FvarTable fvar, out FvarTableBuilder builder)
    {
        builder = new FvarTableBuilder
        {
            Version = fvar.Version,
            WritePostScriptNameId = fvar.InstanceSize >= (4 + (fvar.AxisCount * 4) + 2)
        };

        int axisCount = fvar.AxisCount;
        for (int i = 0; i < axisCount; i++)
        {
            if (!fvar.TryGetAxisRecord(i, out var axis))
                return false;

            builder._axes.Add(new AxisRecord(axis.AxisTag, axis.MinValue, axis.DefaultValue, axis.MaxValue, axis.Flags, axis.AxisNameId));
        }

        int instanceCount = fvar.InstanceCount;
        for (int i = 0; i < instanceCount; i++)
        {
            if (!fvar.TryGetInstanceRecord(i, out var instance))
                return false;

            var coords = new Fixed1616[axisCount];
            for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
            {
                if (!instance.TryGetCoordinate(axisIndex, out coords[axisIndex]))
                    return false;
            }

            ushort psNameId = 0;
            if (builder.WritePostScriptNameId && instance.TryGetPostScriptNameId(out ushort id))
                psNameId = id;

            builder._instances.Add(new InstanceRecord(instance.SubfamilyNameId, instance.Flags, coords, psNameId));
        }

        builder.MarkDirty();
        return true;
    }

    private byte[] BuildTable()
    {
        if ((Version.RawValue >> 16) != 1)
            throw new InvalidOperationException("fvar version major must be 1.");

        if (_axes.Count > ushort.MaxValue)
            throw new InvalidOperationException("fvar axisCount must fit in uint16.");

        if (_instances.Count > ushort.MaxValue)
            throw new InvalidOperationException("fvar instanceCount must fit in uint16.");

        ushort axisCount = checked((ushort)_axes.Count);
        const ushort axisSize = 20;
        ushort instanceSize = checked((ushort)(4 + (axisCount * 4) + (WritePostScriptNameId ? 2 : 0)));

        const ushort axesArrayOffset = 16;
        int axesBytes = checked(axisCount * axisSize);
        int instancesOffset = checked(axesArrayOffset + axesBytes);
        int instancesBytes = checked(_instances.Count * instanceSize);
        int length = checked(instancesOffset + instancesBytes);

        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt32(span, 0, Version.RawValue);
        BigEndian.WriteUInt16(span, 4, axesArrayOffset);
        BigEndian.WriteUInt16(span, 6, 0); // reserved
        BigEndian.WriteUInt16(span, 8, axisCount);
        BigEndian.WriteUInt16(span, 10, axisSize);
        BigEndian.WriteUInt16(span, 12, checked((ushort)_instances.Count));
        BigEndian.WriteUInt16(span, 14, instanceSize);

        int pos = axesArrayOffset;
        for (int i = 0; i < axisCount; i++)
        {
            var a = _axes[i];
            BigEndian.WriteUInt32(span, pos + 0, a.AxisTag.Value);
            BigEndian.WriteUInt32(span, pos + 4, a.MinValue.RawValue);
            BigEndian.WriteUInt32(span, pos + 8, a.DefaultValue.RawValue);
            BigEndian.WriteUInt32(span, pos + 12, a.MaxValue.RawValue);
            BigEndian.WriteUInt16(span, pos + 16, a.Flags);
            BigEndian.WriteUInt16(span, pos + 18, a.AxisNameId);
            pos += axisSize;
        }

        pos = instancesOffset;
        for (int i = 0; i < _instances.Count; i++)
        {
            var inst = _instances[i];
            if (inst.Coordinates.Length != axisCount)
                throw new InvalidOperationException("fvar instance coordinate count must match axisCount.");

            BigEndian.WriteUInt16(span, pos + 0, inst.SubfamilyNameId);
            BigEndian.WriteUInt16(span, pos + 2, inst.Flags);

            int coordPos = pos + 4;
            for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
            {
                BigEndian.WriteUInt32(span, coordPos, inst.Coordinates[axisIndex].RawValue);
                coordPos += 4;
            }

            if (WritePostScriptNameId)
            {
                BigEndian.WriteUInt16(span, coordPos, inst.PostScriptNameId);
            }

            pos = checked(pos + instanceSize);
        }

        return table;
    }

    public readonly struct AxisRecord
    {
        public Tag AxisTag { get; }
        public Fixed1616 MinValue { get; }
        public Fixed1616 DefaultValue { get; }
        public Fixed1616 MaxValue { get; }
        public ushort Flags { get; }
        public ushort AxisNameId { get; }

        public AxisRecord(Tag axisTag, Fixed1616 minValue, Fixed1616 defaultValue, Fixed1616 maxValue, ushort flags, ushort axisNameId)
        {
            AxisTag = axisTag;
            MinValue = minValue;
            DefaultValue = defaultValue;
            MaxValue = maxValue;
            Flags = flags;
            AxisNameId = axisNameId;
        }
    }

    public readonly struct InstanceRecord
    {
        public ushort SubfamilyNameId { get; }
        public ushort Flags { get; }
        public Fixed1616[] Coordinates { get; }
        public ushort PostScriptNameId { get; }

        public InstanceRecord(ushort subfamilyNameId, ushort flags, Fixed1616[] coordinates, ushort postScriptNameId)
        {
            SubfamilyNameId = subfamilyNameId;
            Flags = flags;
            Coordinates = coordinates ?? throw new ArgumentNullException(nameof(coordinates));
            PostScriptNameId = postScriptNameId;
        }
    }
}
