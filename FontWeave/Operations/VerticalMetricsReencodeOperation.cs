using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations;

/// <summary>Only TargetNumOfLongVerMetrics is publishable. Value/table edits are represented
/// to report their unsupported full-font semantics rather than silently preserving them.</summary>
public sealed record VerticalMetricsEdit
{
    public int? TargetNumOfLongVerMetrics { get; init; }
    public VheaValues? Vhea { get; init; }
    public VmtxEditPlan? Vmtx { get; init; }
    public VorgEditPlan? Vorg { get; init; }
}

public enum VerticalMetricsRelation
{
    SameSourceCounts, PayloadLength, AdvanceHeightMaximum, OutlineProfile, StaticProfile,
    GlyphMetricsPreserved, VorgStructure, VorgPayloadPreserved, VorgOriginsPreserved,
    VorgApplicability, OutlineExtrema, OriginOutline, VariableMetrics, LineSpacing
}
public enum VerticalMetricsVerdict { Verified, Rejected, NotEvaluated }
public enum VerticalOriginApplicability { NotDetermined, Absent, CffVorg, IgnoredTrueType }
public readonly record struct VerticalMetricsCheck(VerticalMetricsRelation Relation,
    VerticalMetricsVerdict Verdict, DataLocation Provenance = default,
    FontDiagnostic Diagnostic = default);

public sealed class VerticalMetricsReport
{
    private readonly VerticalMetricsCheck[] _checks;
    internal VerticalMetricsReport(VerticalMetricsCheck[] checks)
    {
        _checks = checks;
        foreach (var check in checks)
            if (check.Relation == VerticalMetricsRelation.VorgApplicability)
                OriginApplicability = check.Verdict == VerticalMetricsVerdict.Verified
                    ? VerticalOriginApplicability.CffVorg
                    : check.Provenance.TableTag == 0x564F5247
                        ? VerticalOriginApplicability.IgnoredTrueType
                        : VerticalOriginApplicability.Absent;
    }
    public ReadOnlySpan<VerticalMetricsCheck> Checks => _checks;
    public VerticalOriginApplicability OriginApplicability { get; }
}

/// <summary>Atomically replaces vhea and vmtx with a different storage boundary while
/// preserving every expanded AH/TSB. No glyph remapping, outline or variable interpretation.</summary>
public static class VerticalMetricsReencodeOperation
{
    private const uint Head = 0x68656164, Vhea = 0x76686561, Vmtx = 0x766D7478,
        Maxp = 0x6D617870, Name = 0x6E616D65, Dsig = 0x44534947, Vorg = 0x564F5247;
    private const uint Glyf = 0x676C7966, Loca = 0x6C6F6361, Cff = 0x43464620, Cff2 = 0x43464632;

    public static ReadStatus TryPrepare(FontFaceRef face, int targetN, out CompiledFont? result,
        out VerticalMetricsReport? report, out FontDiagnostic diagnostic,
        MetadataEditOptions? options = null, BuildLimits? limits = null) =>
        TryPrepare(face, new VerticalMetricsEdit { TargetNumOfLongVerMetrics = targetN },
            out result, out report, out diagnostic, options, limits);

