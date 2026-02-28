namespace OTFontFile2.Tables;

/// <summary>
/// OpenType Layout SequenceLookupRecord used by contextual/chaining subtables in GSUB/GPOS.
/// </summary>
public readonly struct SequenceLookupRecord
{
    public ushort SequenceIndex { get; }
    public ushort LookupListIndex { get; }

    public SequenceLookupRecord(ushort sequenceIndex, ushort lookupListIndex)
    {
        SequenceIndex = sequenceIndex;
        LookupListIndex = lookupListIndex;
    }

    public static bool TryRead(TableSlice table, int offset, out SequenceLookupRecord record)
    {
        record = default;

        if ((uint)offset > (uint)table.Length - 4)
            return false;

        var data = table.Span;
        record = new SequenceLookupRecord(
            sequenceIndex: BigEndian.ReadUInt16(data, offset),
            lookupListIndex: BigEndian.ReadUInt16(data, offset + 2));
        return true;
    }
}

