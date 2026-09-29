using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations;

/// <summary>An explicit edit of the supported head fields. Dependent or derived fields are
/// represented so an attempted edit can be rejected rather than silently ignored.</summary>
public sealed record HeadFieldsEdit
{
    public uint? VersionRaw { get; init; }
    public int? FontRevisionRaw { get; init; }
    public uint? ChecksumAdjustment { get; init; }
    public uint? MagicNumber { get; init; }
    public ushort? Flags { get; init; }
    public ushort? UnitsPerEm { get; init; }
    public long? CreatedSeconds { get; init; }
    public long? ModifiedSeconds { get; init; }
    public short? XMin { get; init; }
    public short? YMin { get; init; }
    public short? XMax { get; init; }
    public short? YMax { get; init; }
    public ushort? MacStyle { get; init; }
    public ushort? LowestRecPpem { get; init; }
    public short? FontDirectionHint { get; init; }
    public short? IndexToLocFormat { get; init; }
    public short? GlyphDataFormat { get; init; }
}

public sealed record MaxpFieldsEdit
{
    public uint? VersionRaw { get; init; }
    public ushort? NumGlyphs { get; init; }
    public Maxp10Values? DerivedMaxima { get; init; }
}

public sealed record FoundationTableEdit
{
    public HeadFieldsEdit? Head { get; init; }
    public MaxpFieldsEdit? Maxp { get; init; }
}

public enum FoundationOutlineKind : byte { Unknown, TrueType, Cff1, Cff2, Conflict }
public enum FoundationRelation : byte
{
    OutlineProfile, GlyphCount, LocaFormat, VariableFlags, Style, BoundingBox, DerivedMaxima
}
public enum FoundationVerdict : byte { Verified, Rejected, NotEvaluated }

/// <summary>Caller-supplied assertion tied to the original source face and head revision.
/// The named producer owns the table interpretation; this operation only checks the binding
/// and the asserted relation, not the outline/program contents.</summary>
public readonly record struct FoundationEvidence<T>(T Value, DataLocation Provenance, int FontRevisionRaw);

public sealed record FoundationCrossTableContext
{
    public FoundationEvidence<FoundationOutlineKind>? OutlineKind { get; init; }
    public FoundationEvidence<ushort>? GlyphCount { get; init; }
    public FoundationEvidence<short>? LocaFormat { get; init; }
    public FoundationEvidence<bool>? IsVariable { get; init; }
    public FoundationEvidence<ushort>? FsSelection { get; init; }
}

public readonly record struct FoundationCheck(FoundationRelation Relation, FoundationVerdict Verdict,
    FontDiagnostic Diagnostic = default);

public sealed class FoundationEditReport
{
    private readonly FoundationCheck[] _checks;
    internal FoundationEditReport(FoundationCheck[] checks) => _checks = checks;
    public ReadOnlySpan<FoundationCheck> Checks => _checks;
}

public sealed record FoundationEditOptions
{
    public UnknownTablePolicy UnknownTables { get; init; } = UnknownTablePolicy.Reject;
    public bool DropDigitalSignature { get; init; }
}

/// <summary>Finite head/maxp table-edit profile; no glyph rewriting or derived-field repair.</summary>
public static class FoundationTableEditOperation
{
    private const uint Head = 0x68656164, Maxp = 0x6D617870, Name = 0x6E616D65;
    private const uint Glyf = 0x676C7966, Loca = 0x6C6F6361;
    private const uint Cff = 0x43464620, Cff2 = 0x43464632, Fvar = 0x66766172;
    private const uint Dsig = 0x44534947;