    public static ReadStatus TryPrepare(FontFaceRef face, VerticalMetricsEdit edit,
        out CompiledFont? result, out VerticalMetricsReport? report,
        out FontDiagnostic diagnostic, MetadataEditOptions? options = null, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        result = null; report = null;
        options ??= new();
        if (!Enum.IsDefined(options.UnknownTables)) throw new ArgumentOutOfRangeException(nameof(options));
        if (edit.Vhea.HasValue || edit.Vmtx is not null || edit.Vorg is not null ||
            !edit.TargetNumOfLongVerMetrics.HasValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                face.Location, out diagnostic);
        if (face.IsCollection)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                face.Location, out diagnostic);
        var budget = new BuildBudget(limits);
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(directory.SourceByteLength, face.Location, out diagnostic) ||
            !budget.Work(32L * directory.DeclaredCount, face.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = directory.Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;

        var checks = new List<VerticalMetricsCheck>
        {
            new(VerticalMetricsRelation.OutlineExtrema, VerticalMetricsVerdict.NotEvaluated),
            new(VerticalMetricsRelation.OriginOutline, VerticalMetricsVerdict.NotEvaluated),
            new(VerticalMetricsRelation.VariableMetrics, VerticalMetricsVerdict.NotEvaluated),
            new(VerticalMetricsRelation.LineSpacing, VerticalMetricsVerdict.NotEvaluated)
        };
        status = directory.TryFindUniqueTable(Head, out int headOrdinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(Vhea, out int vheaOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        status = directory.TryFindUniqueTable(Maxp, out int maxpOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        status = directory.TryFindUniqueTable(Vmtx, out int vmtxOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.PayloadLength, checks, diagnostic, out report);

        status = directory.TryGetTableBytes(headOrdinal, out var headBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryOpen(headBytes, out var headView, out diagnostic, directory.TableLocation(headOrdinal));
        if (status != ReadStatus.Done) return status;
        status = headView.TryRead(out HeadTable? _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;

        status = directory.TryGetTableBytes(vheaOrdinal, out var vheaBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryOpen(vheaBytes, out var vheaView, out diagnostic, directory.TableLocation(vheaOrdinal));
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.PayloadLength, checks, diagnostic, out report);
        status = vheaView.TryRead(out VheaTable? vhea, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.PayloadLength, checks, diagnostic, out report);
        status = directory.TryGetTableBytes(maxpOrdinal, out var maxpBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryOpen(maxpBytes, out var maxpView, out diagnostic, directory.TableLocation(maxpOrdinal));
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        status = maxpView.TryRead(out MaxpTable? maxp, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        var vmtxLocation = directory.TableLocation(vmtxOrdinal);
        status = VmtxContext.TryCreate(vhea!, maxp!, vmtxLocation, out var context, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        checks.Add(new(VerticalMetricsRelation.SameSourceCounts, VerticalMetricsVerdict.Verified, maxp!.Location));
        status = directory.TryGetTableBytes(vmtxOrdinal, out var vmtxBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VmtxView.TryOpen(vmtxBytes, out var vmtxView, out diagnostic, vmtxLocation);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.PayloadLength, checks, diagnostic, out report);
        status = vmtxView.TryRead(context!, out VmtxTable? vmtx, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.PayloadLength, checks, diagnostic, out report);
        checks.Add(new(VerticalMetricsRelation.PayloadLength, VerticalMetricsVerdict.Verified, vmtxLocation));
        checks[0] = checks[0] with { Provenance = vhea!.Location };
        checks[1] = checks[1] with { Provenance = vmtxLocation };
        checks[2] = checks[2] with { Provenance = vmtxLocation };
        checks[3] = checks[3] with { Provenance = vhea.Location };
        if (!budget.Work(vmtx!.NumGlyphs, vmtxLocation, out diagnostic)) return ReadStatus.ResourceLimit;
        status = VerticalMetricsValidation.ValidatePair(vhea!, vmtx, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.AdvanceHeightMaximum, checks, diagnostic, out report);
        checks.Add(new(VerticalMetricsRelation.AdvanceHeightMaximum, VerticalMetricsVerdict.Verified,
            vhea!.Location.At(DataField.Payload, 10)));

        bool glyf = false, loca = false, cff = false, cff2 = false, hasVorg = false;
        DataLocation variableLocation = default;
        for (int i = 0; i < directory.DeclaredCount; i++)
        {
            _ = directory.TryGetRecord(i, out var record, out _);
            glyf |= record.Tag == Glyf; loca |= record.Tag == Loca;
            cff |= record.Tag == Cff; cff2 |= record.Tag == Cff2;
            hasVorg |= record.Tag == Vorg;
            // STAT can occur in static fonts; this is a conservative profile exclusion.
            if (record.Tag is 0x66766172 or 0x67766172 or 0x48564152 or 0x56564152 or 0x4D564152 or 0x53544154)
                variableLocation = directory.TableLocation(i);
        }
        bool trueType = glyf && loca && !cff && !cff2;
        bool compact = cff && !glyf && !loca && !cff2;
        if (!(trueType && maxp is Maxp10Table || compact && maxp is Maxp05Table))
        {
            status = ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                maxp.Location.At(DataField.Version, 0), out diagnostic, maxp.VersionRaw);
            return Reject(status, VerticalMetricsRelation.OutlineProfile, checks, diagnostic, out report);
        }
        checks.Add(new(VerticalMetricsRelation.OutlineProfile, VerticalMetricsVerdict.Verified, maxp.Location));
        if (variableLocation != default)
        {
            status = ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                variableLocation, out diagnostic, variableLocation.TableTag);
            return Reject(status, VerticalMetricsRelation.StaticProfile, checks, diagnostic, out report);
        }
        checks.Add(new(VerticalMetricsRelation.StaticProfile, VerticalMetricsVerdict.Verified, face.Location));

        if (hasVorg)
        {
            status = directory.TryFindUniqueTable(Vorg, out int vorgOrdinal, out diagnostic);
            if (status != ReadStatus.Done)
                return Reject(status, VerticalMetricsRelation.VorgStructure, checks, diagnostic, out report);
            var vorgLocation = directory.TableLocation(vorgOrdinal);
            status = VorgContext.TryCreate(maxp, vorgLocation, out var vorgContext, out diagnostic);
            if (status != ReadStatus.Done)
                return Reject(status, VerticalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
            status = directory.TryGetTableBytes(vorgOrdinal, out var vorgBytes, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = VorgView.TryOpen(vorgBytes, out var vorgView, out diagnostic, vorgLocation);
            if (status != ReadStatus.Done)
                return Reject(status, VerticalMetricsRelation.VorgStructure, checks, diagnostic, out report);
            status = vorgView.TryRead(vorgContext!, out VorgTable? _, out diagnostic, budget);
            if (status != ReadStatus.Done)
                return Reject(status, VerticalMetricsRelation.VorgStructure, checks, diagnostic, out report);
            checks.Add(new(VerticalMetricsRelation.VorgStructure, VerticalMetricsVerdict.Verified, vorgLocation));
            // The unchanged payload proves stored origin preservation, not outline agreement.
            // TrueType consumers must ignore VORG even when its structure is readable.
            checks.Add(new(VerticalMetricsRelation.VorgApplicability,
                compact ? VerticalMetricsVerdict.Verified : VerticalMetricsVerdict.NotEvaluated, vorgLocation));
        }
        else
        {
            checks.Add(new(VerticalMetricsRelation.VorgApplicability,
                VerticalMetricsVerdict.NotEvaluated, face.Location));
            checks.Add(new(VerticalMetricsRelation.VorgOriginsPreserved,
                VerticalMetricsVerdict.NotEvaluated, face.Location));
        }

        status = VmtxTableCompiler.TryReencode(vmtx, edit.TargetNumOfLongVerMetrics.Value,
            out VmtxTable? updatedVmtx, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.GlyphMetricsPreserved, checks, diagnostic, out report);
        status = vhea.TryWith(vhea.Values with { NumOfLongVerMetrics = updatedVmtx!.NumOfLongVerMetrics },
            out VheaTable? updatedVhea, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VerticalMetricsValidation.ValidatePair(updatedVhea!, updatedVmtx, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, VerticalMetricsRelation.AdvanceHeightMaximum, checks, diagnostic, out report);
        checks.Add(new(VerticalMetricsRelation.GlyphMetricsPreserved, VerticalMetricsVerdict.Verified, vmtxLocation));

        if (!budget.Buffer(8L * directory.DeclaredCount, face.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var decisions = new TableDecision[directory.DeclaredCount];
        for (int i = 0; i < decisions.Length; i++)
        {
            _ = directory.TryGetRecord(i, out var record, out _);
            TableAction action;
            if (record.Tag is Vhea or Vmtx) action = TableAction.Rebuild;
            else if (record.Tag == Dsig)
            {
                if (!options.DropDigitalSignature)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.SignatureRequiresDrop,
                        directory.TableLocation(i), out diagnostic, record.Tag);
                action = TableAction.Drop;
            }
            else if (record.Tag is Head or Name or Vorg || MetadataEditOperation.IsKnownMetadataIndependentTable(record.Tag))
                action = TableAction.Preserve;
            else
            {
                if (options.UnknownTables == UnknownTablePolicy.Reject)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnknownTable,
                        directory.TableLocation(i), out diagnostic, record.Tag);
                action = options.UnknownTables == UnknownTablePolicy.Drop ? TableAction.Drop : TableAction.Preserve;
            }
            decisions[i] = new(record.Tag, action);
        }

        status = VheaTableCompiler.TryCompile(updatedVhea!, out var compiledVhea, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = VmtxTableCompiler.TryCompile(updatedVmtx, out var compiledVmtx, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var tables = new SortedDictionary<uint, byte[]>();
        for (int i = 0; i < decisions.Length; i++)
        {
            var decision = decisions[i];
            if (decision.Action == TableAction.Drop) continue;
            if (decision.Tag == Vhea) tables.Add(Vhea, compiledVhea!);
            else if (decision.Tag == Vmtx) tables.Add(Vmtx, compiledVmtx!);
            else
            {
                status = directory.TryGetTableBytes(i, out var bytes, out diagnostic);
                if (status != ReadStatus.Done) return status;
                var location = directory.TableLocation(i);
                if (!budget.Buffer(bytes.Length, location, out diagnostic) ||
                    !budget.Work(bytes.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
                tables.Add(decision.Tag, bytes.ToArray());
                budget.RecordTableCopy(bytes.Length);
                if (decision.Tag == Vorg)
                {
                    checks.Add(new(VerticalMetricsRelation.VorgPayloadPreserved,
                        VerticalMetricsVerdict.Verified, location));
                    checks.Add(new(VerticalMetricsRelation.VorgOriginsPreserved,
                        compact ? VerticalMetricsVerdict.Verified : VerticalMetricsVerdict.NotEvaluated, location));
                }
            }
        }
        status = SfntEmitter.TryEmit(directory.Version, face.Location, tables, out byte[]? output,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        result = new(output!, budget.Usage, decisions);
        report = new(checks.ToArray());
        return ReadStatus.Done;
    }

    private static ReadStatus Reject(ReadStatus status, VerticalMetricsRelation relation,
        List<VerticalMetricsCheck> checks, FontDiagnostic diagnostic, out VerticalMetricsReport? report)
    {
        checks.Add(new(relation, VerticalMetricsVerdict.Rejected, diagnostic.Location, diagnostic));
        report = new(checks.ToArray());
        return status;
    }
}
