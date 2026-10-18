using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations;

public sealed record MetadataTableEdit
{
    public Os2MetadataEdit? Os2 { get; init; }
    public PostEditPlan? Post { get; init; }
}

public enum MetadataTableRelation { StaticOs2, PostOutline, PostGlyphCount, Style, CharacterRanges }
public enum MetadataTableVerdict { Verified, Rejected, NotEvaluated }
public readonly record struct MetadataTableCheck(MetadataTableRelation Relation,
    MetadataTableVerdict Verdict, FontDiagnostic Diagnostic = default);

public sealed class MetadataTableEditReport
{
    private readonly MetadataTableCheck[] _checks;
    internal MetadataTableEditReport(MetadataTableCheck[] checks) => _checks = checks;
    public ReadOnlySpan<MetadataTableCheck> Checks => _checks;
}

public static partial class MetadataEditOperation
{
    private const uint Os2 = 0x4F532F32, Post = 0x706F7374, Maxp = 0x6D617870;
    private const uint Glyf = 0x676C7966, Loca = 0x6C6F6361, Cff = 0x43464620, Cff2 = 0x43464632;

    /// <summary>Finite OS/2 weight/width and post edits. Cross-table evidence is read from
    /// the same immutable source face as the edited tables, never caller-supplied counts.
    /// Unchanged style/range relations and outline contents are not revalidated.</summary>
    public static ReadStatus TryPrepare(FontFaceRef face, MetadataTableEdit edit,
        out CompiledFont? result, out MetadataTableEditReport? report,
        out FontDiagnostic diagnostic, MetadataEditOptions? options = null, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        result = null; report = null;
        options ??= new();
        if (!Enum.IsDefined(options.UnknownTables)) throw new ArgumentOutOfRangeException(nameof(options));
        if (edit.Os2 is null && edit.Post is null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                face.Location, out diagnostic);
        var budget = new BuildBudget(limits);
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = face.RequireStandaloneFace(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(directory.SourceByteLength, face.Location, out diagnostic) ||
            !budget.Work(32L * directory.DeclaredCount, face.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = directory.Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(Head, out int headOrdinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(headOrdinal, out var headBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryOpen(headBytes, out var headView, out diagnostic, directory.TableLocation(headOrdinal));
        if (status != ReadStatus.Done) return status;
        status = headView.TryRead(out HeadTable? _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;

        if (!budget.Buffer(8L * directory.DeclaredCount, face.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var decisions = new TableDecision[directory.DeclaredCount];
        bool glyf = false, loca = false, cff = false, cff2 = false;
        DataLocation variableLocation = default;
        int os2Ordinal = -1, postOrdinal = -1;
        for (int i = 0; i < decisions.Length; i++)
        {
            _ = directory.TryGetRecord(i, out var record, out _);
            if (record.Tag == Os2) os2Ordinal = i;
            if (record.Tag == Post) postOrdinal = i;
            glyf |= record.Tag == Glyf; loca |= record.Tag == Loca;
            cff |= record.Tag == Cff; cff2 |= record.Tag == Cff2;
            if (record.Tag is 0x66766172 or 0x53544154 or 0x4D564152)
                variableLocation = directory.TableLocation(i);
            TableAction action;
            if (record.Tag == Os2 && edit.Os2.HasValue || record.Tag == Post && edit.Post is not null)
                action = TableAction.Rebuild;
            else if (record.Tag == Dsig)
            {
                if (!options.DropDigitalSignature)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.SignatureRequiresDrop,
                        directory.TableLocation(i), out diagnostic, record.Tag);
                action = TableAction.Drop;
            }
            else if (record.Tag is Head or Name || IsKnownMetadataIndependentTable(record.Tag)) action = TableAction.Preserve;
            else
            {
                if (options.UnknownTables == UnknownTablePolicy.Reject)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnknownTable,
                        directory.TableLocation(i), out diagnostic, record.Tag);
                action = options.UnknownTables == UnknownTablePolicy.Drop ? TableAction.Drop : TableAction.Preserve;
            }
            decisions[i] = new(record.Tag, action);
        }
        var checks = new List<MetadataTableCheck>
        {
            new(MetadataTableRelation.Style, MetadataTableVerdict.NotEvaluated),
            new(MetadataTableRelation.CharacterRanges, MetadataTableVerdict.NotEvaluated)
        };
        if (edit.Os2.HasValue)
        {
            if (variableLocation != default)
            {
                status = ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    variableLocation, out diagnostic, variableLocation.TableTag);
                return RejectRelation(status, MetadataTableRelation.StaticOs2, checks, diagnostic, out report);
            }
            if (os2Ordinal < 0)
                return ReadWire.Fail(ReadStatus.MissingTable, DiagnosticCode.TableNotFound,
                    face.Location with { TableTag = Os2 }, out diagnostic, Os2);
            checks.Add(new(MetadataTableRelation.StaticOs2, MetadataTableVerdict.Verified));
        }
        else checks.Add(new(MetadataTableRelation.StaticOs2, MetadataTableVerdict.NotEvaluated));

        PostTable? postModel = null;
        if (edit.Post is not null)
        {
            if (postOrdinal < 0)
                return ReadWire.Fail(ReadStatus.MissingTable, DiagnosticCode.TableNotFound,
                    face.Location with { TableTag = Post }, out diagnostic, Post);
            status = directory.TryGetTableBytes(postOrdinal, out var bytes, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = PostView.TryOpen(bytes, out var view, out diagnostic, directory.TableLocation(postOrdinal));
            if (status != ReadStatus.Done) return status;
            status = view.TryRead(out postModel, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            bool trueType = glyf && loca && !cff && !cff2;
            bool compact = !glyf && !loca && cff != cff2;
            if (!(trueType || compact) ||
                cff && postModel!.Version != PostLayout.Version30 ||
                postModel!.Version == PostLayout.Version25 && !trueType)
            {
                status = ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    postModel!.Location.At(DataField.Version, 0), out diagnostic, postModel.Version);
                return RejectRelation(status, MetadataTableRelation.PostOutline, checks, diagnostic, out report);
            }
            checks.Add(new(MetadataTableRelation.PostOutline, MetadataTableVerdict.Verified));
            status = directory.TryFindUniqueTable(Maxp, out int maxpOrdinal, out diagnostic);
            if (status != ReadStatus.Done)
                return RejectRelation(status, MetadataTableRelation.PostGlyphCount, checks, diagnostic, out report);
            status = directory.TryGetTableBytes(maxpOrdinal, out var maxpBytes, out diagnostic);
            if (status != ReadStatus.Done)
                return RejectRelation(status, MetadataTableRelation.PostGlyphCount, checks, diagnostic, out report);
            status = MaxpView.TryOpen(maxpBytes, out var maxpView, out diagnostic, directory.TableLocation(maxpOrdinal));
            if (status != ReadStatus.Done)
                return RejectRelation(status, MetadataTableRelation.PostGlyphCount, checks, diagnostic, out report);
            status = maxpView.TryRead(out MaxpTable? maxp, out diagnostic, budget);
            if (status != ReadStatus.Done)
                return RejectRelation(status, MetadataTableRelation.PostGlyphCount, checks, diagnostic, out report);
            if ((trueType && maxp is not Maxp10Table) ||
                (compact && maxp is not Maxp05Table) ||
                (postModel.NumGlyphs.HasValue && postModel.NumGlyphs.Value != maxp!.NumGlyphs))
            {
                status = ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    maxp!.Location.At(DataField.NumGlyphs, 4), out diagnostic,
                    maxp.NumGlyphs, postModel.NumGlyphs.GetValueOrDefault());
                return RejectRelation(status, MetadataTableRelation.PostGlyphCount, checks, diagnostic, out report);
            }
            checks.Add(new(MetadataTableRelation.PostGlyphCount, MetadataTableVerdict.Verified));
        }
        else
        {
            checks.Add(new(MetadataTableRelation.PostOutline, MetadataTableVerdict.NotEvaluated));
            checks.Add(new(MetadataTableRelation.PostGlyphCount, MetadataTableVerdict.NotEvaluated));
        }

        var tables = new SortedDictionary<uint, byte[]>();
        for (int i = 0; i < decisions.Length; i++)
        {
            var decision = decisions[i];
            if (decision.Action == TableAction.Drop) continue;
            status = directory.TryGetTableBytes(i, out var bytes, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var location = directory.TableLocation(i);
            byte[]? compiled;
            if (decision.Tag == Os2 && edit.Os2.HasValue)
                status = Os2TableCompiler.TryCompile(bytes, edit.Os2.Value, out compiled, out diagnostic, budget, location);
            else if (decision.Tag == Post && edit.Post is not null)
            {
                status = PostTableCompiler.TryApply(postModel!, edit.Post, out var updated, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = PostTableCompiler.TryCompile(updated!, out compiled, out diagnostic, budget);
            }
            else
            {
                if (!budget.Buffer(bytes.Length, location, out diagnostic) ||
                    !budget.Work(bytes.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
                compiled = bytes.ToArray();
                budget.RecordTableCopy(bytes.Length);
                status = ReadStatus.Done;
            }
            if (status != ReadStatus.Done) return status;
            tables.Add(decision.Tag, compiled!);
        }
        status = SfntEmitter.TryEmit(directory.Version, face.Location, tables,
            out byte[]? output, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        result = new(output!, budget.Usage, decisions);
        report = new(checks.ToArray());
        return ReadStatus.Done;
    }

    private static ReadStatus RejectRelation(ReadStatus status, MetadataTableRelation relation,
        List<MetadataTableCheck> checks, FontDiagnostic diagnostic, out MetadataTableEditReport? report)
    {
        checks.Add(new(relation, MetadataTableVerdict.Rejected, diagnostic));
        report = new(checks.ToArray());
        return status;
    }
}