    public static ReadStatus TryPrepare(FontFaceRef face, FoundationTableEdit edit,
        FoundationCrossTableContext? context, out CompiledFont? result,
        out FoundationEditReport? report, out FontDiagnostic diagnostic,
        FoundationEditOptions? options = null, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        result = null;
        report = null;
        options ??= new();
        context ??= new();
        if (!Enum.IsDefined(options.UnknownTables))
            throw new ArgumentOutOfRangeException(nameof(options));

        var budget = new BuildBudget(limits);
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(directory.SourceByteLength, face.Location, out diagnostic) ||
            !budget.Work(32L * directory.DeclaredCount, face.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        status = directory.Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(Head, out int headOrdinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(Maxp, out int maxpOrdinal, out diagnostic);
        if (status != ReadStatus.Done) return status;

        status = directory.TryGetTableBytes(headOrdinal, out var headBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        DataLocation headAt = directory.TableLocation(headOrdinal);
        status = HeadView.TryOpen(headBytes, out var headView, out diagnostic, headAt);
        if (status != ReadStatus.Done) return status;
        status = headView.TryRead(out HeadTable? head, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;

        status = directory.TryGetTableBytes(maxpOrdinal, out var maxpBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        DataLocation maxpAt = directory.TableLocation(maxpOrdinal);
        status = MaxpView.TryOpen(maxpBytes, out var maxpView, out diagnostic, maxpAt);
        if (status != ReadStatus.Done) return status;
        status = maxpView.TryRead(out MaxpTable? maxp, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;

        status = ApplyHeadEdit(head!, edit.Head, out HeadTable? newHead, out bool headChanged,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ApplyMaxpEdit(maxp!, edit.Maxp, out MaxpTable? newMaxp, out bool maxpChanged,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!headChanged && !maxpChanged)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                headAt, out diagnostic);

        status = EvaluateRelations(directory, head!, newHead!, maxp!, newMaxp!, edit,
            context, face, out report, out diagnostic);
        if (status != ReadStatus.Done) return status;

        if (!budget.Buffer(8L * directory.DeclaredCount, face.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var decisions = new TableDecision[directory.DeclaredCount];
        for (int i = 0; i < decisions.Length; i++)
        {
            status = directory.TryGetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            TableAction action;
            if (record.Tag == Head) action = headChanged ? TableAction.Rebuild : TableAction.Preserve;
            else if (record.Tag == Maxp) action = maxpChanged ? TableAction.Rebuild : TableAction.Preserve;
            else if (record.Tag == Dsig)
            {
                status = directory.TryGetTableBytes(i, out var signatureBytes, out diagnostic);
                if (status != ReadStatus.Done) return status;
                // An empty DSIG has no signature to invalidate. A real signature may only be dropped.
                bool emptyDsig = signatureBytes.Length == 8 && ReadWire.U32(signatureBytes, 0) == 1 &&
                    ReadWire.U16(signatureBytes, 4) == 0 && ReadWire.U16(signatureBytes, 6) == 0;
                if (!emptyDsig && !options.DropDigitalSignature)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                        DiagnosticCode.SignatureRequiresDrop, directory.TableLocation(i),
                        out diagnostic, record.Tag);
                action = options.DropDigitalSignature ? TableAction.Drop : TableAction.Preserve;
            }
            else if (record.Tag == Name || MetadataEditOperation.IsKnownMetadataIndependentTable(record.Tag))
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

        byte[]? compiledHead = null, compiledMaxp = null;
        if (headChanged)
        {
            status = HeadTableCompiler.TryCompile(newHead!, out compiledHead, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (maxpChanged)
        {
            status = MaxpTableCompiler.TryCompile(newMaxp!, out compiledMaxp, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }

        var tables = new SortedDictionary<uint, byte[]>();
        for (int i = 0; i < decisions.Length; i++)
        {
            TableDecision decision = decisions[i];
            if (decision.Action == TableAction.Drop) continue;
            if (decision.Tag == Head && headChanged) tables.Add(Head, compiledHead!);
            else if (decision.Tag == Maxp && maxpChanged) tables.Add(Maxp, compiledMaxp!);
            else
            {
                status = directory.TryGetTableBytes(i, out var bytes, out diagnostic);
                if (status != ReadStatus.Done) return status;
                DataLocation at = directory.TableLocation(i);
                if (!budget.Buffer(bytes.Length, at, out diagnostic) ||
                    !budget.Work(bytes.Length, at, out diagnostic)) return ReadStatus.ResourceLimit;
                tables.Add(decision.Tag, bytes.ToArray());
                budget.RecordTableCopy(bytes.Length);
            }
        }
        status = SfntEmitter.TryEmit(directory.Version, face.Location, tables, out byte[]? output,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        result = new CompiledFont(output!, budget.Usage, decisions);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus ApplyHeadEdit(HeadTable head, HeadFieldsEdit? edit,
        out HeadTable? updated, out bool changed, out FontDiagnostic diagnostic)
    {
        updated = head;
        changed = false;
        diagnostic = default;
        if (edit is null) return ReadStatus.Done;
        DataLocation at = head.Location;
        if (edit.VersionRaw.HasValue || edit.MagicNumber.HasValue || edit.GlyphDataFormat.HasValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                at.At(DataField.Version, 0), out diagnostic);
        if (edit.ChecksumAdjustment.HasValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                at.At(DataField.ChecksumAdjustment, 8), out diagnostic);
        if (edit.IndexToLocFormat.HasValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                at.At(DataField.IndexToLocFormat, 50), out diagnostic);
        if (edit.XMin.HasValue || edit.YMin.HasValue || edit.XMax.HasValue || edit.YMax.HasValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                at.At(DataField.XMin, 36), out diagnostic);

        HeadTableValues values = head.Values;
        if (edit.FontRevisionRaw is int revision) values = values with { FontRevisionRaw = revision };
        if (edit.Flags is ushort flags) values = values with { Flags = flags };
        if (edit.UnitsPerEm is ushort units) values = values with { UnitsPerEm = units };
        if (edit.CreatedSeconds is long created) values = values with { CreatedSeconds = created };
        if (edit.ModifiedSeconds is long modified) values = values with { ModifiedSeconds = modified };
        if (edit.MacStyle is ushort style) values = values with { MacStyle = style };
        if (edit.LowestRecPpem is ushort ppem) values = values with { LowestRecPpem = ppem };
        if (edit.FontDirectionHint is short hint) values = values with { FontDirectionHint = hint };
        changed = values != head.Values;
        if (!changed) return ReadStatus.Done;
        return head.TryWith(values, out updated, out diagnostic);
    }

    private static ReadStatus ApplyMaxpEdit(MaxpTable maxp, MaxpFieldsEdit? edit,
        out MaxpTable? updated, out bool changed, out FontDiagnostic diagnostic)
    {
        updated = maxp;
        changed = false;
        diagnostic = default;
        if (edit is null) return ReadStatus.Done;
        if (edit.VersionRaw.HasValue || edit.DerivedMaxima.HasValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                maxp.Location.At(edit.VersionRaw.HasValue ? DataField.Version : DataField.MaxPoints,
                    edit.VersionRaw.HasValue ? 0UL : 6UL), out diagnostic);
        if (edit.NumGlyphs is not ushort count || count == maxp.NumGlyphs)
            return ReadStatus.Done;
        changed = true;
        if (maxp is Maxp05Table cff)
        {
            ReadStatus status = cff.TryWith(count, out Maxp05Table? newCff, out diagnostic);
            updated = newCff;
            return status;
        }
        if (maxp is Maxp10Table tt)
        {
            ReadStatus status = tt.TryWith(tt.Values with { NumGlyphs = count },
                out Maxp10Table? newTt, out diagnostic);
            updated = newTt;
            return status;
        }
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
            maxp.Location.At(DataField.Version, 0), out diagnostic, maxp.VersionRaw);
    }

    private static ReadStatus EvaluateRelations(SfntDirectoryView directory,
        HeadTable originalHead, HeadTable newHead, MaxpTable originalMaxp, MaxpTable newMaxp,
        FoundationTableEdit edit, FoundationCrossTableContext context, FontFaceRef face,
        out FoundationEditReport report, out FontDiagnostic diagnostic)
    {
        bool glyf = false, loca = false, cff = false, cff2 = false, fvar = false;
        int locaOrdinal = -1;
        for (int i = 0; i < directory.DeclaredCount; i++)
        {
            ReadStatus s = directory.TryGetRecord(i, out var record, out diagnostic);
            if (s != ReadStatus.Done) { report = new([]); return s; }
            switch (record.Tag)
            {
                case Glyf: glyf = true; break;
                case Loca: loca = true; locaOrdinal = i; break;
                case Cff: cff = true; break;
                case Cff2: cff2 = true; break;
                case Fvar: fvar = true; break;
            }
        }
        int outlineKinds = (glyf && loca ? 1 : 0) + (cff ? 1 : 0) + (cff2 ? 1 : 0);
        FoundationOutlineKind kind = glyf != loca || outlineKinds > 1
            ? FoundationOutlineKind.Conflict
            : glyf ? FoundationOutlineKind.TrueType
            : cff ? FoundationOutlineKind.Cff1
            : cff2 ? FoundationOutlineKind.Cff2
            : FoundationOutlineKind.Unknown;
        bool profileMatches = kind switch
        {
            FoundationOutlineKind.TrueType => newMaxp.VersionRaw == 0x00010000,
            FoundationOutlineKind.Cff1 or FoundationOutlineKind.Cff2 => newMaxp.VersionRaw == 0x00005000,
            _ => false
        };
        bool outlineClaimMatches = !context.OutlineKind.HasValue ||
            (context.OutlineKind.Value.Value == kind &&
             OutlineEvidenceMatches(context.OutlineKind.Value, directory, face, originalHead, kind));
        FoundationVerdict outlineVerdict = !outlineClaimMatches || kind == FoundationOutlineKind.Conflict
            ? FoundationVerdict.Rejected
            : kind == FoundationOutlineKind.Unknown ? FoundationVerdict.NotEvaluated
            : profileMatches ? FoundationVerdict.Verified : FoundationVerdict.Rejected;

        FoundationVerdict countVerdict = FoundationVerdict.NotEvaluated;
        if (context.GlyphCount is { } countEvidence)
            countVerdict = countEvidence.Value == newMaxp.NumGlyphs &&
                OutlineEvidenceMatches(countEvidence, directory, face, originalHead, kind)
                ? FoundationVerdict.Verified : FoundationVerdict.Rejected;

        FoundationVerdict locaVerdict = FoundationVerdict.NotEvaluated;
        if (kind == FoundationOutlineKind.TrueType)
        {
            ReadStatus s = directory.TryGetRecord(locaOrdinal, out var locaRecord, out diagnostic);
            if (s != ReadStatus.Done) { report = new([]); return s; }
            uint expectedLength = (uint)(newMaxp.NumGlyphs + 1) *
                (newHead.Values.IndexToLocFormat == 0 ? 2u : 4u);
            bool lengthMatches = locaRecord.Length == expectedLength;
            bool claimMatches = !context.LocaFormat.HasValue ||
                (context.LocaFormat.Value.Value == newHead.Values.IndexToLocFormat &&
                 TableEvidenceMatches(context.LocaFormat.Value, directory, face, originalHead, Loca));
            // This verifies the width implied by length; it does not parse every loca entry.
            locaVerdict = lengthMatches && claimMatches ? FoundationVerdict.Verified : FoundationVerdict.Rejected;
        }

        bool variableFlagsMatch = !fvar ||
            ((newHead.Values.Flags & 0x20) == 0 &&
             (kind != FoundationOutlineKind.TrueType || (newHead.Values.Flags & 0x2) != 0));
        bool variableClaimMatches = !context.IsVariable.HasValue ||
            (context.IsVariable.Value.Value == fvar &&
             (fvar
                 ? TableEvidenceMatches(context.IsVariable.Value, directory, face, originalHead, Fvar)
                 : ForFace(context.IsVariable.Value, face, originalHead) &&
                   context.IsVariable.Value.Provenance == face.Location));
        FoundationVerdict variableVerdict = !variableFlagsMatch || !variableClaimMatches ||
            kind == FoundationOutlineKind.Conflict
            ? FoundationVerdict.Rejected
            : fvar || context.IsVariable.HasValue ? FoundationVerdict.Verified
            : FoundationVerdict.NotEvaluated;

        FoundationVerdict styleVerdict = FoundationVerdict.NotEvaluated;
        if (context.FsSelection is { } styleEvidence)
        {
            bool bold = (newHead.Values.MacStyle & 1) != 0;
            bool italic = (newHead.Values.MacStyle & 2) != 0;
            styleVerdict = TableEvidenceMatches(styleEvidence, directory, face, originalHead, 0x4F532F32) &&
                bold == ((styleEvidence.Value & 0x20) != 0) &&
                italic == ((styleEvidence.Value & 0x1) != 0)
                ? FoundationVerdict.Verified : FoundationVerdict.Rejected;
        }

        report = new FoundationEditReport([
            RelationCheck(FoundationRelation.OutlineProfile, outlineVerdict,
                originalMaxp.Location.At(DataField.Version, 0)),
            RelationCheck(FoundationRelation.GlyphCount, countVerdict,
                originalMaxp.Location.At(DataField.NumGlyphs, 4)),
            RelationCheck(FoundationRelation.LocaFormat, locaVerdict,
                originalHead.Location.At(DataField.IndexToLocFormat, 50)),
            RelationCheck(FoundationRelation.VariableFlags, variableVerdict,
                originalHead.Location.At(DataField.Flags, 16)),
            RelationCheck(FoundationRelation.Style, styleVerdict,
                originalHead.Location.At(DataField.MacStyle, 44)),
            new(FoundationRelation.BoundingBox, FoundationVerdict.NotEvaluated),
            new(FoundationRelation.DerivedMaxima, FoundationVerdict.NotEvaluated)
        ]);

        if (edit.Maxp?.NumGlyphs is ushort count && count != originalMaxp.NumGlyphs &&
            (outlineVerdict != FoundationVerdict.Verified ||
             countVerdict != FoundationVerdict.Verified ||
             (kind == FoundationOutlineKind.TrueType && locaVerdict != FoundationVerdict.Verified)))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                originalMaxp.Location.At(DataField.NumGlyphs, 4), out diagnostic,
                count, originalMaxp.NumGlyphs);

        if (edit.Head?.Flags is ushort newFlags && newFlags != originalHead.Values.Flags &&
            ((newFlags ^ originalHead.Values.Flags) & 0x22) != 0 &&
            (!context.IsVariable.HasValue || variableVerdict != FoundationVerdict.Verified ||
             outlineVerdict != FoundationVerdict.Verified))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                originalHead.Location.At(DataField.Flags, 16), out diagnostic, newFlags);

        if (edit.Head?.MacStyle is ushort newStyle &&
            ((newStyle ^ originalHead.Values.MacStyle) & 0x3) != 0 &&
            styleVerdict != FoundationVerdict.Verified)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                originalHead.Location.At(DataField.MacStyle, 44), out diagnostic, newStyle);

        if (fvar && variableVerdict != FoundationVerdict.Verified)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                originalHead.Location.At(DataField.Flags, 16), out diagnostic,
                newHead.Values.Flags);

        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool ForFace<T>(FoundationEvidence<T> evidence, FontFaceRef face,
        HeadTable originalHead) => evidence.Provenance.SourceId == face.SourceId &&
        evidence.Provenance.FaceIndex == face.FaceIndex &&
        evidence.FontRevisionRaw == originalHead.Values.FontRevisionRaw;

    private static FoundationCheck RelationCheck(FoundationRelation relation,
        FoundationVerdict verdict, DataLocation at) => new(relation, verdict,
        verdict == FoundationVerdict.Rejected
            ? new FontDiagnostic(DiagnosticCode.InvalidValue, at) : default);

    private static bool OutlineEvidenceMatches<T>(FoundationEvidence<T> evidence,
        SfntDirectoryView directory, FontFaceRef face, HeadTable head,
        FoundationOutlineKind kind) => kind switch
    {
        FoundationOutlineKind.TrueType =>
            TableEvidenceMatches(evidence, directory, face, head, Glyf) ||
            TableEvidenceMatches(evidence, directory, face, head, Loca),
        FoundationOutlineKind.Cff1 => TableEvidenceMatches(evidence, directory, face, head, Cff),
        FoundationOutlineKind.Cff2 => TableEvidenceMatches(evidence, directory, face, head, Cff2),
        _ => false
    };

    private static bool TableEvidenceMatches<T>(FoundationEvidence<T> evidence,
        SfntDirectoryView directory, FontFaceRef face, HeadTable head, uint tag)
    {
        DataLocation at = evidence.Provenance;
        if (!ForFace(evidence, face, head) || at.TableTag != tag ||
            at.Basis != OffsetBasis.Table || at.TableOrdinal < 0 ||
            at.TableOrdinal >= directory.DeclaredCount)
            return false;
        ReadStatus status = directory.TryGetRecord(at.TableOrdinal, out var record, out _);
        return status == ReadStatus.Done && record.Tag == tag &&
            at.TableFileOffset == record.FileOffset && at.Offset <= record.Length;
    }
}
