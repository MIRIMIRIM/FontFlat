using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

// The fmt2 class-domain refusals on the materialization path (the
// ContextualSubstView/ChainingSubstView ReadOwned faces). The Inspect path has
// pinned OQ-3's class-value direction since registration; these tests pin the
// same finite refusal on the model read the subset pipeline materializes
// through: every class value the (input) ClassDef assigns must index an
// existing rule-set slot, before an out-of-domain model escapes. Legal fonts
// are always self-consistent, so each corrupt fixture is a compiled wire with
// only its rule-set count shrunk, paired with the unpatched control.
public sealed class SubsetLayoutClassDomainReadTests
{
    private const int NumGlyphs = 10;

    private static void Ok(ReadStatus status)
    {
        Assert.Equal(ReadStatus.Done, status);
    }

    private static void NoDiagnostic(FontDiagnostic diagnostic)
    {
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
    }

    // ---- GSUB 5 format 2: the compiled wire shrinks its rule-set domain to one
    // slot while the ClassDef still assigns class 1 at range row 0 — the minimal
    // out-of-domain form. The read refuses InvalidIndex at the assigning row.
    [Fact]
    public void ContextualReadRefusesAClassValueBeyondTheRuleSetCount()
    {
        byte[] subtable = WithClassSeqRuleSetCount(ContextualWire(),
            ContextualSubstLayout.ClassSeqRuleSetCountField, 1);
        Ok(ContextualSubstView.TryOpen(subtable, out var view, out var open,
            DataLocation.Anonymous(SubsetFontFixture.GsubTag)));
        NoDiagnostic(open);
        var status = view!.TryRead(NumGlyphs, 1, out var model, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(0, diagnostic.Location.RecordOrdinal);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);
        Assert.Null(model);
    }

    [Fact]
    public void ContextualReadAcceptsTheSelfConsistentDomain()
    {
        Ok(ContextualSubstView.TryOpen(ContextualWire(), out var view, out var open,
            DataLocation.Anonymous(SubsetFontFixture.GsubTag)));
        NoDiagnostic(open);
        Ok(view!.TryRead(NumGlyphs, 1, out var model, out var diagnostic));
        NoDiagnostic(diagnostic);
        Assert.Equal(2, model!.Format);
        var sets = model.RuleSets!;
        Assert.Equal(3, sets.Count);
        Assert.Null(sets[0]);
        Assert.Equal(1, sets[1]!.RuleCount);
        Assert.Null(sets[2]);
        Assert.Equal(
            new ClassDefRange[] { new(1, 1, 1), new(3, 3, 2) },
            model.ClassDef!.Ranges.ToArray());
    }

    // ---- GSUB 6 format 2: the input definition's class values own the domain
    // (the backtrack def's class 4 never indexes the array, so the patch targets
    // the input range row's class 1 against the shrunk one-slot count).
    [Fact]
    public void ChainingReadRefusesAnInputClassValueBeyondTheRuleSetCount()
    {
        byte[] subtable = WithClassSeqRuleSetCount(ChainingWire(),
            ChainingSubstLayout.ClassSeqRuleSetCountField, 1);
        Ok(ChainingSubstView.TryOpen(subtable, out var view, out var open,
            DataLocation.Anonymous(SubsetFontFixture.GsubTag)));
        NoDiagnostic(open);
        var status = view!.TryRead(NumGlyphs, 1, out var model, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(0, diagnostic.Location.RecordOrdinal);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);
        Assert.Null(model);
    }

    [Fact]
    public void ChainingReadAcceptsTheSelfConsistentDomain()
    {
        Ok(ChainingSubstView.TryOpen(ChainingWire(), out var view, out var open,
            DataLocation.Anonymous(SubsetFontFixture.GsubTag)));
        NoDiagnostic(open);
        Ok(view!.TryRead(NumGlyphs, 1, out var model, out var diagnostic));
        NoDiagnostic(diagnostic);
        Assert.Equal(2, model!.Format);
        Assert.Equal(new ClassDefRange(2, 2, 4),
            model.BacktrackClassDef!.Ranges.ToArray()[0]);
        Assert.Equal(new ClassDefRange(2, 2, 1),
            model.InputClassDef!.Ranges.ToArray()[0]);
        Assert.Equal(new ClassDefRange(2, 2, 5),
            model.LookaheadClassDef!.Ranges.ToArray()[0]);
        var sets = model.RuleSets!;
        Assert.Equal(3, sets.Count);
        Assert.Null(sets[0]);
        Assert.Equal(1, sets[1]!.RuleCount);
        Assert.Null(sets[2]);
    }

