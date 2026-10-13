using System.Runtime.CompilerServices;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Strict EBSC 2.0 records and explicit EBLC source-strike validation.
/// All wire fields are read through the generated schema accessors.</summary>
public readonly ref partial struct EbscView
{
    /// <summary>Validates 2.0, the exact array extent, and unique target ppem pairs.
    /// Metrics, pad bytes, record order and zero ppem values remain observations.</summary>
    public ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(out diagnostic, budget);
    }

    private ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMinorVersion(out ushort minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = EbscTableCompiler.RequireVersion(major, minor, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumSizes(out uint numSizes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryRequireBitmapScaleExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        ulong required = EbscLayout.BitmapScaleAt + (ulong)EbscLayout.BitmapScaleStride * numSizes;
        if (required != (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.AtLocal(DataField.Length, required), out diagnostic,
                (ulong)_data.Length - required, 0UL);
        int count = (int)numSizes;
        int mapLength = EbscTableCompiler.MapLength(count);
        if (!budget.Work((long)_data.Length + 29L * count + mapLength, Location, out diagnostic) ||
            !budget.Buffer(mapLength, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] targets = mapLength == 0 ? [] : new byte[mapLength];
        for (int i = 0; i < count; i++)
        {
            status = TryGetBitmapScale(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int pair = EbscTableCompiler.PpemPair(row.PpemX, row.PpemY);
            if (EbscTableCompiler.HasPair(targets, pair))
            {
                for (int previous = 0; previous < i; previous++)
                {
                    status = TryGetBitmapScale(previous, out var earlier, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (pair == EbscTableCompiler.PpemPair(earlier.PpemX, earlier.PpemY))
                        return EbscTableCompiler.DuplicateTarget(Location, i, pair, previous,
                            out diagnostic);
                }
            }
            EbscTableCompiler.AddPair(targets, pair);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Validates EBLC under the same caller budget, then requires each
    /// substitute ppem pair to exist. A missing pair returns NotFound/InvalidValue
    /// at this EBSC record's source bytes; duplicate EBLC ppem pairs are allowed.</summary>
    public ReadStatus ValidateTable(EblcView eblc, int ebdtDataLength, int? numGlyphs,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        ValidatePair(eblc, ebdtDataLength, numGlyphs, out diagnostic, new BuildBudget(limits));

    private ReadStatus ValidatePair(EblcView eblc, int ebdtDataLength, int? numGlyphs,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!IsBound || !eblc.IsBound)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                !IsBound ? Location : eblc.Location, out diagnostic);
        if (Location.SourceId != eblc.Location.SourceId || Location.FaceIndex != eblc.Location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                eblc.Location, out diagnostic);
        if (!budget.Input(_data.Length, Location, out diagnostic) ||
            !budget.Input(eblc.RawBytes.Length, eblc.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = ValidateTable(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = eblc.ValidateTable(ebdtDataLength, numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumSizes(out uint numSizes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (numSizes == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = eblc.TryGetNumSizes(out uint sourceCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(EbscTableCompiler.PpemMapLength + (long)sourceCount + numSizes,
                Location, out diagnostic) ||
            !budget.Buffer(EbscTableCompiler.PpemMapLength, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var sources = new byte[EbscTableCompiler.PpemMapLength];
        for (int i = 0; i < sourceCount; i++)
        {
            status = eblc.TryGetBitmapSize(i, out var source, out diagnostic);
            if (status != ReadStatus.Done) return status;
            EbscTableCompiler.AddPair(sources, EbscTableCompiler.PpemPair(source.PpemX, source.PpemY));
        }
        for (int i = 0; i < numSizes; i++)
        {
            status = TryGetBitmapScale(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int pair = EbscTableCompiler.PpemPair(row.SubstitutePpemX, row.SubstitutePpemY);
            if (!EbscTableCompiler.HasPair(sources, pair))
                return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload,
                        EbscLayout.RecordPosition(i) + EbscLayout.SubstitutePpemXAt, i),
                    out diagnostic, (uint)pair, 0UL);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryReadTable(out EbscTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = ValidateTable(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        return ReadValidated(out model, out diagnostic, budget);
    }

    public ReadStatus TryReadTable(EblcView eblc, int ebdtDataLength, int? numGlyphs,
        out EbscTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = ValidatePair(eblc, ebdtDataLength, numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        return ReadValidated(out model, out diagnostic, budget);
    }

    private ReadStatus ReadValidated(out EbscTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMinorVersion(out ushort minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumSizes(out uint numSizes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(28L * numSizes, Location, out diagnostic) ||
            !budget.Buffer(numSizes * (long)Unsafe.SizeOf<EbscScaleRecord>(),
                Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = new EbscScaleRecord[(int)numSizes];
        for (int i = 0; i < records.Length; i++)
        {
            status = TryGetBitmapScale(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            records[i] = EbscScaleRecord.FromWire(row,
                Location.AtLocal(DataField.Payload, EbscLayout.RecordPosition(i), i));
        }
        model = new(major, minor, records, Location, _data.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
