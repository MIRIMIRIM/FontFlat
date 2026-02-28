using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>avar</c> table (axis variations).
/// </summary>
[OtTableBuilder("avar")]
public sealed partial class AvarTableBuilder : ISfntTableSource
{
    private readonly List<List<AvarTable.AxisValueMap>> _axes = new();

    private Fixed1616 _version = new(0x00010000u);

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

    public int AxisCount => _axes.Count;

    public void ClearAxes()
    {
        if (_axes.Count == 0)
            return;

        _axes.Clear();
        MarkDirty();
    }

    public void AddAxis()
    {
        _axes.Add(new List<AvarTable.AxisValueMap>());
        MarkDirty();
    }

    public void AddAxis(ReadOnlySpan<AvarTable.AxisValueMap> maps)
    {
        var list = new List<AvarTable.AxisValueMap>(maps.Length);
        for (int i = 0; i < maps.Length; i++)
            list.Add(maps[i]);

        _axes.Add(list);
        MarkDirty();
    }

    public bool RemoveAxis(int axisIndex)
    {
        if ((uint)axisIndex >= (uint)_axes.Count)
            return false;

        _axes.RemoveAt(axisIndex);
        MarkDirty();
        return true;
    }

    public bool TryGetAxisMaps(int axisIndex, out IReadOnlyList<AvarTable.AxisValueMap> maps)
    {
        if ((uint)axisIndex >= (uint)_axes.Count)
        {
            maps = null!;
            return false;
        }

        maps = _axes[axisIndex];
        return true;
    }

    public void ClearAxisMaps(int axisIndex)
    {
        if ((uint)axisIndex >= (uint)_axes.Count)
            throw new ArgumentOutOfRangeException(nameof(axisIndex));

        _axes[axisIndex].Clear();
        MarkDirty();
    }

    public void AddMap(int axisIndex, F2Dot14 fromCoordinate, F2Dot14 toCoordinate)
    {
        if ((uint)axisIndex >= (uint)_axes.Count)
            throw new ArgumentOutOfRangeException(nameof(axisIndex));

        _axes[axisIndex].Add(new AvarTable.AxisValueMap(fromCoordinate, toCoordinate));
        MarkDirty();
    }

    public void AddMap(int axisIndex, AvarTable.AxisValueMap map)
    {
        if ((uint)axisIndex >= (uint)_axes.Count)
            throw new ArgumentOutOfRangeException(nameof(axisIndex));

        _axes[axisIndex].Add(map);
        MarkDirty();
    }

    public static bool TryFrom(AvarTable avar, out AvarTableBuilder builder)
    {
        builder = new AvarTableBuilder
        {
            Version = avar.Version
        };

        int axisCount = avar.AxisCount;
        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            if (!avar.TryGetSegmentMap(axisIndex, out var segmentMap))
                return false;

            int mapCount = segmentMap.PositionMapCount;
            var maps = new List<AvarTable.AxisValueMap>(mapCount);
            for (int i = 0; i < mapCount; i++)
            {
                if (!segmentMap.TryGetAxisValueMap(i, out var map))
                    return false;
                maps.Add(map);
            }

            builder._axes.Add(maps);
        }

        builder.MarkDirty();
        return true;
    }

    private byte[] BuildTable()
    {
        if (Version.RawValue != 0x00010000u)
            throw new InvalidOperationException("avar version must be 1.0 (0x00010000).");

        if (_axes.Count > ushort.MaxValue)
            throw new InvalidOperationException("avar axisCount must fit in uint16.");

        int axisCount = _axes.Count;
        int length = 8;

        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            var maps = _axes[axisIndex];
            if (maps.Count > ushort.MaxValue)
                throw new InvalidOperationException("avar positionMapCount must fit in uint16.");

            length = checked(length + 2 + (maps.Count * 4));
        }

        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt32(span, 0, Version.RawValue);
        BigEndian.WriteUInt16(span, 4, 0); // reserved
        BigEndian.WriteUInt16(span, 6, checked((ushort)axisCount));

        int pos = 8;
        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            var maps = _axes[axisIndex];
            maps.Sort(static (a, b) => a.FromCoordinate.RawValue.CompareTo(b.FromCoordinate.RawValue));

            BigEndian.WriteUInt16(span, pos, checked((ushort)maps.Count));
            pos += 2;

            for (int i = 0; i < maps.Count; i++)
            {
                var m = maps[i];
                BigEndian.WriteInt16(span, pos + 0, m.FromCoordinate.RawValue);
                BigEndian.WriteInt16(span, pos + 2, m.ToCoordinate.RawValue);
                pos += 4;
            }
        }

        return table;
    }
}
