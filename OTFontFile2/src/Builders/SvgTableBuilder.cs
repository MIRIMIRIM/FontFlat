using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>SVG </c> table.
/// </summary>
[OtTableBuilder("SVG ")]
public sealed partial class SvgTableBuilder : ISfntTableSource
{
    private const ushort DefaultVersion = 0;

    private readonly List<DocumentRecord> _records = new();

    private ushort _version = DefaultVersion;
    private uint _reserved;

    /// <summary>
    /// If true, performs a lightweight payload sanity check for documents that appear to be XML.
    /// </summary>
    public bool ValidateSvgPayload { get; set; }

    public ushort Version
    {
        get => _version;
        set
        {
            if (value == _version)
                return;

            _version = value;
            MarkDirty();
        }
    }

    public uint Reserved
    {
        get => _reserved;
        set
        {
            if (value == _reserved)
                return;

            _reserved = value;
            MarkDirty();
        }
    }

    public int DocumentCount => _records.Count;

    public IReadOnlyList<DocumentRecord> Documents => _records;

    public void Clear()
    {
        _records.Clear();
        MarkDirty();
    }

    public void AddDocument(ushort startGlyphId, ushort endGlyphId, ReadOnlyMemory<byte> documentBytes)
    {
        if (endGlyphId < startGlyphId)
            throw new ArgumentOutOfRangeException(nameof(endGlyphId));

        _records.Add(new DocumentRecord(startGlyphId, endGlyphId, documentBytes));
        MarkDirty();
    }

    public bool RemoveAt(int index)
    {
        if ((uint)index >= (uint)_records.Count)
            return false;

        _records.RemoveAt(index);
        MarkDirty();
        return true;
    }

    public static bool TryFrom(SvgTable svg, out SvgTableBuilder builder)
    {
        builder = null!;

        if (!svg.TryGetDocumentIndex(out var index))
            return false;

        var b = new SvgTableBuilder
        {
            Version = svg.Version,
            Reserved = svg.Reserved
        };

        int count = index.RecordCount;
        for (int i = 0; i < count; i++)
        {
            if (!index.TryGetRecord(i, out var record))
                return false;

            if (!svg.TryGetDocumentSpan(record, out var bytes))
                return false;

            b._records.Add(new DocumentRecord(record.StartGlyphId, record.EndGlyphId, bytes.ToArray()));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        var records = _records.ToArray();
        Array.Sort(records, static (a, b) =>
        {
            int c = a.StartGlyphId.CompareTo(b.StartGlyphId);
            if (c != 0) return c;
            return a.EndGlyphId.CompareTo(b.EndGlyphId);
        });

        for (int i = 0; i < records.Length; i++)
        {
            if (records[i].EndGlyphId < records[i].StartGlyphId)
                throw new InvalidOperationException("SVG document glyph range is invalid.");

            if (i != 0 && records[i].StartGlyphId <= records[i - 1].EndGlyphId)
                throw new InvalidOperationException("SVG document glyph ranges must be non-overlapping and ordered by StartGlyphId.");

            if (ValidateSvgPayload && !records[i].DocumentBytes.IsEmpty && LooksLikeXml(records[i].DocumentBytes.Span) && !ContainsSvgTag(records[i].DocumentBytes.Span))
                throw new InvalidOperationException("SVG document bytes look like XML but do not contain an <svg> tag.");
        }

        int recordCount = records.Length;
        if (recordCount > ushort.MaxValue)
            throw new InvalidOperationException("SVG document record count must fit in uint16.");

        const int headerSize = 10;
        const int docIndexOffset = headerSize;

        int indexSize = checked(2 + (recordCount * 12));
        int tableSize = checked(docIndexOffset + indexSize);

        var docOffsets = new uint[recordCount];
        var docLengths = new uint[recordCount];

        int dataPos = 0;
        for (int i = 0; i < recordCount; i++)
        {
            var doc = records[i].DocumentBytes;
            int length = doc.Length;
            if (length < 0)
                throw new InvalidOperationException("Negative SVG document length.");

            docOffsets[i] = checked((uint)(indexSize + dataPos));
            docLengths[i] = checked((uint)length);
            dataPos = checked(dataPos + length);
        }

        tableSize = checked(tableSize + dataPos);

        byte[] table = new byte[tableSize];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Version);
        BigEndian.WriteUInt32(span, 2, checked((uint)docIndexOffset));
        BigEndian.WriteUInt32(span, 6, Reserved);

        BigEndian.WriteUInt16(span, docIndexOffset, (ushort)recordCount);

        int recordPos = docIndexOffset + 2;
        for (int i = 0; i < recordCount; i++)
        {
            var r = records[i];

            BigEndian.WriteUInt16(span, recordPos + 0, r.StartGlyphId);
            BigEndian.WriteUInt16(span, recordPos + 2, r.EndGlyphId);
            BigEndian.WriteUInt32(span, recordPos + 4, docOffsets[i]);
            BigEndian.WriteUInt32(span, recordPos + 8, docLengths[i]);
            recordPos += 12;
        }

        int docPos = docIndexOffset + indexSize;
        for (int i = 0; i < recordCount; i++)
        {
            var doc = records[i].DocumentBytes;
            int length = doc.Length;
            if (length == 0)
                continue;

            doc.Span.CopyTo(span.Slice(docPos, length));
            docPos = checked(docPos + length);
        }

        return table;
    }

    private static bool LooksLikeXml(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while ((uint)i < (uint)data.Length && (data[i] == 0x20 || data[i] == 0x09 || data[i] == 0x0D || data[i] == 0x0A))
            i++;

        if (i >= data.Length)
            return false;

        return data[i] == (byte)'<';
    }

    private static bool ContainsSvgTag(ReadOnlySpan<byte> data)
    {
        // Case-sensitive best-effort scan for "<svg" within the first 4KB.
        int limit = data.Length > 4096 ? 4096 : data.Length;
        for (int i = 0; i + 4 <= limit; i++)
        {
            if (data[i] == (byte)'<' && data[i + 1] == (byte)'s' && data[i + 2] == (byte)'v' && data[i + 3] == (byte)'g')
                return true;
        }

        return false;
    }

    public readonly struct DocumentRecord
    {
        public ushort StartGlyphId { get; }
        public ushort EndGlyphId { get; }
        public ReadOnlyMemory<byte> DocumentBytes { get; }

        public DocumentRecord(ushort startGlyphId, ushort endGlyphId, ReadOnlyMemory<byte> documentBytes)
        {
            StartGlyphId = startGlyphId;
            EndGlyphId = endGlyphId;
            DocumentBytes = documentBytes;
        }
    }
}