    // The subset pipeline's materializing leg reaches the leaf through the
    // lookup walk, so the same patch inside a compiled GSUB must refuse the
    // whole-table read with the leaf's diagnostic intact.
    [Fact]
    public void WholeTableReadRefusesTheCorruptClassDomain()
    {
        byte[] gsub = CorruptGsub();
        Ok(LayoutListsView.TryOpen(gsub, out var view, out var open,
            DataLocation.Anonymous(SubsetFontFixture.GsubTag)));
        NoDiagnostic(open);
        var status = view!.TryReadTable(NumGlyphs, LayoutLookupKind.Gsub, null,
            out var model, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(0, diagnostic.Location.RecordOrdinal);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);
        Assert.Null(model);
    }

    // ---- Fixtures: the self-consistent shapes the compilers emit (one rule set
    // over three class slots), handed to the tests as raw subtable wire.
    private static byte[] ContextualWire()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1 }, NumGlyphs, 1,
            out var coverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate(
            [new ClassDefRange(1, 1, 1), new ClassDefRange(3, 3, 2)], NumGlyphs, 2,
            out var classDef, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRule(stackalloc ushort[] { 1 },
            [new SequenceLookupRecord(0, 0)], 1, out var rule, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRuleSet([rule!], out var ruleSet,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreate(coverage!, classDef!,
            new ContextualRuleSet?[] { null, ruleSet, null }, NumGlyphs, out var contextual,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCompile(contextual!, out var subtable,
            out diagnostic));
        NoDiagnostic(diagnostic);
        return subtable!;
    }

    private static byte[] ChainingWire()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 2 }, NumGlyphs, 1,
            out var coverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate([new ClassDefRange(2, 2, 4)], NumGlyphs, 2,
            out var backtrackDef, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate(
            [new ClassDefRange(2, 2, 1), new ClassDefRange(5, 5, 2)], NumGlyphs, 2,
            out var inputDef, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate([new ClassDefRange(2, 2, 5)], NumGlyphs, 2,
            out var lookaheadDef, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ChainingSubstTableCompiler.TryCreateRule([],
            stackalloc ushort[] { 1 }, [], [new SequenceLookupRecord(0, 0)], 1,
            out var rule, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ChainingSubstTableCompiler.TryCreateRuleSet([rule!], out var ruleSet,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ChainingSubstTableCompiler.TryCreate(coverage!, backtrackDef!, inputDef!,
            lookaheadDef!, new ChainingRuleSet?[] { null, ruleSet, null }, NumGlyphs,
            out var chaining, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ChainingSubstTableCompiler.TryCompile(chaining!, out var subtable,
            out diagnostic));
        NoDiagnostic(diagnostic);
        return subtable!;
    }

    // The compiled self-consistent GSUB with its subtable's rule-set count
    // shrunk in place: the lookup list chain (GSUB header -> lookup list ->
    // first lookup -> first subtable) locates the count field's absolute offset.
    private static byte[] CorruptGsub()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1 }, NumGlyphs, 1,
            out var coverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate(
            [new ClassDefRange(1, 1, 1), new ClassDefRange(3, 3, 2)], NumGlyphs, 2,
            out var classDef, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRule(stackalloc ushort[] { 1 },
            [new SequenceLookupRecord(0, 0)], 1, out var rule, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRuleSet([rule!], out var ruleSet,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreate(coverage!, classDef!,
            new ContextualRuleSet?[] { null, ruleSet, null }, NumGlyphs, out var contextual,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateContextualSubst(NumGlyphs, [contextual!], 0,
            null, null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(GsubTableCompiler.TryCreate(LayoutListsLayout.Version10, 0, [], [],
            [lookup!], NumGlyphs, out var gsubRoot, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(GsubTableCompiler.TryCompile(gsubRoot!, null, out var gsub, out diagnostic));
        NoDiagnostic(diagnostic);
        int lookupListAt = BinaryPrimitives.ReadUInt16BigEndian(gsub!.AsSpan(8, 2));
        int lookupAt = lookupListAt + BinaryPrimitives.ReadUInt16BigEndian(
            gsub.AsSpan(lookupListAt + 2, 2));
        int subtableAt = lookupAt + BinaryPrimitives.ReadUInt16BigEndian(
            gsub.AsSpan(lookupAt + LookupLayout.HeaderLength, 2));
        return WithClassSeqRuleSetCount(gsub!,
            subtableAt + ContextualSubstLayout.ClassSeqRuleSetCountField, 1);
    }

    private static byte[] WithClassSeqRuleSetCount(byte[] wire, int at, ushort count)
    {
        byte[] patched = (byte[])wire.Clone();
        BinaryPrimitives.WriteUInt16BigEndian(patched.AsSpan(at, 2), count);
        return patched;
    }
}
