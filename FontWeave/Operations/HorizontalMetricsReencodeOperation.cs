using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations;

/// <summary>Only TargetNumberOfHMetrics is publishable. Value/table edits are represented
/// to report their unsupported full-font semantics rather than silently preserving them.</summary>
public sealed record HorizontalMetricsEdit
{
    public int? TargetNumberOfHMetrics { get; init; }
    public HheaValues? Hhea { get; init; }
    public HmtxEditPlan? Hmtx { get; init; }
}

public enum HorizontalMetricsRelation
{
    SameSourceCounts, PayloadLength, AdvanceWidthMaximum, OutlineProfile, StaticProfile,
    GlyphMetricsPreserved, OutlineExtrema, SideBearingOutline, VariableMetrics, LineSpacing
}
public enum HorizontalMetricsVerdict { Verified, Rejected, NotEvaluated }
public readonly record struct HorizontalMetricsCheck(HorizontalMetricsRelation Relation,
    HorizontalMetricsVerdict Verdict, DataLocation Provenance = default,
    FontDiagnostic Diagnostic = default);

public sealed class HorizontalMetricsReport
{
    private readonly HorizontalMetricsCheck[] _checks;
    internal HorizontalMetricsReport(HorizontalMetricsCheck[] checks) => _checks = checks;
    public ReadOnlySpan<HorizontalMetricsCheck> Checks => _checks;
}

/// <summary>Atomically replaces hhea and hmtx with a different storage boundary while
/// preserving every expanded AW/LSB. No glyph remapping, outline or variable interpretation.</summary>
public static class HorizontalMetricsReencodeOperation
{
    private const uint Head = 0x68656164, Hhea = 0x68686561, Hmtx = 0x686D7478,
        Maxp = 0x6D617870, Name = 0x6E616D65, Dsig = 0x44534947;
    private const uint Glyf = 0x676C7966, Loca = 0x6C6F6361, Cff = 0x43464620, Cff2 = 0x43464632;

    public static ReadStatus TryPrepare(FontFaceRef face, int targetN, out CompiledFont? result,
        out HorizontalMetricsReport? report, out FontDiagnostic diagnostic,
        MetadataEditOptions? options = null, BuildLimits? limits = null) =>
        TryPrepare(face, new HorizontalMetricsEdit { TargetNumberOfHMetrics = targetN },
            out result, out report, out diagnostic, options, limits);

