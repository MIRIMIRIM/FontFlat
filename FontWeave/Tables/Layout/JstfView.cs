using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The whole-table read/validate face of the JSTF root. The version gate,
/// every declared reference bound and opener are generated; this partial adds the
/// semantics the single views cannot see: the cross-table lookup-index authority
/// (section 3.2 - the caller's explicit GSUB/GPOS LookupList counts bound the modlist
/// indices by slot kind, the T4-04 index-graph hard shape with a neighbor-table
/// domain), the extender GID domain, the duplicate hard faces (script/langsys tag
/// equality, OQ-2's narrow form; repeated priority offsets within one array, OQ-8),
/// the JstfMax inline chain (OQ-6's LookupView reuse over a handwritten opener, with
/// OQ-3's contextual exclusion: direct types 7/8 refuse, and a type-9 extension whose
/// wrapped type lands in {7,8} refuses through the declared branch probe before the
/// typed chain can accept it as legal GPOS), and the shared-identity rule (a wire
/// position materializes once and keeps model identity - the BASE R7 discipline; the
/// modlist cache keys the slot domain too, so a cross-kind shared modlist reads once
/// per authority while same-kind shares stay one instance).
///
/// numGlyphs and both lookup counts are explicit inputs with no defaults (the Avar
/// fvarAxisCount discipline; a negative count refuses at the entry - the counts are
/// required authority, not the conditional-check first-class form, because every
/// non-empty modlist consumes its domain unconditionally). Alphabetical tag order,
/// increasing extender/modlist order, design order and consumed slack are
/// observations, never refusals (OQ-2); the wire order is what the owned models
/// record. Justification application and lookup resolution stay outside this face
/// (the T0 Operations ban).</summary>
public readonly ref partial struct JstfView
{
    /// <summary>Whole-table validate face. The order observations are not reported
    /// here (no diagnostic is ever emitted for them); only the hard faces refuse.</summary>
    public ReadStatus ValidateTable(int numGlyphs, int gsubLookupCount, int gposLookupCount,
        GdefLookupContext? context, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ReadTable(numGlyphs, gsubLookupCount, gposLookupCount, context, false,
            out _, out diagnostic, budget);
    }

    /// <summary>Reads the complete table into an owned <see cref="JstfTable"/> with
    /// every script subtree materialized per budget. The materialize flag decides
    /// owned models, never which checks run, so both entries answer with identical
    /// diagnostics at identical locations.</summary>
    public ReadStatus TryReadTable(int numGlyphs, int gsubLookupCount, int gposLookupCount,
        GdefLookupContext? context, out JstfTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = ReadTable(numGlyphs, gsubLookupCount, gposLookupCount, context, true,
            out var read, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = read;
        return ReadStatus.Done;
    }

    // The one strict pass: version gate, the explicit authority inputs, then the
    // script record walk (the declaration owns the records' extent, truncation and
    // the per-item Required reference bounds).
    private ReadStatus ReadTable(int numGlyphs, int gsubLookupCount, int gposLookupCount,
        GdefLookupContext? context, bool materialize, out JstfTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The two neighbor counts are wire-less authority inputs (the Avar
        // fvarAxisCount shape): negative values refuse at the entry.
        if (gsubLookupCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.None, 0), out diagnostic,
                unchecked((ulong)(long)gsubLookupCount), 0ul);
        if (gposLookupCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.None, 0), out diagnostic,
                unchecked((ulong)(long)gposLookupCount), 0ul);
        status = TryGetScriptCount(out ushort scriptCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryRequireScriptRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetRecordsEnd(out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var walk = new Walk(numGlyphs, materialize);
        JstfScriptRecord[]? scripts = null;
        if (materialize)
        {
            if (!budget.Buffer(16L * scriptCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            scripts = new JstfScriptRecord[scriptCount];
        }
        int consumed = recordsEnd;
        for (int i = 0; i < scriptCount; i++)
        {
            status = TryGetScriptRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // OQ-2's narrow duplicate face: an adjacent tag equality refuses (the
            // fvar duplicate shape); a non-adjacent repeat or a descent is the order
            // observation and the wire order is what the model records.
            if (i > 0 && record.Tag == walk.PreviousScriptTag)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                    Location.AtLocal(DataField.Tag,
                        (ulong)(JstfLayout.ScriptRecordsAt + i * JstfLayout.ScriptRecordSize), i),
                    out diagnostic, record.Tag, 1ul);
            walk.PreviousScriptTag = record.Tag;
            // Required per-item reference: the opener owns the zero policy, the
            // RecordsEnd floor and the source bounds.
            status = TryOpenScript(i, out var scriptView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadScript(scriptView, record.Offset, numGlyphs, gsubLookupCount,
                gposLookupCount, context, walk, out var script, out int scriptLength,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (scripts is not null)
                scripts[i] = new(record.Tag, script!);
            consumed = Math.Max(consumed, record.Offset + scriptLength);
        }
        if (materialize)
        {
            if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new((uint)version, numGlyphs, gsubLookupCount, gposLookupCount, context,
                scripts!, Location, consumed);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One script subtree: both Optional slots and the langsys record walk.
    private ReadStatus ReadScript(JstfScriptView script, int position, int numGlyphs,
        int gsubLookupCount, int gposLookupCount, GdefLookupContext? context, Walk walk,
        out JstfScriptTable? model, out int length, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        length = 0;
        ReadStatus status = script.TryGetExtenderGlyphOffset(out ushort extenderOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = script.TryGetDefJstfLangSysOffset(out ushort defLangSysOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = script.TryGetLangSysCount(out ushort langSysCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = script.TryRequireLangSysRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = script.TryGetRecordsEnd(out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, script.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int end = recordsEnd;
        JstfExtenderGlyphTable? extenders = null;
        if (extenderOffset != 0)
        {
            status = script.TryOpenExtenderGlyph(out var extenderView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadExtenderGlyphs(extenderView, walk,
                out extenders, out int extenderLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, extenderOffset + extenderLength);
        }
        JstfLangSysTable? defaultLangSys = null;
        if (defLangSysOffset != 0)
        {
            status = script.TryOpenDefJstfLangSys(out var defView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadLangSys(defView, position + defLangSysOffset, numGlyphs, gsubLookupCount,
                gposLookupCount, context, walk, out defaultLangSys, out int defLength,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, defLangSysOffset + defLength);
        }
        JstfLangSysRecord[]? langSys = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16L * langSysCount, script.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            langSys = new JstfLangSysRecord[langSysCount];
        }
        for (int i = 0; i < langSysCount; i++)
        {
            status = script.TryGetLangSysRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (i > 0 && record.Tag == walk.PreviousLangSysTag)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                    script.Location.AtLocal(DataField.Tag,
                        (ulong)(JstfScriptLayout.LangSysRecordsAt + i * JstfScriptLayout.LangSysRecordSize), i),
                    out diagnostic, record.Tag, 1ul);
            walk.PreviousLangSysTag = record.Tag;
            // Required per-item reference (no spec NULL note on the entries).
            status = script.TryOpenLangSys(i, out var langSysView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadLangSys(langSysView, position + record.Offset, numGlyphs,
                gsubLookupCount, gposLookupCount, context, walk, out var langSysTable,
                out int langSysLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (langSys is not null)
                langSys[i] = new(record.Tag, langSysTable!);
            end = Math.Max(end, record.Offset + langSysLength);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, script.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(extenders, defaultLangSys, langSys!, script.Location, end);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One ExtenderGlyph list: the GID domain is the numGlyphs authority (the kern
    // OQ-4 hard half, the Coverage GID-domain shape); the increasing order is the
    // OQ-2 observation and the wire order is what the model records.
    private ReadStatus ReadExtenderGlyphs(JstfExtenderGlyphView extender, Walk walk,
        out JstfExtenderGlyphTable? model, out int length, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        length = 0;
        ReadStatus status = extender.TryGetGlyphCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = extender.TryRequireExtenderGlyphExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        int bodyEnd = JstfExtenderGlyphLayout.GlyphsAt + JstfExtenderGlyphLayout.GlyphStride * count;
        if (!budget.Work(bodyEnd, extender.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort[]? glyphs = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(2L * count, extender.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            glyphs = new ushort[count];
        }
        for (int i = 0; i < count; i++)
        {
            status = extender.TryGetExtenderGlyph(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (row.GlyphId >= walk.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    extender.Location.AtLocal(DataField.GlyphId,
                        (ulong)(JstfExtenderGlyphLayout.GlyphsAt + i * JstfExtenderGlyphLayout.GlyphStride), i),
                    out diagnostic, row.GlyphId, (ulong)walk.NumGlyphs);
            if (glyphs is not null)
                glyphs[i] = row.GlyphId;
        }
        length = bodyEnd;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, extender.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(glyphs!, extender.Location, length);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One JstfLangSys: the priority offset array in priority order, every entry a
    // Required reference (zero refuses natively) and every target shared under the
    // identity rule. A repeated offset inside the array is the OQ-8 hard face - the
    // same table twice at one priority level; sharing across levels and scripts
    // stays legal through the cache.
    private ReadStatus ReadLangSys(JstfLangSysView langSys, int position, int numGlyphs,
        int gsubLookupCount, int gposLookupCount, GdefLookupContext? context, Walk walk,
        out JstfLangSysTable? model, out int length, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        length = 0;
        if (walk.TryTakeLangSys(position, out var cached))
        {
            model = cached;
            length = cached?.ConsumedLength ?? 0;
            diagnostic = default;
            return ReadStatus.Done;
        }
        model = null;
        ReadStatus status = langSys.TryGetPriorityCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = langSys.TryRequirePriorityOffsetExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = langSys.TryGetRecordsEnd(out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, langSys.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var seenOffsets = new HashSet<ushort>();
        JstfPriorityTable[]? priorities = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(8L * count, langSys.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            priorities = new JstfPriorityTable[count];
        }
        int end = recordsEnd;
        for (int i = 0; i < count; i++)
        {
            status = langSys.TryGetPriorityOffset(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!seenOffsets.Add(row.Offset))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                    langSys.Location.AtLocal(DataField.Offset,
                        (ulong)(JstfLangSysLayout.OffsetsAt + i * JstfLangSysLayout.OffsetStride), i),
                    out diagnostic, row.Offset, 1ul);
            // Required per-item reference: the opener owns the zero policy, the
            // RecordsEnd floor and the source bounds.
            status = langSys.TryOpenPriority(i, out var priorityView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadPriority(priorityView, position + row.Offset, numGlyphs,
                gsubLookupCount, gposLookupCount, context, walk, out var priority,
                out int priorityLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (priorities is not null)
                priorities[i] = priority!;
            end = Math.Max(end, row.Offset + priorityLength);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, langSys.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(priorities!, langSys.Location, length);
        }
        return walk.PublishLangSys(position, model, langSys.Location, budget, out diagnostic);
    }

    // One JstfPriority: the ten Optional slots in wire order, dispatched by slot kind.
    // The GSUB/GPOS attribution is the slot-position matrix (section 0), so the
    // modlist index domain switches with the slot while the wire shape stays one
    // view; the two JstfMax slots carry no domain (their lookups are inline).
    private ReadStatus ReadPriority(JstfPriorityView priority, int position, int numGlyphs,
        int gsubLookupCount, int gposLookupCount, GdefLookupContext? context, Walk walk,
        out JstfPriorityTable? model, out int length, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        length = 0;
        if (!budget.Work(JstfPriorityLayout.HeaderLength, priority.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int end = JstfPriorityLayout.HeaderLength;
        ReadStatus status = priority.TryGetGsubShrinkageEnableOffset(out ushort offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfModListTable? gsubShrinkageEnable = null;
        if (offset != 0)
        {
            status = priority.TryOpenGsubShrinkageEnable(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadModList(view, position + offset, walk, JstfLookupDomain.Gsub,
                gsubLookupCount, out gsubShrinkageEnable, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        status = priority.TryGetGsubShrinkageDisableOffset(out offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfModListTable? gsubShrinkageDisable = null;
        if (offset != 0)
        {
            status = priority.TryOpenGsubShrinkageDisable(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadModList(view, position + offset, walk, JstfLookupDomain.Gsub,
                gsubLookupCount, out gsubShrinkageDisable, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        status = priority.TryGetGposShrinkageEnableOffset(out offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfModListTable? gposShrinkageEnable = null;
        if (offset != 0)
        {
            status = priority.TryOpenGposShrinkageEnable(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadModList(view, position + offset, walk, JstfLookupDomain.Gpos,
                gposLookupCount, out gposShrinkageEnable, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        status = priority.TryGetGposShrinkageDisableOffset(out offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfModListTable? gposShrinkageDisable = null;
        if (offset != 0)
        {
            status = priority.TryOpenGposShrinkageDisable(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadModList(view, position + offset, walk, JstfLookupDomain.Gpos,
                gposLookupCount, out gposShrinkageDisable, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        status = priority.TryGetShrinkageJstfMaxOffset(out offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfMaxTable? shrinkageJstfMax = null;
        if (offset != 0)
        {
            status = priority.TryOpenShrinkageJstfMax(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadMax(view, position + offset, numGlyphs, gposLookupCount, context,
                walk, out shrinkageJstfMax, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        status = priority.TryGetGsubExtensionEnableOffset(out offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfModListTable? gsubExtensionEnable = null;
        if (offset != 0)
        {
            status = priority.TryOpenGsubExtensionEnable(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadModList(view, position + offset, walk, JstfLookupDomain.Gsub,
                gsubLookupCount, out gsubExtensionEnable, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        status = priority.TryGetGsubExtensionDisableOffset(out offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfModListTable? gsubExtensionDisable = null;
        if (offset != 0)
        {
            status = priority.TryOpenGsubExtensionDisable(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadModList(view, position + offset, walk, JstfLookupDomain.Gsub,
                gsubLookupCount, out gsubExtensionDisable, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        status = priority.TryGetGposExtensionEnableOffset(out offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfModListTable? gposExtensionEnable = null;
        if (offset != 0)
        {
            status = priority.TryOpenGposExtensionEnable(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadModList(view, position + offset, walk, JstfLookupDomain.Gpos,
                gposLookupCount, out gposExtensionEnable, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        status = priority.TryGetGposExtensionDisableOffset(out offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfModListTable? gposExtensionDisable = null;
        if (offset != 0)
        {
            status = priority.TryOpenGposExtensionDisable(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadModList(view, position + offset, walk, JstfLookupDomain.Gpos,
                gposLookupCount, out gposExtensionDisable, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        status = priority.TryGetExtensionJstfMaxOffset(out offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        JstfMaxTable? extensionJstfMax = null;
        if (offset != 0)
        {
            status = priority.TryOpenExtensionJstfMax(out var view, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadMax(view, position + offset, numGlyphs, gposLookupCount, context,
                walk, out extensionJstfMax, out int slotLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, offset + slotLength);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(32, priority.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(gsubShrinkageEnable, gsubShrinkageDisable, gposShrinkageEnable,
                gposShrinkageDisable, shrinkageJstfMax, gsubExtensionEnable,
                gsubExtensionDisable, gposExtensionEnable, gposExtensionDisable,
                extensionJstfMax, priority.Location, length);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One JstfModList: the index domain is the neighbor table's LookupList count
    // selected by the slot kind (section 3.2's cross-table authority; the T4-04
    // index-graph refusal shape at the assigning element). The increasing order is
    // the OQ-2 observation; the indices are kept, never resolved.
    private ReadStatus ReadModList(JstfModListView modList, int position, Walk walk,
        JstfLookupDomain domain, int domainCount, out JstfModListTable? model,
        out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = 0;
        if (walk.TryTakeModList(position, domain, out var cached))
        {
            model = cached;
            length = cached?.ConsumedLength ?? 0;
            diagnostic = default;
            return ReadStatus.Done;
        }
        model = null;
        ReadStatus status = modList.TryGetLookupCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = modList.TryRequireLookupIndexExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        int bodyEnd = JstfModListLayout.IndicesAt + JstfModListLayout.IndexStride * count;
        if (!budget.Work(bodyEnd, modList.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort[]? indices = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(2L * count, modList.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            indices = new ushort[count];
        }
        for (int i = 0; i < count; i++)
        {
            status = modList.TryGetLookupIndex(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (row.Index >= domainCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    modList.Location.AtLocal(DataField.Payload,
                        (ulong)(JstfModListLayout.IndicesAt + i * JstfModListLayout.IndexStride), i),
                    out diagnostic, row.Index, (ulong)domainCount);
            if (indices is not null)
                indices[i] = row.Index;
        }
        length = bodyEnd;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, modList.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(domain, indices!, modList.Location, length);
        }
        return walk.PublishModList(position, domain, model, modList.Location, budget, out diagnostic);
    }

    // One JstfMax: the lookup offset array in design order (never checked), every
    // entry a required reference whose opener is the handwritten OQ-6 shape (no
    // declared reference - the targets are GPOS-shaped lookups opened as the
    // existing typed chain). Shared entries keep the lookup model identity.
    private ReadStatus ReadMax(JstfMaxView max, int position, int numGlyphs,
        int gposLookupCount, GdefLookupContext? context, Walk walk, out JstfMaxTable? model,
        out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = 0;
        if (walk.TryTakeMax(position, out var cached))
        {
            model = cached;
            length = cached?.ConsumedLength ?? 0;
            diagnostic = default;
            return ReadStatus.Done;
        }
        model = null;
        ReadStatus status = max.TryGetLookupCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = max.TryRequireLookupOffsetExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = max.TryGetRecordsEnd(out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, max.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        LookupTable[]? lookups = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(8L * count, max.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            lookups = new LookupTable[count];
        }
        int end = recordsEnd;
        for (int i = 0; i < count; i++)
        {
            status = max.TryGetLookupOffset(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // The required per-entry policy the declared reference would have owned
            // (OQ-6): zero refuses, the RecordsEnd floor and the source bounds.
            if (row.Offset == 0)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    max.Location.AtLocal(DataField.Offset,
                        (ulong)(JstfMaxLayout.OffsetsAt + i * JstfMaxLayout.OffsetStride), i),
                    out diagnostic, 0ul, (ulong)_data.Length);
            if (row.Offset < recordsEnd)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                    max.Location.AtLocal(DataField.Offset,
                        (ulong)(JstfMaxLayout.OffsetsAt + i * JstfMaxLayout.OffsetStride), i),
                    out diagnostic, row.Offset, (ulong)recordsEnd);
            if (row.Offset >= (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    max.Location.AtLocal(DataField.Offset,
                        (ulong)(JstfMaxLayout.OffsetsAt + i * JstfMaxLayout.OffsetStride), i),
                    out diagnostic, row.Offset, (ulong)_data.Length);
            status = ReadMaxLookup(position + row.Offset, numGlyphs, gposLookupCount, context,
                walk, out var lookup, out int lookupLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (lookups is not null)
                lookups[i] = lookup!;
            end = Math.Max(end, row.Offset + lookupLength);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, max.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(lookups!, max.Location, length);
        }
        return walk.PublishMax(position, model, max.Location, budget, out diagnostic);
    }

    // One inline JstfMax lookup: OQ-6's LookupView reuse (zero parallel lookup
    // declaration) plus OQ-3's contextual exclusion. The subtree presents the GPOS
    // family tag on source-bound locations so the frozen typed-chain identity
    // contracts (LookupTable.ValidateGposSource / ValidateFlags) keep their exact
    // semantics - the GPOS-shaped wire genuinely lives inside this JSTF window, and
    // the byte coordinates stay window-local (the registered OQ-T4-09-10 lean).
    private ReadStatus ReadMaxLookup(int target, int numGlyphs, int gposLookupCount,
        GdefLookupContext? context, Walk walk, out LookupTable? model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = 0;
        model = null;
        if (walk.TryTakeLookup(target, out var cached))
        {
            model = cached;
            if (cached is not null)
                length = LookupWireLength(cached);
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (!budget.Work(1, Location.AtLocal(DataField.None, (ulong)target), out diagnostic))
            return ReadStatus.ResourceLimit;
        var childLocation = GposLookupLocation(target);
        ReadStatus status = LookupView.TryOpen(_data.Slice(target), out var lookupView,
            out diagnostic, childLocation);
        if (status != ReadStatus.Done) return status;
        status = lookupView.TryGetLookupType(out ushort lookupType, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // OQ-3's exclusion face, direct form: the spec allows every GPOS lookup type
        // except the contextual pair. The refusal keeps the typed gate's shape.
        if (lookupType is 7 or 8)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                childLocation.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic,
                lookupType, 0ul);
        // OQ-3's exclusion face through the extension: the declared branch opener
        // proves each wrapped header (bounds, floor, format), then the wrapped type
        // rides the same exclusion before the typed chain can unwrap it as legal
        // GPOS (the "wrapped {7,8} also refuses" ruling).
        if (lookupType == 9)
        {
            status = lookupView.TryGetSubtableCount(out ushort subtableCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            for (int i = 0; i < subtableCount; i++)
            {
                status = lookupView.TryOpenExtensionPos(LayoutLookupKind.Gpos, i,
                    out var extension, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = extension.TryGetExtensionLookupType(out ushort wrapped, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (wrapped is 7 or 8)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                        extension.Location.AtLocal(DataField.Tag, ExtensionPosLayout.TypeAt),
                        out diagnostic, wrapped, 0ul);
            }
        }
        if (walk.Materialize)
        {
            status = lookupView.TryRead(numGlyphs, LayoutLookupKind.Gpos, context,
                gposLookupCount, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            length = LookupWireLength(model!);
        }
        else
        {
            status = lookupView.Validate(numGlyphs, LayoutLookupKind.Gpos, context,
                gposLookupCount, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        return walk.PublishLookup(target, model, childLocation, budget, out diagnostic);
    }

    // The source-side wire extent of an owned inline lookup: the count-driven header
    // (plus the conditional filter word) plus the farthest subtable end, where each
    // owned leaf's consumed length covers its closed subtree (the Coverage-inclusive
    // leaf convention). Pure accounting - the typed chain already proved every body.
    private static int LookupWireLength(LookupTable lookup)
    {
        int count = lookup.SubtableCount;
        int header = LookupLayout.HeaderLength + LookupLayout.OffsetStride * count +
            ((lookup.Flags & LookupLayout.UseMarkFilteringSet) != 0 ? 2 : 0);
        var offsets = lookup.SourceSubtableOffsets;
        int end = header;
        for (int i = 0; i < count && i < offsets.Length; i++)
        {
            int leaf = lookup.LookupType switch
            {
                2 => lookup.PairPositions is { } p ? p[i].ConsumedLength : 0,
                3 => lookup.CursiveAttachments is { } c ? c[i].ConsumedLength : 0,
                4 => lookup.MarkBases is { } b ? b[i].ConsumedLength : 0,
                5 => lookup.MarkLigs is { } l ? l[i].ConsumedLength : 0,
                6 => lookup.MarkMarks is { } m ? m[i].ConsumedLength : 0,
                7 => lookup.Contextuals is { } x ? x[i].ConsumedLength : 0,
                8 => lookup.Chainings is { } n ? n[i].ConsumedLength : 0,
                9 => lookup.ExtensionPositions is { } e ? e[i].ConsumedLength : 0,
                _ => lookup.SinglePositions is { } s ? s[i].ConsumedLength : 0,
            };
            end = Math.Max(end, offsets[i] + leaf);
        }
        return end;
    }

    // The JstfMax lookup subtree's location: the JSTF window's source identity and
    // byte coordinates under the GPOS family tag (see ReadMaxLookup's note).
    private DataLocation GposLookupLocation(int target) =>
        new(Location.SourceId, Location.FaceIndex, SinglePosLayout.Tag, -1,
            DataField.None, OffsetBasis.Table, (ulong)target);

    /// <summary>Walk state: the materialize flag, the shared-identity caches keyed by
    /// root-relative wire position (validated blocks stay visited in both modes, so
    /// a shared body is proven once; the modlist cache keys the slot domain too) and
    /// the adjacent-equality trackers for the two tag arrays (the OQ-2 narrow face;
    /// the trackers are per-array facts reset by the walk's natural nesting).</summary>
    private sealed class Walk
    {
        private readonly Dictionary<int, JstfLangSysTable?> _langSys = new();
        private readonly Dictionary<(int Position, JstfLookupDomain Domain), JstfModListTable?> _modLists = new();
        private readonly Dictionary<int, JstfMaxTable?> _max = new();
        private readonly Dictionary<int, LookupTable?> _lookups = new();

        internal Walk(int numGlyphs, bool materialize)
        {
            NumGlyphs = numGlyphs;
            Materialize = materialize;
        }

        internal int NumGlyphs { get; }
        internal bool Materialize { get; }
        internal uint PreviousScriptTag { get; set; }
        internal uint PreviousLangSysTag { get; set; }

        internal bool TryTakeLangSys(int position, out JstfLangSysTable? model) =>
            _langSys.TryGetValue(position, out model);
        internal bool TryTakeModList(int position, JstfLookupDomain domain, out JstfModListTable? model) =>
            _modLists.TryGetValue((position, domain), out model);
        internal bool TryTakeMax(int position, out JstfMaxTable? model) =>
            _max.TryGetValue(position, out model);
        internal bool TryTakeLookup(int position, out LookupTable? model) =>
            _lookups.TryGetValue(position, out model);

        // Publishing charges the model slot at the physical target exactly like the
        // BASE walk discipline: a failed admission publishes nothing, so an
        // unfinished body never owns a cache slot. Validate-mode entries store the
        // null marker - the position is proven visited either way.
        internal ReadStatus PublishLangSys(int position, JstfLangSysTable? model,
            DataLocation at, BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _langSys.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishModList(int position, JstfLookupDomain domain,
            JstfModListTable? model, DataLocation at, BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _modLists.Add((position, domain), model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishMax(int position, JstfMaxTable? model,
            DataLocation at, BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _max.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishLookup(int position, LookupTable? model,
            DataLocation at, BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _lookups.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }
    }
}
