using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The first kern root writer (the GDEF/BASE root contract, scaled to the
/// length-driven sequence): one planned, atomically published v0 emission. The
/// canonical layout is the witnesses' own shape - the 4-byte header, then the
/// subtables in input order (the declaration order), each subtable's canonical bytes
/// from <see cref="KernSubtableCompiler"/>. There is nothing to search: the sequence
/// is linear, no offset crosses a subtable boundary (all three fmt2 offsets are
/// subtable-relative), so the plan is a single deterministic candidate whose only
/// bounded refusals are the u16 subtable length and the u16 count.
///
/// TryCreate admission mirrors the GDEF/BASE roots: the Apple dialect refuses new
/// writes (UnsupportedOperation at the version slot - the read-side-only boundary),
/// the subtable list must be non-empty and v0-typed, SameSource covers every
/// subtable, and the pair GID domain is re-answered against THIS root's explicit
/// numGlyphs (a subtable read under another context does not carry its proof over).
/// The edit witnesses re-root here: TryWithPairValue/TryWithKernValue produce updated
/// subtables, TryCreate rebuilds the root, TryCompile re-emits byte-identically for
/// unedited canonical sources.</summary>
public static class KernTableCompiler
{
    public static ReadStatus TryCreate(KernDialect dialect, int numGlyphs,
        IReadOnlyList<KernSubtable> subtables, out KernTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(subtables?.Sum(static s => s.Length) ?? 0, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (dialect != KernDialect.Version0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Version, 0), out diagnostic);
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (subtables is null || subtables.Count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 2), out diagnostic, 0ul, 1ul);
        if (subtables.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 2), out diagnostic,
                (ulong)subtables.Count, (ulong)ushort.MaxValue);
        var owned = new KernSubtable[subtables.Count];
        long total = KernLayout.HeaderV0;
        for (int i = 0; i < subtables.Count; i++)
        {
            var subtable = subtables[i];
            if (subtable is null || subtable.Dialect != KernDialect.Version0 ||
                !subtable.IsTyped || KernSubtableCompiler.Measure(subtable) > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Offset, (ulong)i, i), out diagnostic);
            // The GID domain is re-answered against THIS root's context (the read
            // face's hard check; a model carries its read-time proof, not a transfer).
            switch (subtable.Body)
            {
                case KernFormat0Body format0:
                    foreach (var pair in format0.Pairs)
                        if (pair.Left >= (ushort)numGlyphs || pair.Right >= (ushort)numGlyphs)
                            return ReadWire.Fail(ReadStatus.InvalidIndex,
                                DiagnosticCode.IndexOutOfRange,
                                location.AtLocal(DataField.GlyphId, (ulong)i, i), out diagnostic,
                                Math.Max(pair.Left, pair.Right), (ulong)numGlyphs);
                    break;
                case KernFormat2Body:
                    break; // the class range stays the OQ-4 observation under every context
            }
            if (!SameSource(location, subtable))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.None, 0), out diagnostic);
            owned[i] = subtable;
            total += KernSubtableCompiler.Measure(subtable);
        }
        if (total > int.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Length, 0), out diagnostic,
                (ulong)total, int.MaxValue);
        if (!budget.Work(total, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new KernTable(0, KernDialect.Version0, numGlyphs, owned, location,
            (int)total, exactTiling: true);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, KernSubtable subtable)
    {
        var child = subtable.Location;
        return location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
            (location.SourceId == 0 || location.TableTag == child.TableTag);
    }

    public static ReadStatus TryCompile(KernTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(KernTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(KernTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Every plan admission completes before the destination capacity check
        // (spec 7.3; the A6 1.5 O4 order); the destination is untouched on refusal.
        if (!budget.Work(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The one deterministic plan: header plus every subtable's canonical measure in
    // input order. The u16 subtable length domains were preflighted at TryCreate;
    // a raw or Apple-dialect member can never reach here.
    private static ReadStatus TryPlan(KernTable model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = KernLayout.HeaderV0;
        foreach (var subtable in model.Subtables)
        {
            int measure = KernSubtableCompiler.Measure(subtable);
            if (measure > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.Length,
                        (ulong)(length + 2), subtable.Index),
                    out diagnostic, (ulong)measure, (ulong)ushort.MaxValue);
            if (!budget.Work(measure, subtable.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            length += measure;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a planned model and a sufficient destination reach this path; each
    // subtable writes exactly once at its planned position.
    private static void WritePlanned(KernTable model, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(KernView.TryWriteVersion(destination, 0,
            out _, location));
        CoverageTableCompiler.RequireWritten(KernView.TryWriteLegacyCountAt(destination,
            2UL, new KernView.LegacyCountData(-1, (ushort)model.SubtableCount), out _, location));
        int cursor = KernLayout.HeaderV0;
        foreach (var subtable in model.Subtables)
        {
            int measure = KernSubtableCompiler.Measure(subtable);
            CoverageTableCompiler.RequireWritten(KernSubtableCompiler.Write(subtable,
                destination.Slice(cursor, measure), out _));
            cursor += measure;
        }
    }
}
