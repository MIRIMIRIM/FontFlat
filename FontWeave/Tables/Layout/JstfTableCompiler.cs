using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The first JSTF root writer: one planned, atomically published 1.0-only
/// emission whose canonical layout is the kind-grouped DFS shape - the header, the
/// script record array, then every unique script body, extender list, langsys body,
/// priority body, modlist body, JstfMax body and inline lookup body in first-visit
/// order (the BASE kind-grouped precedent, scaled down). The grouping is what keeps
/// every edge strictly forward on the wire: each kind group references only later
/// groups, so a block shared across scripts still points forward from every
/// referrer. Shared blocks dedupe by ReferenceEquals first, then same-kind content
/// identity for the JSTF-owned kinds (the T4-01 rules); the inline lookups dedupe
/// by instance only - a deep lookup identity comparison would re-implement GPOS
/// semantics this compiler must not own (the registered lean). There is no second
/// candidate: the record order and the array orders are semantic, so nothing may
/// swap - the single deterministic plan's only bounded refusals are the Offset16
/// domains through the generated slot helpers (the finite-refusal contract).
///
/// TryCreate admission mirrors the GDEF/BASE roots: 1.0 only, the glyph and both
/// neighbor-count authorities re-answered against THIS root's explicit inputs (a
/// subtree read under another context does not carry its proof over), SameSource
/// over every script, and the JstfMax face re-answered on every inline lookup
/// (kind, source and numGlyphs identity, the flags contract and OQ-3's direct and
/// wrapped {7,8} ban). The edit witnesses re-root here: TryWithLookupIndex and
/// TryWithExtenderGlyph produce updated bodies, TryCreate rebuilds the root, and
/// TryCompile re-emits byte-identically for unedited canonical sources (the
/// write-read-write fixed point).</summary>
public static class JstfTableCompiler
{
    // ------------------------------------------------------------------ factory

