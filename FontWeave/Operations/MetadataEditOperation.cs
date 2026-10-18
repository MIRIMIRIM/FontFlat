using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations;

public enum UnknownTablePolicy { Reject, Preserve, Drop }
public enum TableAction { Preserve, Rebuild, Drop }
public readonly record struct TableDecision(uint Tag, TableAction Action);

/// <summary>Policies for this metadata-only operation, not a general transform profile.</summary>
public sealed record MetadataEditOptions
{
    public UnknownTablePolicy UnknownTables { get; init; } = UnknownTablePolicy.Reject;
    public bool DropDigitalSignature { get; init; }
}

/// <summary>
/// Edits explicit name ordinals. The legacy overload also edits head revision/modified time;
/// the name-only overload leaves those fields unchanged. Neither selects a preferred name,
/// changes glyph identities, nor validates preserved table internals. Private-table
/// preservation requires the caller's explicit consent.
/// </summary>
public static partial class MetadataEditOperation
{
    private const uint Head = 0x68656164, Name = 0x6E616D65, Dsig = 0x44534947;

    public static ReadStatus TryPrepare(FontFaceRef face, HeadMetadataEdit headEdit,
        ReadOnlySpan<NameReplacement> replacements, out CompiledFont? result,
        out FontDiagnostic diagnostic, MetadataEditOptions? options = null, BuildLimits? limits = null)
        => Prepare(face, headEdit, replacements, null, out result, out diagnostic, options, limits);

    /// <summary>Edit supported name records and language tags without changing head metadata.
    /// The container still recomputes head.checksumAdjustment for the emitted sfnt.</summary>
    public static ReadStatus TryPrepare(FontFaceRef face, NameEditPlan nameEdit,
        out CompiledFont? result, out FontDiagnostic diagnostic,
        MetadataEditOptions? options = null, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(nameEdit);
        ArgumentNullException.ThrowIfNull(nameEdit.Replacements);
        ArgumentNullException.ThrowIfNull(nameEdit.LanguageIdChanges);
        return Prepare(face, null, default, nameEdit, out result, out diagnostic, options, limits);
    }

