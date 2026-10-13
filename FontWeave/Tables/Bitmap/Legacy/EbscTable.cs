using System.Runtime.CompilerServices;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>The twelve SbitLineMetrics bytes. Pad bytes are preserved data.</summary>
public readonly record struct EbscLineMetrics(sbyte Ascender, sbyte Descender,
    byte WidthMax, sbyte CaretSlopeNumerator, sbyte CaretSlopeDenominator,
    sbyte CaretOffset, sbyte MinOriginSB, sbyte MinAdvanceSB, sbyte MaxBeforeBL,
    sbyte MinAfterBL, sbyte Pad1, sbyte Pad2);

/// <summary>A target strike description and the existing strike to scale.</summary>
public readonly record struct EbscScaleRecord(EbscLineMetrics HoriMetrics,
    EbscLineMetrics VertMetrics, byte PpemX, byte PpemY, byte SubstitutePpemX,
    byte SubstitutePpemY)
{
    /// <summary>The record's original table coordinate; no input bytes are retained.</summary>
    public DataLocation Location { get; internal init; }

    internal static EbscScaleRecord FromWire(in EbscView.BitmapScaleData row,
        DataLocation location) => new(new(row.HoriAscender, row.HoriDescender,
            row.HoriWidthMax, row.HoriCaretSlopeNumerator, row.HoriCaretSlopeDenominator,
            row.HoriCaretOffset, row.HoriMinOriginSB, row.HoriMinAdvanceSB,
            row.HoriMaxBeforeBL, row.HoriMinAfterBL, row.HoriPad1, row.HoriPad2),
        new(row.VertAscender, row.VertDescender, row.VertWidthMax,
            row.VertCaretSlopeNumerator, row.VertCaretSlopeDenominator,
            row.VertCaretOffset, row.VertMinOriginSB, row.VertMinAdvanceSB,
            row.VertMaxBeforeBL, row.VertMinAfterBL, row.VertPad1, row.VertPad2),
        row.PpemX, row.PpemY, row.SubstitutePpemX, row.SubstitutePpemY)
        { Location = location };

    internal EbscView.BitmapScaleData ToWire(int ordinal) => new(ordinal,
        HoriMetrics.Ascender, HoriMetrics.Descender, HoriMetrics.WidthMax,
        HoriMetrics.CaretSlopeNumerator, HoriMetrics.CaretSlopeDenominator,
        HoriMetrics.CaretOffset, HoriMetrics.MinOriginSB, HoriMetrics.MinAdvanceSB,
        HoriMetrics.MaxBeforeBL, HoriMetrics.MinAfterBL, HoriMetrics.Pad1, HoriMetrics.Pad2,
        VertMetrics.Ascender, VertMetrics.Descender, VertMetrics.WidthMax,
        VertMetrics.CaretSlopeNumerator, VertMetrics.CaretSlopeDenominator,
        VertMetrics.CaretOffset, VertMetrics.MinOriginSB, VertMetrics.MinAdvanceSB,
        VertMetrics.MaxBeforeBL, VertMetrics.MinAfterBL, VertMetrics.Pad1, VertMetrics.Pad2,
        PpemX, PpemY, SubstitutePpemX, SubstitutePpemY);
}

/// <summary>Owned EBSC 2.0 records, with immutable metrics and numeric provenance.</summary>
public sealed class EbscTable
{
    private readonly EbscScaleRecord[] _records;

    internal EbscTable(ushort majorVersion, ushort minorVersion, EbscScaleRecord[] records,
        DataLocation location, int consumedLength)
    {
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        _records = records;
        Records = Array.AsReadOnly(records);
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ushort MajorVersion { get; }
    public ushort MinorVersion { get; }
    public int SizeCount => _records.Length;
    public IReadOnlyList<EbscScaleRecord> Records { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ReadStatus TryGetRecord(int recordIndex, out EbscScaleRecord record,
        out FontDiagnostic diagnostic)
    {
        record = default;
        ReadStatus status = RequireRecordIndex(recordIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        record = _records[recordIndex];
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryWithRecord(int recordIndex, EbscScaleRecord record,
        out EbscTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        ReadStatus status = RequireRecordIndex(recordIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var budget = new BuildBudget(limits);
        if (!budget.Work(29L * _records.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int pair = EbscTableCompiler.PpemPair(record.PpemX, record.PpemY);
        for (int i = 0; i < _records.Length; i++)
        {
            if (i == recordIndex) continue;
            var other = _records[i];
            if (pair != EbscTableCompiler.PpemPair(other.PpemX, other.PpemY)) continue;
            int duplicate = Math.Max(i, recordIndex), previous = Math.Min(i, recordIndex);
            return EbscTableCompiler.DuplicateTarget(Location, duplicate, pair, previous,
                out diagnostic);
        }
        if (!budget.Buffer(_records.Length * (long)Unsafe.SizeOf<EbscScaleRecord>(),
                Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = new EbscScaleRecord[_records.Length];
        Array.Copy(_records, records, _records.Length);
        records[recordIndex] = record with { Location = _records[recordIndex].Location };
        updated = new(MajorVersion, MinorVersion, records, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryWithRecordMetrics(int recordIndex, EbscLineMetrics hori,
        EbscLineMetrics vert, out EbscTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        ReadStatus status = TryGetRecord(recordIndex, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return TryWithRecord(recordIndex, record with { HoriMetrics = hori, VertMetrics = vert },
            out updated, out diagnostic, limits);
    }

    public ReadStatus TryWithRecordPpem(int recordIndex, byte ppemX, byte ppemY,
        byte substitutePpemX, byte substitutePpemY, out EbscTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        ReadStatus status = TryGetRecord(recordIndex, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return TryWithRecord(recordIndex, record with { PpemX = ppemX, PpemY = ppemY,
            SubstitutePpemX = substitutePpemX, SubstitutePpemY = substitutePpemY },
            out updated, out diagnostic, limits);
    }

    private ReadStatus RequireRecordIndex(int recordIndex, out FontDiagnostic diagnostic)
    {
        if (recordIndex < 0 || (uint)recordIndex >= (uint)_records.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 4, recordIndex), out diagnostic,
                unchecked((ulong)(long)recordIndex), (ulong)_records.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