    /// <summary>Explicit root-model factory: the version, both authorities, the GDEF
    /// context and the whole script tree are caller-supplied and never inferred. A
    /// null or empty script list is the legal empty table (the read face's OQ-5
    /// family); the consumed length of a factory model is zero (no source extent -
    /// the SinglePosTable factory convention).</summary>
    public static ReadStatus TryCreate(uint version, int numGlyphs, int gsubLookupCount,
        int gposLookupCount, GdefLookupContext? context, IReadOnlyList<JstfScriptRecord>? scripts,
        out JstfTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(scripts?.Count ?? 0, location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (version != JstfLayout.Version10)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, version);
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (gsubLookupCount < 0 || gposLookupCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.None, 0), out diagnostic,
                unchecked((ulong)(long)(gsubLookupCount < 0 ? gsubLookupCount : gposLookupCount)), 0ul);
        int count = scripts?.Count ?? 0;
        var owned = new JstfScriptRecord[count];
        for (int i = 0; i < count; i++)
        {
            var record = scripts![i];
            var script = record.Script;
            if (script is null || !SameSource(location, script.Location))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Tag,
                        (ulong)(JstfLayout.ScriptRecordsAt + i * JstfLayout.ScriptRecordSize), i),
                    out diagnostic);
            status = AnswerScript(script, location, numGlyphs, gsubLookupCount, gposLookupCount,
                context, out diagnostic);
            if (status != ReadStatus.Done) return status;
            owned[i] = record;
        }
        if (!budget.Work(count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(version, numGlyphs, gsubLookupCount, gposLookupCount, context, owned,
            location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, DataLocation child) =>
        location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
        (location.SourceId == 0 || location.TableTag == child.TableTag);

    // ------------------------------------------------------------- leaf factories

    // The per-level explicit factories (the BaseAxisTableCompiler shape): a consumer
    // edits a leaf through its TryWith* witness and rebuilds the tree upward through
    // these, then re-roots through the root TryCreate. Every domain the factory can
    // answer, it answers here; the root re-answers everything again.

    /// <summary>Builds an ExtenderGlyph list; every id answers the explicit numGlyphs
    /// domain (the kern OQ-4 hard half).</summary>
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, int numGlyphs,
        out JstfExtenderGlyphTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(glyphs.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, JstfExtenderGlyphLayout.CountAt), out diagnostic,
                (ulong)glyphs.Length, ushort.MaxValue);
        for (int i = 0; i < glyphs.Length; i++)
        {
            if (glyphs[i] >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId,
                        (ulong)(JstfExtenderGlyphLayout.GlyphsAt + i * JstfExtenderGlyphLayout.GlyphStride), i),
                    out diagnostic, glyphs[i], (ulong)numGlyphs);
        }
        if (!budget.Buffer(2L * glyphs.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(glyphs.ToArray(), location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one JstfModList; the domain is the referencing slot's kind
    /// carried explicitly and every index answers that neighbor LookupList count
    /// here (the section 3.2 authority at the factory).</summary>
    public static ReadStatus TryCreate(JstfLookupDomain domain, ReadOnlySpan<ushort> indices,
        int domainLookupCount, out JstfModListTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(indices.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var status = LayoutListsTableCompiler.CheckCount(indices.Length, location,
            JstfModListLayout.CountAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (domainLookupCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.None, 0), out diagnostic,
                unchecked((ulong)(long)domainLookupCount), 0ul);
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] >= domainLookupCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload,
                        (ulong)(JstfModListLayout.IndicesAt + i * JstfModListLayout.IndexStride), i),
                    out diagnostic, indices[i], (ulong)domainLookupCount);
        }
        if (!budget.Buffer(2L * indices.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(domain, indices.ToArray(), location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one JstfLangSys over the priority list in priority order (the
    /// array order is the semantic order; never re-sorted).</summary>
    public static ReadStatus TryCreate(IReadOnlyList<JstfPriorityTable> priorities,
        out JstfLangSysTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(priorities);
        var budget = new BuildBudget(limits);
        if (!budget.Input(priorities.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var status = LayoutListsTableCompiler.CheckCount(priorities.Count, location,
            JstfLangSysLayout.CountAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        foreach (var priority in priorities)
        {
            if (priority is null)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Offset, JstfLangSysLayout.OffsetsAt), out diagnostic);
        }
        if (!budget.Buffer(8L * priorities.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var owned = new JstfPriorityTable[priorities.Count];
        for (int i = 0; i < priorities.Count; i++) owned[i] = priorities[i];
        model = new(owned, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one JstfPriority over the ten Optional slots in wire order;
    /// null keeps the spec's "may be NULL" slot.</summary>
    public static ReadStatus TryCreate(JstfModListTable? gsubShrinkageEnable,
        JstfModListTable? gsubShrinkageDisable, JstfModListTable? gposShrinkageEnable,
        JstfModListTable? gposShrinkageDisable, JstfMaxTable? shrinkageJstfMax,
        JstfModListTable? gsubExtensionEnable, JstfModListTable? gsubExtensionDisable,
        JstfModListTable? gposExtensionEnable, JstfModListTable? gposExtensionDisable,
        JstfMaxTable? extensionJstfMax, out JstfPriorityTable? model,
        out FontDiagnostic diagnostic, DataLocation location = default)
    {
        model = new(gsubShrinkageEnable, gsubShrinkageDisable, gposShrinkageEnable,
            gposShrinkageDisable, shrinkageJstfMax, gsubExtensionEnable, gsubExtensionDisable,
            gposExtensionEnable, gposExtensionDisable, extensionJstfMax, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds one JstfScript over both Optional slots and the langsys rows
    /// in wire order.</summary>
    public static ReadStatus TryCreate(JstfExtenderGlyphTable? extenderGlyphs,
        JstfLangSysTable? defaultLangSys, IReadOnlyList<JstfLangSysRecord> langSys,
        out JstfScriptTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(langSys);
        var budget = new BuildBudget(limits);
        if (!budget.Input(langSys.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var status = LayoutListsTableCompiler.CheckCount(langSys.Count, location,
            JstfScriptLayout.LangSysCountAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        foreach (var row in langSys)
        {
            if (row.LangSys is null)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Tag,
                        (ulong)(JstfScriptLayout.LangSysRecordsAt + 4)), out diagnostic);
        }
        if (!budget.Buffer(16L * langSys.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var owned = new JstfLangSysRecord[langSys.Count];
        for (int i = 0; i < langSys.Count; i++) owned[i] = langSys[i];
        model = new(extenderGlyphs, defaultLangSys, owned, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The authority re-answer: a model read under another context carries its proof
    // as a value, never as a transfer (the kern root's re-answer rule). Extender
    // GIDs re-answer against this root's numGlyphs, every modlist index against the
    // domain its slot kind names, every inline lookup against the source/numGlyphs
    // identity, the flags contract and the OQ-3 exclusion.
    private static ReadStatus AnswerScript(JstfScriptTable script, DataLocation location,
        int numGlyphs, int gsubLookupCount, int gposLookupCount, GdefLookupContext? context,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = ReadStatus.Done;
        diagnostic = default;
        if (script.ExtenderGlyphs is { } extenders)
        {
            var glyphs = extenders.Glyphs;
            for (int i = 0; i < glyphs.Count; i++)
            {
                if (glyphs[i] >= (ushort)numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        extenders.Location.AtLocal(DataField.GlyphId,
                            (ulong)(JstfExtenderGlyphLayout.GlyphsAt + i * JstfExtenderGlyphLayout.GlyphStride), i),
                        out diagnostic, glyphs[i], (ulong)numGlyphs);
            }
        }
        if (script.DefaultLangSys is { } defLangSys &&
            (status = AnswerLangSys(defLangSys, location, numGlyphs, gsubLookupCount,
                gposLookupCount, context, out diagnostic)) != ReadStatus.Done)
            return status;
        foreach (var row in script.LangSys)
        {
            if ((status = AnswerLangSys(row.LangSys, location, numGlyphs, gsubLookupCount,
                gposLookupCount, context, out diagnostic)) != ReadStatus.Done)
                return status;
        }
        return status;
    }

    private static ReadStatus AnswerLangSys(JstfLangSysTable langSys, DataLocation location,
        int numGlyphs, int gsubLookupCount, int gposLookupCount, GdefLookupContext? context,
        out FontDiagnostic diagnostic)
    {
        foreach (var priority in langSys.Priorities)
        {
            ReadStatus status;
            if ((status = AnswerModList(priority.GsubShrinkageEnable, gsubLookupCount, out diagnostic)) != ReadStatus.Done ||
                (status = AnswerModList(priority.GsubShrinkageDisable, gsubLookupCount, out diagnostic)) != ReadStatus.Done ||
                (status = AnswerModList(priority.GposShrinkageEnable, gposLookupCount, out diagnostic)) != ReadStatus.Done ||
                (status = AnswerModList(priority.GposShrinkageDisable, gposLookupCount, out diagnostic)) != ReadStatus.Done ||
                (status = AnswerMax(priority.ShrinkageJstfMax, location, numGlyphs, context, out diagnostic)) != ReadStatus.Done ||
                (status = AnswerModList(priority.GsubExtensionEnable, gsubLookupCount, out diagnostic)) != ReadStatus.Done ||
                (status = AnswerModList(priority.GsubExtensionDisable, gsubLookupCount, out diagnostic)) != ReadStatus.Done ||
                (status = AnswerModList(priority.GposExtensionEnable, gposLookupCount, out diagnostic)) != ReadStatus.Done ||
                (status = AnswerModList(priority.GposExtensionDisable, gposLookupCount, out diagnostic)) != ReadStatus.Done ||
                (status = AnswerMax(priority.ExtensionJstfMax, location, numGlyphs, context, out diagnostic)) != ReadStatus.Done)
                return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus AnswerModList(JstfModListTable? modList, int domainCount,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (modList is null) return ReadStatus.Done;
        var indices = modList.LookupIndices;
        for (int i = 0; i < indices.Count; i++)
        {
            if (indices[i] >= domainCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    modList.Location.AtLocal(DataField.Payload,
                        (ulong)(JstfModListLayout.IndicesAt + i * JstfModListLayout.IndexStride), i),
                    out diagnostic, indices[i], (ulong)domainCount);
        }
        return ReadStatus.Done;
    }

    private static ReadStatus AnswerMax(JstfMaxTable? max, DataLocation location,
        int numGlyphs, GdefLookupContext? context, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (max is null) return ReadStatus.Done;
        foreach (var lookup in max.Lookups)
        {
            if (lookup.Kind != LayoutLookupKind.Gpos || lookup.NumGlyphs != numGlyphs ||
                !SameSource(location, lookup.Location))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    lookup.Location, out diagnostic, (ulong)lookup.NumGlyphs, (ulong)numGlyphs);
            if (lookup.LookupType is 7 or 8)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    lookup.Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic,
                    lookup.LookupType, 0ul);
            if (lookup.LookupType == 9 && lookup.ExtensionPositions is { } extensions)
            {
                foreach (var extension in extensions)
                {
                    if (extension.ExtensionLookupType is 7 or 8)
                        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                            extension.Location.AtLocal(DataField.Tag, ExtensionPosLayout.TypeAt),
                            out diagnostic, extension.ExtensionLookupType, 0ul);
                }
            }
            var status = LookupTable.ValidateFlags(numGlyphs, lookup.Flags, lookup.MarkFilteringSet,
                lookup.SubtableCount, context, lookup.Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        return ReadStatus.Done;
    }

    // ------------------------------------------------------------------ plan

    /// <summary>The dedupe-aware placement: the first-visit bodies (the
    /// representatives) in wire order with their positions, plus the block to
    /// representative map every referencing slot resolves through. Collection runs
    /// in dependency order (modlists and maxes, then priorities, langsys bodies,
    /// extenders and script bodies) so every content comparison sees its children's
    /// representatives; position assignment runs in wire order.</summary>
    private sealed class Placement
    {
        internal int Length;
        internal List<JstfScriptRecord> Records = new();
        internal List<JstfScriptTable> Scripts = new();
        internal List<JstfExtenderGlyphTable> Extenders = new();
        internal List<JstfLangSysTable> LangSysBodies = new();
        internal List<JstfPriorityTable> Priorities = new();
        internal List<JstfModListTable> ModLists = new();
        internal List<JstfMaxTable> Maxes = new();
        internal List<LookupTable> Lookups = new();
        internal Dictionary<JstfScriptTable, JstfScriptTable> ScriptRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfExtenderGlyphTable, JstfExtenderGlyphTable> ExtenderRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfLangSysTable, JstfLangSysTable> LangSysRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfPriorityTable, JstfPriorityTable> PriorityRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfModListTable, JstfModListTable> ModListRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfMaxTable, JstfMaxTable> MaxRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfScriptTable, int> ScriptAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfExtenderGlyphTable, int> ExtenderAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfLangSysTable, int> LangSysAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfPriorityTable, int> PriorityAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfModListTable, int> ModListAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<JstfMaxTable, int> MaxAt = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<LookupTable, int> LookupAt = new(ReferenceEqualityComparer.Instance);
    }

    public static ReadStatus TryCompile(JstfTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(JstfTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan!.Length, model.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes, budget, out diagnostic);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(JstfTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Every plan admission completes before the destination capacity check (spec
        // 7.3; the A6 1.5 O4 order); the destination is untouched on refusal.
        if (!budget.Work(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (destination.Length < plan.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination, budget, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The one deterministic plan.
    private static ReadStatus TryPlan(JstfTable model, out Placement? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        var placement = new Placement();
        // First visit order: the records keep their input order (the array order is
        // semantic and never re-sorted), then the DFS over each script's subtree.
        foreach (var record in model.Scripts)
        {
            placement.Records.Add(record);
            CollectScript(record.Script, placement);
        }
        // Wire order: header, records, script bodies, extenders, langsys, priorities,
        // modlists, maxes, lookups. Each kind group references only later groups.
        int cursor = JstfLayout.Header10 + JstfLayout.ScriptRecordSize * placement.Records.Count;
        if (!budget.Work(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        foreach (var script in placement.Scripts)
        {
            placement.ScriptAt[script] = cursor;
            int length = JstfScriptLayout.HeaderLength +
                JstfScriptLayout.LangSysRecordSize * script.LangSysCount;
            if (!budget.Work(length, script.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            cursor += length;
        }
        foreach (var extenders in placement.Extenders)
        {
            placement.ExtenderAt[extenders] = cursor;
            if (!budget.Work(ExtenderLength(extenders), extenders.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            cursor += ExtenderLength(extenders);
        }
        foreach (var langSys in placement.LangSysBodies)
        {
            placement.LangSysAt[langSys] = cursor;
            int length = JstfLangSysLayout.HeaderLength +
                JstfLangSysLayout.OffsetStride * langSys.PriorityCount;
            if (!budget.Work(length, langSys.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            cursor += length;
        }
        foreach (var priority in placement.Priorities)
        {
            placement.PriorityAt[priority] = cursor;
            if (!budget.Work(JstfPriorityLayout.HeaderLength, priority.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            cursor += JstfPriorityLayout.HeaderLength;
        }
        foreach (var modList in placement.ModLists)
        {
            placement.ModListAt[modList] = cursor;
            int length = JstfModListLayout.HeaderLength +
                JstfModListLayout.IndexStride * modList.LookupCount;
            if (!budget.Work(length, modList.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            cursor += length;
        }
        foreach (var max in placement.Maxes)
        {
            placement.MaxAt[max] = cursor;
            int length = JstfMaxLayout.HeaderLength + JstfMaxLayout.OffsetStride * max.LookupCount;
            if (!budget.Work(length, max.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            cursor += length;
        }
        foreach (var lookup in placement.Lookups)
        {
            placement.LookupAt[lookup] = cursor;
            var status = LookupTableCompiler.TryPlan(lookup, model.Context, out var lookupPlan,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            cursor += lookupPlan!.Length;
        }
        placement.Length = cursor;
        plan = placement;
        return Preflight(model, placement, out diagnostic);
    }

    private static int ExtenderLength(JstfExtenderGlyphTable extenders) =>
        JstfExtenderGlyphLayout.GlyphsAt + JstfExtenderGlyphLayout.GlyphStride * extenders.GlyphCount;

    // The DFS collection with the shared-identity dedupe: ReferenceEquals first,
    // then same-kind content identity (the T4-01 rules). Children collect before
    // their containers so every content comparison sees the children's
    // representatives.
    private static void CollectScript(JstfScriptTable script, Placement placement)
    {
        if (script.ExtenderGlyphs is { } extenders)
            CollectExtenders(extenders, placement);
        if (script.DefaultLangSys is { } defLangSys)
            CollectLangSys(defLangSys, placement);
        foreach (var row in script.LangSys)
            CollectLangSys(row.LangSys, placement);
        if (placement.ScriptRep.ContainsKey(script)) return;
        var representative = Find(placement.Scripts, script, (a, b) => SameScript(a, b, placement));
        placement.ScriptRep[script] = representative ?? script;
        if (representative is null) placement.Scripts.Add(script);
    }

    private static void CollectExtenders(JstfExtenderGlyphTable extenders, Placement placement)
    {
        if (placement.ExtenderRep.ContainsKey(extenders)) return;
        var representative = Find(placement.Extenders, extenders, SameExtenders);
        placement.ExtenderRep[extenders] = representative ?? extenders;
        if (representative is null) placement.Extenders.Add(extenders);
    }

    private static void CollectLangSys(JstfLangSysTable langSys, Placement placement)
    {
        if (placement.LangSysRep.ContainsKey(langSys)) return;
        foreach (var priority in langSys.Priorities)
            CollectPriority(priority, placement);
        var representative = Find(placement.LangSysBodies, langSys, (a, b) => SameLangSys(a, b, placement));
        placement.LangSysRep[langSys] = representative ?? langSys;
        if (representative is null) placement.LangSysBodies.Add(langSys);
    }

    private static void CollectPriority(JstfPriorityTable priority, Placement placement)
    {
        if (placement.PriorityRep.ContainsKey(priority)) return;
        CollectModList(priority.GsubShrinkageEnable, placement);
        CollectModList(priority.GsubShrinkageDisable, placement);
        CollectModList(priority.GposShrinkageEnable, placement);
        CollectModList(priority.GposShrinkageDisable, placement);
        CollectMax(priority.ShrinkageJstfMax, placement);
        CollectModList(priority.GsubExtensionEnable, placement);
        CollectModList(priority.GsubExtensionDisable, placement);
        CollectModList(priority.GposExtensionEnable, placement);
        CollectModList(priority.GposExtensionDisable, placement);
        CollectMax(priority.ExtensionJstfMax, placement);
        var representative = Find(placement.Priorities, priority, (a, b) => SamePriority(a, b, placement));
        placement.PriorityRep[priority] = representative ?? priority;
        if (representative is null) placement.Priorities.Add(priority);
    }

    private static void CollectModList(JstfModListTable? modList, Placement placement)
    {
        if (modList is null || placement.ModListRep.ContainsKey(modList)) return;
        // The mod list wire is domain-blind (the indices carry no kind), so the
        // content identity compares the index sequences only; the domain rides the
        // owning model and the read face re-assigns it per referencing slot.
        var representative = Find(placement.ModLists, modList, SameIndices);
        placement.ModListRep[modList] = representative ?? modList;
        if (representative is null) placement.ModLists.Add(modList);
    }

    private static void CollectMax(JstfMaxTable? max, Placement placement)
    {
        if (max is null || placement.MaxRep.ContainsKey(max)) return;
        foreach (var lookup in max.Lookups)
        {
            if (!placement.LookupAt.ContainsKey(lookup))
            {
                placement.LookupAt[lookup] = -1;
                placement.Lookups.Add(lookup);
            }
        }
        var representative = Find(placement.Maxes, max, SameMax);
        placement.MaxRep[max] = representative ?? max;
        if (representative is null) placement.Maxes.Add(max);
    }

    private static T? Find<T>(List<T> bodies, T candidate, Func<T, T, bool> same) where T : class
    {
        foreach (var body in bodies)
            if (same(body, candidate)) return body;
        return null;
    }

    private static bool SameScript(JstfScriptTable a, JstfScriptTable b, Placement placement)
    {
        bool SameExtender(JstfExtenderGlyphTable? x, JstfExtenderGlyphTable? y) =>
            x is null ? y is null
                : y is not null && ReferenceEquals(placement.ExtenderRep[x], placement.ExtenderRep[y]);
        bool SameLangSys(JstfLangSysTable? x, JstfLangSysTable? y) =>
            x is null ? y is null
                : y is not null && ReferenceEquals(placement.LangSysRep[x], placement.LangSysRep[y]);
        if (!SameExtender(a.ExtenderGlyphs, b.ExtenderGlyphs)) return false;
        if (!SameLangSys(a.DefaultLangSys, b.DefaultLangSys)) return false;
        if (a.LangSysCount != b.LangSysCount) return false;
        for (int i = 0; i < a.LangSysCount; i++)
            if (!SameLangSys(a.LangSys[i].LangSys, b.LangSys[i].LangSys)) return false;
        return true;
    }

    private static bool SameExtenders(JstfExtenderGlyphTable a, JstfExtenderGlyphTable b)
    {
        if (a.GlyphCount != b.GlyphCount) return false;
        for (int i = 0; i < a.GlyphCount; i++)
            if (a.Glyphs[i] != b.Glyphs[i]) return false;
        return true;
    }

    private static bool SameIndices(JstfModListTable a, JstfModListTable b)
    {
        if (a.LookupCount != b.LookupCount) return false;
        for (int i = 0; i < a.LookupCount; i++)
            if (a.LookupIndices[i] != b.LookupIndices[i]) return false;
        return true;
    }

    private static bool SameMax(JstfMaxTable a, JstfMaxTable b)
    {
        if (a.LookupCount != b.LookupCount) return false;
        for (int i = 0; i < a.LookupCount; i++)
            if (!ReferenceEquals(a.Lookups[i], b.Lookups[i])) return false;
        return true;
    }

    private static bool SamePriority(JstfPriorityTable a, JstfPriorityTable b, Placement placement)
    {
        bool SameModList(JstfModListTable? x, JstfModListTable? y) =>
            x is null ? y is null
                : y is not null && ReferenceEquals(placement.ModListRep[x], placement.ModListRep[y]);
        bool SameJstfMax(JstfMaxTable? x, JstfMaxTable? y) =>
            x is null ? y is null
                : y is not null && ReferenceEquals(placement.MaxRep[x], placement.MaxRep[y]);
        return SameModList(a.GsubShrinkageEnable, b.GsubShrinkageEnable) &&
            SameModList(a.GsubShrinkageDisable, b.GsubShrinkageDisable) &&
            SameModList(a.GposShrinkageEnable, b.GposShrinkageEnable) &&
            SameModList(a.GposShrinkageDisable, b.GposShrinkageDisable) &&
            SameJstfMax(a.ShrinkageJstfMax, b.ShrinkageJstfMax) &&
            SameModList(a.GsubExtensionEnable, b.GsubExtensionEnable) &&
            SameModList(a.GsubExtensionDisable, b.GsubExtensionDisable) &&
            SameModList(a.GposExtensionEnable, b.GposExtensionEnable) &&
            SameModList(a.GposExtensionDisable, b.GposExtensionDisable) &&
            SameJstfMax(a.ExtensionJstfMax, b.ExtensionJstfMax);
    }

    private static bool SameLangSys(JstfLangSysTable a, JstfLangSysTable b, Placement placement)
    {
        if (a.PriorityCount != b.PriorityCount) return false;
        for (int i = 0; i < a.PriorityCount; i++)
            if (!ReferenceEquals(placement.PriorityRep[a.Priorities[i]],
                placement.PriorityRep[b.Priorities[i]])) return false;
        return true;
    }

    // ------------------------------------------------------------------ preflight

    // Every offset slot preflights its final target through its generated helper;
    // a too-narrow target refuses the whole plan (UnsupportedOperation with the
    // slot's Detail/Expected). The max lookup offsets have no generated helper (no
    // declared reference, OQ-6), so their domain prefights by hand.
    private static ReadStatus Preflight(JstfTable model, Placement plan, out FontDiagnostic diagnostic)
    {
        var location = model.Location;
        ReadStatus status;
        for (int i = 0; i < plan.Records.Count; i++)
        {
            var record = plan.Records[i];
            status = JstfView.TryGetScriptOffsetValue(present: true, 0UL,
                (ulong)plan.ScriptAt[plan.ScriptRep[record.Script]], out _, out diagnostic,
                location.AtLocal(DataField.Offset,
                    (ulong)(JstfLayout.ScriptRecordsAt + i * JstfLayout.ScriptRecordSize + 4), i));
            if (status != ReadStatus.Done) return status;
        }
        foreach (var script in plan.Scripts)
        {
            int scriptAt = plan.ScriptAt[script];
            status = JstfScriptView.TryGetExtenderGlyphOffsetValue(script.ExtenderGlyphs is not null,
                0UL, (ulong)(script.ExtenderGlyphs is null ? 0
                    : plan.ExtenderAt[plan.ExtenderRep[script.ExtenderGlyphs]] - scriptAt),
                out _, out diagnostic,
                script.Location.AtLocal(DataField.Offset, JstfScriptLayout.ExtenderGlyphOffsetAt));
            if (status != ReadStatus.Done) return status;
            status = JstfScriptView.TryGetDefJstfLangSysOffsetValue(script.DefaultLangSys is not null,
                0UL, (ulong)(script.DefaultLangSys is null ? 0
                    : plan.LangSysAt[plan.LangSysRep[script.DefaultLangSys]] - scriptAt),
                out _, out diagnostic,
                script.Location.AtLocal(DataField.Offset, JstfScriptLayout.DefJstfLangSysOffsetAt));
            if (status != ReadStatus.Done) return status;
            for (int i = 0; i < script.LangSysCount; i++)
            {
                var row = script.LangSys[i];
                status = JstfScriptView.TryGetLangSysOffsetValue(present: true, 0UL,
                    (ulong)(plan.LangSysAt[plan.LangSysRep[row.LangSys]] - scriptAt), out _, out diagnostic,
                    script.Location.AtLocal(DataField.Offset,
                        (ulong)(JstfScriptLayout.LangSysRecordsAt + i * JstfScriptLayout.LangSysRecordSize + 4), i));
                if (status != ReadStatus.Done) return status;
            }
        }
        foreach (var langSys in plan.LangSysBodies)
        {
            int langSysAt = plan.LangSysAt[langSys];
            for (int i = 0; i < langSys.PriorityCount; i++)
            {
                status = JstfLangSysView.TryGetPriorityOffsetValue(present: true, 0UL,
                    (ulong)(plan.PriorityAt[plan.PriorityRep[langSys.Priorities[i]]] - langSysAt),
                    out _, out diagnostic,
                    langSys.Location.AtLocal(DataField.Offset,
                        (ulong)(JstfLangSysLayout.OffsetsAt + i * JstfLangSysLayout.OffsetStride), i));
                if (status != ReadStatus.Done) return status;
            }
        }
        foreach (var priority in plan.Priorities)
        {
            int priorityAt = plan.PriorityAt[priority];
            if ((status = PreflightSlot(priority.GsubShrinkageEnable, priorityAt, plan,
                    JstfPriorityView.TryGetGsubShrinkageEnableOffsetValue,
                    priority.Location, JstfPriorityLayout.GsubShrinkageEnableAt, out diagnostic)) != ReadStatus.Done ||
                (status = PreflightSlot(priority.GsubShrinkageDisable, priorityAt, plan,
                    JstfPriorityView.TryGetGsubShrinkageDisableOffsetValue,
                    priority.Location, JstfPriorityLayout.GsubShrinkageDisableAt, out diagnostic)) != ReadStatus.Done ||
                (status = PreflightSlot(priority.GposShrinkageEnable, priorityAt, plan,
                    JstfPriorityView.TryGetGposShrinkageEnableOffsetValue,
                    priority.Location, JstfPriorityLayout.GposShrinkageEnableAt, out diagnostic)) != ReadStatus.Done ||
                (status = PreflightSlot(priority.GposShrinkageDisable, priorityAt, plan,
                    JstfPriorityView.TryGetGposShrinkageDisableOffsetValue,
                    priority.Location, JstfPriorityLayout.GposShrinkageDisableAt, out diagnostic)) != ReadStatus.Done ||
                (status = PreflightSlot(priority.ShrinkageJstfMax, priorityAt, plan,
                    JstfPriorityView.TryGetShrinkageJstfMaxOffsetValue,
                    priority.Location, JstfPriorityLayout.ShrinkageJstfMaxAt, out diagnostic)) != ReadStatus.Done ||
                (status = PreflightSlot(priority.GsubExtensionEnable, priorityAt, plan,
                    JstfPriorityView.TryGetGsubExtensionEnableOffsetValue,
                    priority.Location, JstfPriorityLayout.GsubExtensionEnableAt, out diagnostic)) != ReadStatus.Done ||
                (status = PreflightSlot(priority.GsubExtensionDisable, priorityAt, plan,
                    JstfPriorityView.TryGetGsubExtensionDisableOffsetValue,
                    priority.Location, JstfPriorityLayout.GsubExtensionDisableAt, out diagnostic)) != ReadStatus.Done ||
                (status = PreflightSlot(priority.GposExtensionEnable, priorityAt, plan,
                    JstfPriorityView.TryGetGposExtensionEnableOffsetValue,
                    priority.Location, JstfPriorityLayout.GposExtensionEnableAt, out diagnostic)) != ReadStatus.Done ||
                (status = PreflightSlot(priority.GposExtensionDisable, priorityAt, plan,
                    JstfPriorityView.TryGetGposExtensionDisableOffsetValue,
                    priority.Location, JstfPriorityLayout.GposExtensionDisableAt, out diagnostic)) != ReadStatus.Done ||
                (status = PreflightSlot(priority.ExtensionJstfMax, priorityAt, plan,
                    JstfPriorityView.TryGetExtensionJstfMaxOffsetValue,
                    priority.Location, JstfPriorityLayout.ExtensionJstfMaxAt, out diagnostic)) != ReadStatus.Done)
                return status;
        }
        foreach (var max in plan.Maxes)
        {
            int maxAt = plan.MaxAt[max];
            for (int i = 0; i < max.LookupCount; i++)
            {
                int delta = plan.LookupAt[max.Lookups[i]] - maxAt;
                if (delta is < 1 or > ushort.MaxValue)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                        max.Location.AtLocal(DataField.Offset,
                            (ulong)(JstfMaxLayout.OffsetsAt + i * JstfMaxLayout.OffsetStride), i),
                        out diagnostic, (ulong)delta, ushort.MaxValue);
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private delegate ReadStatus SlotValue(bool present, ulong origin, ulong target,
        out ushort encoded, out FontDiagnostic diagnostic, DataLocation location);

    private static ReadStatus PreflightSlot<T>(T? block, int priorityAt, Placement plan,
        SlotValue encode, DataLocation location, int fieldAt, out FontDiagnostic diagnostic)
        where T : class
    {
        int delta = block is null ? 0 : PositionOf(block, plan) - priorityAt;
        return encode(block is not null, 0UL, (ulong)delta, out _, out diagnostic,
            location.AtLocal(DataField.Offset, (ulong)fieldAt));
    }

    // The planned position of a block's representative.
    private static int PositionOf<T>(T block, Placement plan) where T : class => block switch
    {
        JstfModListTable modList => plan.ModListAt[plan.ModListRep[modList]],
        JstfMaxTable max => plan.MaxAt[plan.MaxRep[max]],
        _ => throw new InvalidOperationException("A planned slot lost its model."),
    };

    // ------------------------------------------------------------------ emission

    // Only a fully preflighted plan and destination reach this path; each body
    // writes exactly once at its position (content duplicates share the position,
    // and equal content makes either instance's bytes identical).
    private static void WritePlanned(JstfTable model, Placement plan, Span<byte> destination,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(JstfView.TryWriteVersion(destination, model.Version,
            out _, location));
        CoverageTableCompiler.RequireWritten(JstfView.TryWriteScriptCount(destination,
            (ushort)plan.Records.Count, out _, location));
        for (int i = 0; i < plan.Records.Count; i++)
        {
            var record = plan.Records[i];
            CoverageTableCompiler.RequireWritten(JstfView.TryWriteScriptRecordAt(destination,
                (ulong)(JstfLayout.ScriptRecordsAt + i * JstfLayout.ScriptRecordSize),
                new JstfView.ScriptRecordData(i, record.Tag,
                    (ushort)plan.ScriptAt[plan.ScriptRep[record.Script]]), out _, location));
        }
        foreach (var script in plan.Scripts)
        {
            int scriptAt = plan.ScriptAt[script];
            var slice = destination.Slice(scriptAt);
            CoverageTableCompiler.RequireWritten(JstfScriptView.TryWriteExtenderGlyphOffset(slice,
                (ushort)(script.ExtenderGlyphs is null ? 0
                    : plan.ExtenderAt[plan.ExtenderRep[script.ExtenderGlyphs]] - scriptAt),
                out _, script.Location));
            CoverageTableCompiler.RequireWritten(JstfScriptView.TryWriteDefJstfLangSysOffset(slice,
                (ushort)(script.DefaultLangSys is null ? 0
                    : plan.LangSysAt[plan.LangSysRep[script.DefaultLangSys]] - scriptAt),
                out _, script.Location));
            CoverageTableCompiler.RequireWritten(JstfScriptView.TryWriteLangSysCount(slice,
                (ushort)script.LangSysCount, out _, script.Location));
            for (int i = 0; i < script.LangSysCount; i++)
            {
                var row = script.LangSys[i];
                CoverageTableCompiler.RequireWritten(JstfScriptView.TryWriteLangSysRecordAt(slice,
                    (ulong)(JstfScriptLayout.LangSysRecordsAt + i * JstfScriptLayout.LangSysRecordSize),
                    new JstfScriptView.LangSysRecordData(i, row.Tag,
                        (ushort)(plan.LangSysAt[plan.LangSysRep[row.LangSys]] - scriptAt)),
                    out _, script.Location));
            }
        }
        foreach (var extenders in plan.Extenders)
        {
            int at = plan.ExtenderAt[extenders];
            var slice = destination.Slice(at);
            CoverageTableCompiler.RequireWritten(JstfExtenderGlyphView.TryWriteGlyphCount(slice,
                (ushort)extenders.GlyphCount, out _, extenders.Location));
            for (int i = 0; i < extenders.GlyphCount; i++)
                CoverageTableCompiler.RequireWritten(JstfExtenderGlyphView.TryWriteExtenderGlyphAt(slice,
                    (ulong)(JstfExtenderGlyphLayout.GlyphsAt + i * JstfExtenderGlyphLayout.GlyphStride),
                    new JstfExtenderGlyphView.ExtenderGlyphData(i, extenders.Glyphs[i]),
                    out _, extenders.Location));
        }
        foreach (var langSys in plan.LangSysBodies)
        {
            int at = plan.LangSysAt[langSys];
            var slice = destination.Slice(at);
            CoverageTableCompiler.RequireWritten(JstfLangSysView.TryWritePriorityCount(slice,
                (ushort)langSys.PriorityCount, out _, langSys.Location));
            for (int i = 0; i < langSys.PriorityCount; i++)
                CoverageTableCompiler.RequireWritten(JstfLangSysView.TryWritePriorityOffsetAt(slice,
                    (ulong)(JstfLangSysLayout.OffsetsAt + i * JstfLangSysLayout.OffsetStride),
                    new JstfLangSysView.PriorityOffsetData(i,
                        (ushort)(plan.PriorityAt[plan.PriorityRep[langSys.Priorities[i]]] - at)),
                    out _, langSys.Location));
        }
        foreach (var priority in plan.Priorities)
        {
            int at = plan.PriorityAt[priority];
            var slice = destination.Slice(at);
            WriteSlot(slice, ModListEncoded(priority.GsubShrinkageEnable, at, plan),
                priority.Location, JstfPriorityView.TryWriteGsubShrinkageEnableOffset);
            WriteSlot(slice, ModListEncoded(priority.GsubShrinkageDisable, at, plan),
                priority.Location, JstfPriorityView.TryWriteGsubShrinkageDisableOffset);
            WriteSlot(slice, ModListEncoded(priority.GposShrinkageEnable, at, plan),
                priority.Location, JstfPriorityView.TryWriteGposShrinkageEnableOffset);
            WriteSlot(slice, ModListEncoded(priority.GposShrinkageDisable, at, plan),
                priority.Location, JstfPriorityView.TryWriteGposShrinkageDisableOffset);
            WriteSlot(slice, MaxEncoded(priority.ShrinkageJstfMax, at, plan),
                priority.Location, JstfPriorityView.TryWriteShrinkageJstfMaxOffset);
            WriteSlot(slice, ModListEncoded(priority.GsubExtensionEnable, at, plan),
                priority.Location, JstfPriorityView.TryWriteGsubExtensionEnableOffset);
            WriteSlot(slice, ModListEncoded(priority.GsubExtensionDisable, at, plan),
                priority.Location, JstfPriorityView.TryWriteGsubExtensionDisableOffset);
            WriteSlot(slice, ModListEncoded(priority.GposExtensionEnable, at, plan),
                priority.Location, JstfPriorityView.TryWriteGposExtensionEnableOffset);
            WriteSlot(slice, ModListEncoded(priority.GposExtensionDisable, at, plan),
                priority.Location, JstfPriorityView.TryWriteGposExtensionDisableOffset);
            WriteSlot(slice, MaxEncoded(priority.ExtensionJstfMax, at, plan),
                priority.Location, JstfPriorityView.TryWriteExtensionJstfMaxOffset);
        }
        foreach (var modList in plan.ModLists)
        {
            int at = plan.ModListAt[modList];
            var slice = destination.Slice(at);
            CoverageTableCompiler.RequireWritten(JstfModListView.TryWriteLookupCount(slice,
                (ushort)modList.LookupCount, out _, modList.Location));
            for (int i = 0; i < modList.LookupCount; i++)
                CoverageTableCompiler.RequireWritten(JstfModListView.TryWriteLookupIndexAt(slice,
                    (ulong)(JstfModListLayout.IndicesAt + i * JstfModListLayout.IndexStride),
                    new JstfModListView.LookupIndexData(i, modList.LookupIndices[i]),
                    out _, modList.Location));
        }
        foreach (var max in plan.Maxes)
        {
            int at = plan.MaxAt[max];
            var slice = destination.Slice(at);
            CoverageTableCompiler.RequireWritten(JstfMaxView.TryWriteLookupCount(slice,
                (ushort)max.LookupCount, out _, max.Location));
            for (int i = 0; i < max.LookupCount; i++)
                CoverageTableCompiler.RequireWritten(JstfMaxView.TryWriteLookupOffsetAt(slice,
                    (ulong)(JstfMaxLayout.OffsetsAt + i * JstfMaxLayout.OffsetStride),
                    new JstfMaxView.LookupOffsetData(i,
                        (ushort)(plan.LookupAt[max.Lookups[i]] - at)), out _, max.Location));
        }
        foreach (var lookup in plan.Lookups)
        {
            // The accepted GPOS lookup compiler (T4-03..06, byte-proven) emits the
            // canonical lookup body - the mechanical leaf reuse, zero new GPOS
            // emission logic.
            var status = LookupTableCompiler.TryCompile(lookup, model.Context, out var body,
                out diagnostic, budget);
            CoverageTableCompiler.RequireWritten(status);
            var compiled = body!;
            compiled.AsSpan().CopyTo(destination.Slice(plan.LookupAt[lookup], compiled.Length));
        }
    }

    private delegate ReadStatus SlotWriter(Span<byte> destination, ushort value,
        out FontDiagnostic diagnostic, DataLocation location);

    private static void WriteSlot(Span<byte> slice, ushort encoded, DataLocation location,
        SlotWriter write) =>
        CoverageTableCompiler.RequireWritten(write(slice, encoded, out _, location));

    // One priority slot's encoding: the Optional helper writes absent as zero (the
    // representative resolution happens once here, not per slot).
    private static ushort ModListEncoded(JstfModListTable? modList, int priorityAt, Placement plan) =>
        modList is null ? (ushort)0 : (ushort)(plan.ModListAt[plan.ModListRep[modList]] - priorityAt);

    private static ushort MaxEncoded(JstfMaxTable? max, int priorityAt, Placement plan) =>
        max is null ? (ushort)0 : (ushort)(plan.MaxAt[plan.MaxRep[max]] - priorityAt);
}