    private static ReadStatus Prepare(FontFaceRef face, HeadMetadataEdit? headEdit,
        ReadOnlySpan<NameReplacement> legacyReplacements, NameEditPlan? nameEdit,
        out CompiledFont? result, out FontDiagnostic diagnostic,
        MetadataEditOptions? options, BuildLimits? limits)
    {
        result = null;
        options ??= new();
        if (!Enum.IsDefined(options.UnknownTables))
            throw new ArgumentOutOfRangeException(nameof(options));
        var budget = new BuildBudget(limits);
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = face.RequireStandaloneFace(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(directory.SourceByteLength, face.Location, out diagnostic) ||
            !budget.Work(32L * directory.DeclaredCount, face.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        status = directory.Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(Head, out int headOrdinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(Name, out int nameOrdinal, out diagnostic);
        if (status != ReadStatus.Done) return status;

        // Decide the whole operation before compiling or copying any table payload.
        if (!budget.Buffer(8L * directory.DeclaredCount, face.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var decisions = new TableDecision[directory.DeclaredCount];
        for (int i = 0; i < directory.DeclaredCount; i++)
        {
            status = directory.TryGetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            TableAction action;
            if (record.Tag == Name) action = TableAction.Rebuild;
            else if (record.Tag == Head) action = headEdit.HasValue ? TableAction.Rebuild : TableAction.Preserve;
            else if (record.Tag == Dsig)
            {
                if (!options.DropDigitalSignature)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.SignatureRequiresDrop,
                        directory.TableLocation(i), out diagnostic, record.Tag);
                action = TableAction.Drop;
            }
            else if (IsKnownMetadataIndependentTable(record.Tag)) action = TableAction.Preserve;
            else
            {
                if (options.UnknownTables == UnknownTablePolicy.Reject)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnknownTable,
                        directory.TableLocation(i), out diagnostic, record.Tag);
                action = options.UnknownTables == UnknownTablePolicy.Drop ? TableAction.Drop : TableAction.Preserve;
            }
            decisions[i] = new(record.Tag, action);
        }

        status = directory.TryGetTableBytes(nameOrdinal, out var nameBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        DataLocation nameLocation = directory.TableLocation(nameOrdinal);
        status = NameView.TryOpen(nameBytes, out var names, out diagnostic, nameLocation);
        if (status != ReadStatus.Done) return status;
        int replacementCount = headEdit.HasValue ? legacyReplacements.Length : nameEdit!.Replacements.Count;
        int languageChangeCount = headEdit.HasValue ? 0 : nameEdit!.LanguageIdChanges.Count;
        if (!budget.Work((long)replacementCount + languageChangeCount, nameLocation, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < replacementCount + languageChangeCount; i++)
        {
            int ordinal = i < replacementCount
                ? headEdit.HasValue ? legacyReplacements[i].Ordinal : nameEdit!.Replacements[i].Ordinal
                : nameEdit!.LanguageIdChanges[i - replacementCount].Ordinal;
            status = names.TryGetRecord(ordinal, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // PostScript names (6/20/25), variation labels and other dependent identities are
            // deliberately not edited by this finite operation. Table compiler remains generic.
            if (record.NameId is not (1 or 2 or 4 or 16 or 17))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    nameLocation.At(DataField.Payload, 6UL + (uint)ordinal * 12UL + 6,
                        ordinal), out diagnostic, record.NameId);
        }
        byte[]? compiledName;
        if (headEdit.HasValue)
            status = NameTableCompiler.TryCompile(nameBytes, legacyReplacements, out compiledName,
                out diagnostic, budget, nameLocation);
        else
            status = NameTableCompiler.TryCompile(nameBytes, nameEdit!, out compiledName,
                out diagnostic, budget, nameLocation);
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(headOrdinal, out var headBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        byte[]? compiledHead;
        if (headEdit.HasValue)
        {
            status = HeadTableCompiler.TryCompile(headBytes, headEdit.Value, out compiledHead,
                out diagnostic, budget, directory.TableLocation(headOrdinal));
            if (status != ReadStatus.Done) return status;
        }
        else
        {
            DataLocation headLocation = directory.TableLocation(headOrdinal);
            status = HeadView.TryOpen(headBytes, out var headView, out diagnostic, headLocation);
            if (status != ReadStatus.Done) return status;
            status = headView.TryRead(out HeadTable? _, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (!budget.Buffer(headBytes.Length, headLocation, out diagnostic) ||
                !budget.Work(headBytes.Length, headLocation, out diagnostic))
                return ReadStatus.ResourceLimit;
            compiledHead = headBytes.ToArray();
            budget.RecordTableCopy(compiledHead.Length);
        }

        var tables = new SortedDictionary<uint, byte[]>();
        for (int i = 0; i < decisions.Length; i++)
        {
            var decision = decisions[i];
            if (decision.Action == TableAction.Drop) continue;
            if (decision.Tag == Head) tables.Add(Head, compiledHead!);
            else if (decision.Tag == Name) tables.Add(Name, compiledName!);
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
        return ReadStatus.Done;
    }

    // These top-level tables can be relocated opaquely for this metadata-only edit: name
    // language IDs may change, but glyph identities, metrics and table-internal offsets do not.
    // Preserving a table is not acceptance of its own R/S/V/W profile.
    internal static bool IsKnownMetadataIndependentTable(uint tag) => tag is
        0x42415345 or 0x43424454 or 0x43424C43 or 0x43464620 or 0x43464632 or
        0x434F4C52 or 0x4350414C or 0x45424454 or 0x45424C43 or 0x45425343 or
        0x47444546 or 0x47504F53 or 0x47535542 or 0x48564152 or 0x4A535446 or
        0x4C545348 or 0x4D415448 or 0x4D455247 or 0x4D564152 or 0x4F532F32 or
        0x50434C54 or 0x53544154 or 0x53564720 or 0x56444D58 or 0x564F5247 or
        0x56564152 or 0x61766172 or 0x636D6170 or 0x63766172 or 0x63767420 or
        0x6670676D or 0x66766172 or 0x67617370 or 0x676C7966 or 0x67766172 or
        0x68646D78 or 0x68686561 or 0x686D7478 or 0x6B65726E or 0x6C6F6361 or
        0x6D617870 or 0x6D657461 or 0x706F7374 or 0x70726570 or 0x73626978 or
        0x76686561 or 0x766D7478;
}
