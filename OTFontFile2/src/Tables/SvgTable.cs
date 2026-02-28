using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("SVG ", 10)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("DocumentIndexOffset", OtFieldKind.UInt32, 2)]
[OtField("Reserved", OtFieldKind.UInt32, 6)]
[OtSubTableOffset("DocumentIndex", nameof(DocumentIndexOffset), typeof(SvgDocumentIndex), OutParameterName = "index")]
public readonly partial struct SvgTable
{
    public bool TryGetDocumentRecord(int index, out SvgDocumentIndex.SvgDocumentRecord record)
    {
        record = default;
        return TryGetDocumentIndex(out var docIndex) && docIndex.TryGetRecord(index, out record);
    }

    public bool TryGetDocumentSpan(SvgDocumentIndex.SvgDocumentRecord record, out ReadOnlySpan<byte> documentBytes)
    {
        documentBytes = default;

        int baseOffset = (int)DocumentIndexOffset;
        if ((uint)baseOffset > (uint)_table.Length)
            return false;

        if (record.DocumentOffset > int.MaxValue || record.DocumentLength > int.MaxValue)
            return false;

        int offset = checked(baseOffset + (int)record.DocumentOffset);
        int length = (int)record.DocumentLength;
        if (length < 0)
            return false;
        if ((uint)offset > (uint)_table.Length)
            return false;
        if (length > _table.Length - offset)
            return false;

        documentBytes = _table.Span.Slice(offset, length);
        return true;
    }

    public bool TryGetDocumentSpan(int index, out ReadOnlySpan<byte> documentBytes)
    {
        documentBytes = default;
        return TryGetDocumentRecord(index, out var record) && TryGetDocumentSpan(record, out documentBytes);
    }

    [OtSubTable(2)]
    [OtField("RecordCount", OtFieldKind.UInt16, 0)]
    [OtSequentialRecordArray("Record", 2, 12, CountPropertyName = "RecordCount", RecordTypeName = "SvgDocumentRecord")]
    public readonly partial struct SvgDocumentIndex
    {
        public readonly struct SvgDocumentRecord
        {
            public ushort StartGlyphId { get; }
            public ushort EndGlyphId { get; }
            public uint DocumentOffset { get; }
            public uint DocumentLength { get; }

            public SvgDocumentRecord(ushort startGlyphId, ushort endGlyphId, uint documentOffset, uint documentLength)
            {
                StartGlyphId = startGlyphId;
                EndGlyphId = endGlyphId;
                DocumentOffset = documentOffset;
                DocumentLength = documentLength;
            }
        }


    }
}
