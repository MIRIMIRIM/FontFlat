using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CvtLayout
{
    internal const uint Tag = 0x63767420;
    internal const int ValueStride = 2;

    /// <summary>cvt  has no header at all; the schema requires a positive minimum, and one
    /// value is the smallest table that carries any content. A zero-length table is legal on
    /// the wire and is admitted by the semantic partial rather than by this floor.</summary>
    internal const int MinimumHeader = ValueStride;

    /// <summary>Declared element budget, so a table that claims an implausible value count
    /// is refused rather than allocated.</summary>
    internal const int MaxValues = 1 << 16;
}

// cvt  has no version and no header: it is exactly one array of signed FWORD values whose
// count is derived from the table length. The array is declared here so the generated
// reader binds it; the partial supplies the count relation and the even-length rule.
[BinaryTable(CvtLayout.Tag, CvtLayout.MinimumHeader)]
[BinaryRecord("Value", CvtLayout.ValueStride)]
[BinaryElement("Value", "Amount", WireType.Int16, 0, DataField.Payload,
    Semantic = NumberSemantic.Plain)]
public readonly ref partial struct CvtView { }

/// <summary>One Control Value Table entry: an FWORD in design units.
///
/// Nothing here evaluates, interpolates or applies a control value; the number is carried
/// as data and its control-table semantics are not modelled by this slice.</summary>
public readonly record struct CvtValue(int Index, short Amount);

/// <summary>Owned Control Value Table contents.</summary>
public sealed class CvtTable
{
    private readonly short[] _values;

    internal CvtTable(short[] values, DataLocation location)
    {
        _values = values;
        Location = location;
    }

    public DataLocation Location { get; }

    public int Count => _values.Length;

    /// <summary>Byte length of the table as encoded: two bytes per value.</summary>
    public int EncodedLength => _values.Length * CvtLayout.ValueStride;

    public ReadOnlySpan<short> Values => _values;

    public short this[int index] => _values[index];

    public ReadStatus TryGetValue(int index, out short amount, out FontDiagnostic diagnostic)
    {
        amount = 0;
        if (index < 0 || index >= _values.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, (ulong)(index * CvtLayout.ValueStride), index),
                out diagnostic, unchecked((ulong)(long)index), (ulong)_values.Length);
        amount = _values[index];
        diagnostic = default;
        return ReadStatus.Done;
    }

    public IEnumerable<CvtValue> Enumerate()
    {
        for (int i = 0; i < _values.Length; i++) yield return new(i, _values[i]);
    }
}