    public static ReadStatus TryPrepare(FontFaceRef face, HorizontalMetricsEdit edit,
        out CompiledFont? result, out HorizontalMetricsReport? report,
        out FontDiagnostic diagnostic, MetadataEditOptions? options = null, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        result = null; report = null;
        options ??= new();
        if (!Enum.IsDefined(options.UnknownTables)) throw new ArgumentOutOfRangeException(nameof(options));
        if (edit.Hhea.HasValue || edit.Hmtx is not null || !edit.TargetNumberOfHMetrics.HasValue)
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

        var checks = new List<HorizontalMetricsCheck>
        {
            new(HorizontalMetricsRelation.OutlineExtrema, HorizontalMetricsVerdict.NotEvaluated),
            new(HorizontalMetricsRelation.SideBearingOutline, HorizontalMetricsVerdict.NotEvaluated),
            new(HorizontalMetricsRelation.VariableMetrics, HorizontalMetricsVerdict.NotEvaluated),
            new(HorizontalMetricsRelation.LineSpacing, HorizontalMetricsVerdict.NotEvaluated)
        };
        status = directory.TryFindUniqueTable(Head, out int headOrdinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(Hhea, out int hheaOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        status = directory.TryFindUniqueTable(Maxp, out int maxpOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        status = directory.TryFindUniqueTable(Hmtx, out int hmtxOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.PayloadLength, checks, diagnostic, out report);

        status = directory.TryGetTableBytes(headOrdinal, out var headBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryOpen(headBytes, out var headView, out diagnostic, directory.TableLocation(headOrdinal));
        if (status != ReadStatus.Done) return status;
        status = headView.TryRead(out HeadTable? _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;

        status = directory.TryGetTableBytes(hheaOrdinal, out var hheaBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryOpen(hheaBytes, out var hheaView, out diagnostic, directory.TableLocation(hheaOrdinal));
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.PayloadLength, checks, diagnostic, out report);
        status = hheaView.TryRead(out HheaTable? hhea, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.PayloadLength, checks, diagnostic, out report);
        status = directory.TryGetTableBytes(maxpOrdinal, out var maxpBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryOpen(maxpBytes, out var maxpView, out diagnostic, directory.TableLocation(maxpOrdinal));
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        status = maxpView.TryRead(out MaxpTable? maxp, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        var hmtxLocation = directory.TableLocation(hmtxOrdinal);
        status = HmtxContext.TryCreate(hhea!, maxp!, hmtxLocation, out var context, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.SameSourceCounts, checks, diagnostic, out report);
        checks.Add(new(HorizontalMetricsRelation.SameSourceCounts, HorizontalMetricsVerdict.Verified, maxp!.Location));
        status = directory.TryGetTableBytes(hmtxOrdinal, out var hmtxBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HmtxView.TryOpen(hmtxBytes, out var hmtxView, out diagnostic, hmtxLocation);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.PayloadLength, checks, diagnostic, out report);
        status = hmtxView.TryRead(context!, out HmtxTable? hmtx, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.PayloadLength, checks, diagnostic, out report);
        checks.Add(new(HorizontalMetricsRelation.PayloadLength, HorizontalMetricsVerdict.Verified, hmtxLocation));
        checks[0] = checks[0] with { Provenance = hhea!.Location };
        checks[1] = checks[1] with { Provenance = hmtxLocation };
        checks[2] = checks[2] with { Provenance = hmtxLocation };
        checks[3] = checks[3] with { Provenance = hhea.Location };
        if (!budget.Work(hmtx!.NumGlyphs, hmtxLocation, out diagnostic)) return ReadStatus.ResourceLimit;
        status = HorizontalMetricsValidation.ValidatePair(hhea!, hmtx, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.AdvanceWidthMaximum, checks, diagnostic, out report);
        checks.Add(new(HorizontalMetricsRelation.AdvanceWidthMaximum, HorizontalMetricsVerdict.Verified,
            hhea!.Location.At(DataField.Payload, 10)));

        bool glyf = false, loca = false, cff = false, cff2 = false;
        DataLocation variableLocation = default;
        for (int i = 0; i < directory.DeclaredCount; i++)
        {
            _ = directory.TryGetRecord(i, out var record, out _);
            glyf |= record.Tag == Glyf; loca |= record.Tag == Loca;
            cff |= record.Tag == Cff; cff2 |= record.Tag == Cff2;
            // STAT can occur in static fonts; this is a conservative profile exclusion.
            if (record.Tag is 0x66766172 or 0x67766172 or 0x48564152 or 0x4D564152 or 0x53544154)
                variableLocation = directory.TableLocation(i);
        }
        bool trueType = glyf && loca && !cff && !cff2;
        bool compact = cff && !glyf && !loca && !cff2;
        if (!(trueType && maxp is Maxp10Table || compact && maxp is Maxp05Table))
        {
            status = ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                maxp.Location.At(DataField.Version, 0), out diagnostic, maxp.VersionRaw);
            return Reject(status, HorizontalMetricsRelation.OutlineProfile, checks, diagnostic, out report);
        }
        checks.Add(new(HorizontalMetricsRelation.OutlineProfile, HorizontalMetricsVerdict.Verified, maxp.Location));
        if (variableLocation != default)
        {
            status = ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                variableLocation, out diagnostic, variableLocation.TableTag);
            return Reject(status, HorizontalMetricsRelation.StaticProfile, checks, diagnostic, out report);
        }
        checks.Add(new(HorizontalMetricsRelation.StaticProfile, HorizontalMetricsVerdict.Verified, face.Location));

        status = HmtxTableCompiler.TryReencode(hmtx, edit.TargetNumberOfHMetrics.Value,
            out HmtxTable? updatedHmtx, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.GlyphMetricsPreserved, checks, diagnostic, out report);
        status = hhea.TryWith(hhea.Values with { NumberOfHMetrics = updatedHmtx!.NumberOfHMetrics },
            out HheaTable? updatedHhea, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HorizontalMetricsValidation.ValidatePair(updatedHhea!, updatedHmtx, out diagnostic);
        if (status != ReadStatus.Done)
            return Reject(status, HorizontalMetricsRelation.AdvanceWidthMaximum, checks, diagnostic, out report);
        checks.Add(new(HorizontalMetricsRelation.GlyphMetricsPreserved, HorizontalMetricsVerdict.Verified, hmtxLocation));

        if (!budget.Buffer(8L * directory.DeclaredCount, face.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var decisions = new TableDecision[directory.DeclaredCount];
        for (int i = 0; i < decisions.Length; i++)
        {
            _ = directory.TryGetRecord(i, out var record, out _);
            TableAction action;
            if (record.Tag is Hhea or Hmtx) action = TableAction.Rebuild;
            else if (record.Tag == Dsig)
            {
                if (!options.DropDigitalSignature)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.SignatureRequiresDrop,
                        directory.TableLocation(i), out diagnostic, record.Tag);
                action = TableAction.Drop;
            }
            else if (record.Tag is Head or Name || MetadataEditOperation.IsKnownMetadataIndependentTable(record.Tag))
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

        status = HheaTableCompiler.TryCompile(updatedHhea!, out var compiledHhea, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = HmtxTableCompiler.TryCompile(updatedHmtx, out var compiledHmtx, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var tables = new SortedDictionary<uint, byte[]>();
        for (int i = 0; i < decisions.Length; i++)
        {
            var decision = decisions[i];
            if (decision.Action == TableAction.Drop) continue;
            if (decision.Tag == Hhea) tables.Add(Hhea, compiledHhea!);
            else if (decision.Tag == Hmtx) tables.Add(Hmtx, compiledHmtx!);
            else
            {
                status = directory.TryGetTableBytes(i, out var bytes, out diagnostic);
                if (status != ReadStatus.Done) return status;
                var location = directory.TableLocation(i);
                if (!budget.Buffer(bytes.Length, location, out diagnostic) ||
                    !budget.Work(bytes.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
                tables.Add(decision.Tag, bytes.ToArray());
                budget.RecordTableCopy(bytes.Length);
            }
        }
        status = SfntEmitter.TryEmit(directory.Version, face.Location, tables, out byte[]? output,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        result = new(output!, budget.Usage, decisions);
        report = new(checks.ToArray());
        return ReadStatus.Done;
    }

    private static ReadStatus Reject(ReadStatus status, HorizontalMetricsRelation relation,
        List<HorizontalMetricsCheck> checks, FontDiagnostic diagnostic, out HorizontalMetricsReport? report)
    {
        checks.Add(new(relation, HorizontalMetricsVerdict.Rejected, diagnostic.Location, diagnostic));
        report = new(checks.ToArray());
        return status;
    }
}
