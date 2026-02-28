using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the TrueType <c>cvt </c> table.
/// </summary>
[OtTableBuilder("cvt ")]
public sealed partial class CvtTableBuilder : ISfntTableSource
{
    private readonly List<short> _values = new();

    public int ValueCount => _values.Count;

    public IReadOnlyList<short> Values => _values;

    public void Clear()
    {
        _values.Clear();
        MarkDirty();
    }

    public void AddValue(short value)
    {
        _values.Add(value);
        MarkDirty();
    }

    public void SetValue(int index, short value)
    {
        if ((uint)index >= (uint)_values.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _values[index] = value;
        MarkDirty();
    }

    public static bool TryFrom(CvtTable cvt, out CvtTableBuilder builder)
    {
        builder = null!;

        var b = new CvtTableBuilder();

        int count = cvt.ValueCount;
        for (int i = 0; i < count; i++)
        {
            if (!cvt.TryGetValue(i, out short value))
                return false;

            b._values.Add(value);
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        int count = _values.Count;
        int length = checked(count * 2);

        byte[] table = new byte[length];
        var span = table.AsSpan();

        int offset = 0;
        for (int i = 0; i < count; i++)
        {
            BigEndian.WriteInt16(span, offset, _values[i]);
            offset += 2;
        }

        return table;
    }
}
