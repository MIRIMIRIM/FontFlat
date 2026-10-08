using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables.Layout;

/// <summary>The whole-table read/validate face of BASE 1.0/1.1. The version gate, the
/// reference bounds and every opener are declared; this partial adds the semantics the
/// single views cannot see: the two index-domain hard checks (defaultBaselineIndex
/// against the owning axis's baseTagCount - narrowed away when that tag list is NULL,
/// OQ-4; fmt2 referenceGlyph against numGlyphs, OQ-5), the shared-identity rule (a
/// wire position materializes once and keeps model identity - R7's 4->1 script,
/// 7->2 script and 8->7 coord shapes), and the 0x8000 consumption chain: a fmt3
/// coord's VariationIndex device resolves (outer, inner) against THIS table's own
/// store after the declared store traversal trails both axis subtrees (OQ-T4-02b-4
/// order; a missing store slot refuses MissingTable at the referencing device field,
/// the ValidateFlags markFilteringSet shape, OQ-6; the (0xFFFF,0xFFFF) sentinel stays
/// the legal no-change observation ahead of the domain checks). Alphabetical order
/// and the baseCoordCount==baseTagCount relation are observations, never refusals
/// (OQ-2); the wire order is what the owned models record. numGlyphs and axisCount
/// are explicit inputs with no defaults; axisCount is consumed only where the store
/// slot physically exists (v1.1 with a non-NULL offset).</summary>
public readonly ref partial struct BaseView
{
    public ReadStatus ValidateTable(int numGlyphs, int axisCount, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ReadTable(numGlyphs, axisCount, false, out _, out diagnostic, budget);
    }

    /// <summary>Reads the complete table into an owned <see cref="BaseTable"/> with
    /// every axis subtree, coord leaf and (v1.1) the item variation store materialized.
    /// LONG_WORDS stores refuse through the typed VarData read via this entry
    /// (OQ-T5-01-1's surface, reused).</summary>
    public ReadStatus TryReadTable(int numGlyphs, int axisCount, out BaseTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = ReadTable(numGlyphs, axisCount, true, out var read, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = read;
        return ReadStatus.Done;
    }

    // The one strict pass over the whole declared slice; materialize decides owned
    // models, never which checks run, so both entries answer with identical
    // diagnostics at identical locations.
    private ReadStatus ReadTable(int numGlyphs, int axisCount, bool materialize,
        out BaseTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int headerEnd = version == BaseLayout.Version11 ? BaseLayout.Header11 : BaseLayout.Header10;
        uint storeOffset = 0;
        if (version == BaseLayout.Version11)
        {
            // The record reader owns the v1.1 existence domain (1.0 refuses inactive
            // before touching bytes 8..12, the A4 OQ-6 discipline).
            status = TryReadVariationStoreHeaderAt(BaseLayout.ItemVariationStoreAt, -1,
                out var storeHeader, out diagnostic);
            if (status != ReadStatus.Done) return status;
            storeOffset = storeHeader.ItemVariationStoreOffset;
        }
        if (!budget.Work(headerEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var walk = new Walk(numGlyphs, materialize);
        BaseAxisTable? horiz = null, vert = null;
        status = TryGetHorizAxisOffset(out ushort horizOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenHorizAxis(out var axisView, out bool axisPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axisPresent)
        {
            status = ReadAxis(axisView, horizOffset, walk, out horiz, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        status = TryGetVertAxisOffset(out ushort vertOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenVertAxis(out axisView, out axisPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axisPresent)
        {
            status = ReadAxis(axisView, vertOffset, walk, out vert, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        // The store traversal trails both axis references (OQ-T4-02b-4); the declared
        // opener owns the version gate, the zero policy, the HeaderEnd floor and the
        // source bounds, the store view owns its own structural domain. axisCount is
        // consumed exactly here - 1.0 and the NULL store never see it.
        VariationStoreTable? store = null;
        bool storePresent = false;
        ItemVariationStoreView storeView = default;
        if (version == BaseLayout.Version11 && storeOffset != 0)
        {
            status = TryOpenItemVariationStoreAt(BaseLayout.ItemVariationStoreAt, -1,
                out storeView, out storePresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (storePresent)
        {
            if (axisCount < 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.None, BaseLayout.ItemVariationStoreAt),
                    out diagnostic, unchecked((ulong)axisCount), 0ul);
            status = materialize
                ? storeView.TryRead(axisCount, out store, out diagnostic, budget)
                : storeView.Validate(axisCount, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        status = ResolveVariationDevices(walk, storeView, storePresent, store, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (materialize)
        {
            int consumed = headerEnd;
            if (horiz is not null)
                consumed = Math.Max(consumed, horizOffset + horiz.ConsumedLength);
            if (vert is not null)
                consumed = Math.Max(consumed, vertOffset + vert.ConsumedLength);
            if (store is not null)
                consumed = Math.Max(consumed, checked((int)storeOffset + store.ConsumedLength));
            model = new((uint)version, numGlyphs, horiz, vert, store, storeOffset, Location, consumed);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One axis subtree: the Optional tag list (its count is the defaultBaselineIndex
    // domain), the Required script list, and the shared-identity script walk below it.
    private ReadStatus ReadAxis(BaseAxisView axis, int axisPosition, Walk walk,
        out BaseAxisTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = axis.TryGetBaseTagListOffset(out ushort tagListOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = axis.TryGetBaseScriptListOffset(out ushort scriptListOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int end = BaseAxisLayout.HeaderLength;
        BaseTagListTable? tagList = null;
        int tagCount = -1;
        if (tagListOffset != 0)
        {
            status = axis.TryOpenBaseTagList(out var tagListView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int tagListAt = axisPosition + tagListOffset;
            status = ReadTagList(tagListView, tagListAt, walk, out tagList, out tagCount,
                out int tagListLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, tagListOffset + tagListLength);
        }
        // Required (OQ-1): the declared opener refuses a zero offset natively.
        status = axis.TryOpenBaseScriptList(out var scriptListView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int scriptListAt = axisPosition + scriptListOffset;
        status = ReadScriptList(scriptListView, scriptListAt, tagCount, walk, out var scripts,
            out int subtreeLength, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        end = Math.Max(end, scriptListOffset + subtreeLength);
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, axis.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(tagList, scripts!, axis.Location, end);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadTagList(BaseTagListView tagList, int position, Walk walk,
        out BaseTagListTable? model, out int tagCount, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        if (walk.TryTakeTagList(position, out var cached))
        {
            model = cached;
            tagCount = walk.TagCountAt(position);
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = tagList.TryGetBaseTagCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done)
        {
            tagCount = -1;
            return status;
        }
        tagCount = count;
        walk.RememberTagCount(position, count);
        length = BaseTagListLayout.HeaderLength + BaseTagListLayout.TagStride * count;
        status = tagList.TryRequireBaselineTagExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(length, tagList.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (walk.Materialize)
        {
            if (!budget.Buffer(4L * count, tagList.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            var tags = new uint[count];
            for (int i = 0; i < count; i++)
            {
                status = tagList.TryGetBaselineTag(i, out var tag, out diagnostic);
                if (status != ReadStatus.Done) return status;
                tags[i] = tag.Tag;
            }
            model = new(tags, tagList.Location, length);
        }
        return walk.PublishTagList(position, model, tagList.Location, budget, out diagnostic);
    }

    private ReadStatus ReadScriptList(BaseScriptListView scriptList, int position, int tagCount,
        Walk walk, out BaseScriptRecord[]? scripts, out int subtreeLength,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        scripts = null;
        subtreeLength = 0;
        ReadStatus status = scriptList.TryGetBaseScriptCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsLength = BaseScriptListLayout.HeaderLength + BaseScriptListLayout.RecordStride * count;
        status = scriptList.TryRequireBaseScriptRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(recordsLength, scriptList.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16L * count, scriptList.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            scripts = new BaseScriptRecord[count];
        }
        subtreeLength = recordsLength;
        for (int i = 0; i < count; i++)
        {
            status = scriptList.TryGetBaseScriptRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // Required per-item reference (OQ-1): the opener owns the zero policy, the
            // RecordsEnd floor and the source bounds.
            status = scriptList.TryOpenBaseScript(i, out var scriptView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int scriptAt = position + record.Offset;
            BaseScriptTable? script;
            if (walk.TryTakeScript(scriptAt, out var cached))
            {
                script = cached;
            }
            else
            {
                status = ReadScript(scriptView, scriptAt, tagCount, walk, out script,
                    out int scriptLength, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (script is not null)
                    subtreeLength = Math.Max(subtreeLength, record.Offset + scriptLength);
            }
            if (scripts is not null)
                scripts[i] = new(record.Tag, script!);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadScript(BaseScriptView script, int position, int tagCount, Walk walk,
        out BaseScriptTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        ReadStatus status = script.TryGetBaseValuesOffset(out ushort valuesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = script.TryGetDefaultMinMaxOffset(out ushort defaultMinMaxOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = script.TryGetBaseLangSysCount(out ushort langSysCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = BaseScriptLayout.HeaderLength + BaseScriptLayout.RecordStride * langSysCount;
        status = script.TryRequireBaseLangSysRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(recordsEnd, script.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int end = recordsEnd;
        BaseValuesTable? values = null;
        if (valuesOffset != 0)
        {
            status = script.TryOpenBaseValues(out var valuesView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadValues(valuesView, position + valuesOffset, tagCount, walk, out values,
                out int valuesLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (values is not null) end = Math.Max(end, valuesOffset + valuesLength);
        }
        BaseMinMaxTable? defaultMinMax = null;
        if (defaultMinMaxOffset != 0)
        {
            status = script.TryOpenDefaultMinMax(out var minMaxView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadSharedMinMax(minMaxView, position + defaultMinMaxOffset, walk,
                out defaultMinMax, out int minMaxLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (defaultMinMax is not null) end = Math.Max(end, defaultMinMaxOffset + minMaxLength);
        }
        BaseLangSysRecord[]? langSys = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16L * langSysCount, script.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            langSys = new BaseLangSysRecord[langSysCount];
        }
        for (int i = 0; i < langSysCount; i++)
        {
            status = script.TryGetBaseLangSysRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // Required per-item MinMax (no spec NULL note).
            status = script.TryOpenMinMax(i, out var minMaxView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadSharedMinMax(minMaxView, position + record.MinMaxOffset, walk,
                out var minMax, out int minMaxLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (langSys is not null)
                langSys[i] = new(record.Tag, minMax!);
            if (minMax is not null) end = Math.Max(end, record.MinMaxOffset + minMaxLength);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, script.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(values, defaultMinMax, langSys!, script.Location, end);
        }
        return walk.PublishScript(position, model, script.Location, budget, out diagnostic);
    }

    private ReadStatus ReadValues(BaseValuesView values, int position, int tagCount, Walk walk,
        out BaseValuesTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        if (walk.TryTakeValues(position, out var cached))
        {
            model = cached;
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = values.TryGetDefaultBaselineIndex(out ushort defaultIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = values.TryGetBaseCoordCount(out ushort coordCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int coordsEnd = BaseValuesLayout.HeaderLength + BaseValuesLayout.CoordStride * coordCount;
        status = values.TryRequireBaseCoordOffsetExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(coordsEnd, values.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // The one index-domain hard check on this level (OQ-2's split): the default
        // index names the owning axis's tag list. A NULL tag list on that axis
        // narrows the check away (OQ-4; the count context is absent, not zero).
        if (tagCount >= 0 && defaultIndex >= tagCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                values.Location.AtLocal(DataField.Payload, BaseValuesLayout.DefaultBaselineIndexAt),
                out diagnostic, defaultIndex, unchecked((ulong)tagCount));
        int end = coordsEnd;
        BaseCoordTable[]? coords = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(8L * coordCount, values.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            coords = new BaseCoordTable[coordCount];
        }
        for (int i = 0; i < coordCount; i++)
        {
            status = values.TryGetBaseCoordOffset(i, out var offset, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // Required per-item reference: the opener owns the zero policy (no spec
            // NULL note - the native zero-entry refusal), the CoordsEnd floor and the
            // source bounds.
            status = values.TryOpenCoord(i, out var coordView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int coordAt = position + offset.Offset;
            status = ReadSharedCoord(coordView, coordAt, walk, out var coord,
                out int coordLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (coords is not null)
                coords[i] = coord!;
            if (coord is not null) end = Math.Max(end, offset.Offset + coordLength);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, values.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(defaultIndex, coords!, values.Location, end);
        }
        return walk.PublishValues(position, model, values.Location, budget, out diagnostic);
    }

    private ReadStatus ReadMinMax(BaseMinMaxView minMax, int position, Walk walk,
        out BaseMinMaxTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        ReadStatus status = minMax.TryGetMinCoordOffset(out ushort minOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = minMax.TryGetMaxCoordOffset(out ushort maxOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = minMax.TryGetBaseFeatMinMaxCount(out ushort featCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = BaseMinMaxLayout.HeaderLength + BaseMinMaxLayout.RecordStride * featCount;
        status = minMax.TryRequireFeatMinMaxRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(recordsEnd, minMax.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int end = recordsEnd;
        BaseCoordTable? minCoord = null;
        if (minOffset != 0)
        {
            status = minMax.TryOpenMinCoord(out var coordView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadSharedCoord(coordView, position + minOffset, walk, out minCoord,
                out int coordLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (minCoord is not null) end = Math.Max(end, minOffset + coordLength);
        }
        BaseCoordTable? maxCoord = null;
        if (maxOffset != 0)
        {
            status = minMax.TryOpenMaxCoord(out var maxView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadSharedCoord(maxView, position + maxOffset, walk, out maxCoord,
                out int coordLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (maxCoord is not null) end = Math.Max(end, maxOffset + coordLength);
        }
        FeatMinMaxRecord[]? rows = null;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16L * featCount, minMax.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            rows = new FeatMinMaxRecord[featCount];
        }
        for (int i = 0; i < featCount; i++)
        {
            status = minMax.TryGetFeatMinMaxRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            BaseCoordTable? featMin = null, featMax = null;
            if (record.MinCoordOffset != 0)
            {
                status = minMax.TryOpenFeatMinCoord(i, out var coordView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ReadSharedCoord(coordView, position + record.MinCoordOffset, walk,
                    out featMin, out int coordLength, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (featMin is not null) end = Math.Max(end, record.MinCoordOffset + coordLength);
            }
            if (record.MaxCoordOffset != 0)
            {
                status = minMax.TryOpenFeatMaxCoord(i, out var coordView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ReadSharedCoord(coordView, position + record.MaxCoordOffset, walk,
                    out featMax, out int coordLength, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (featMax is not null) end = Math.Max(end, record.MaxCoordOffset + coordLength);
            }
            if (rows is not null)
                rows[i] = new(record.FeatureTag, featMin, featMax);
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, minMax.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(minCoord, maxCoord, rows!, minMax.Location, end);
        }
        return walk.PublishMinMax(position, model, minMax.Location, budget, out diagnostic);
    }

    // MinMax-side coord and MinMax reads share the values-side caches (a coord or
    // MinMax referenced from several owners is one model), so these wrappers only
    // fall through to the fresh read on a cache miss.
    private ReadStatus ReadSharedMinMax(BaseMinMaxView minMax, int position, Walk walk,
        out BaseMinMaxTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (walk.TryTakeMinMax(position, out var cached))
        {
            model = cached;
            length = cached?.ConsumedLength ?? 0;
            diagnostic = default;
            return ReadStatus.Done;
        }
        return ReadMinMax(minMax, position, walk, out model, out length, out diagnostic, budget);
    }

    private ReadStatus ReadSharedCoord(BaseCoordView coord, int position, Walk walk,
        out BaseCoordTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (walk.TryTakeCoord(position, out var cached))
        {
            model = cached;
            length = cached?.ConsumedLength ?? 0;
            diagnostic = default;
            return ReadStatus.Done;
        }
        return ReadCoord(coord, position, walk, out model, out length, out diagnostic, budget);
    }

    private ReadStatus ReadCoord(BaseCoordView coord, int position, Walk walk,
        out BaseCoordTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        length = 0;
        ReadStatus status = coord.TryRequireKnownFormat(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = coord.TryGetBaseCoordFormat(out ushort format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = coord.TryGetCoordinate(out short coordinate, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ushort referenceGlyph = 0, baseCoordPoint = 0;
        int ownLength = BaseCoordLayout.MinimumHeader;
        if (format == 2)
        {
            ownLength += 4;
            status = coord.TryGetFormat2Header(out var format2, out diagnostic);
            if (status != ReadStatus.Done) return status;
            referenceGlyph = format2.ReferenceGlyph;
            baseCoordPoint = format2.BaseCoordPoint;
            // The second index-domain hard check (OQ-5): fmt2's referenceGlyph is a
            // glyph ID, so the numGlyphs context bounds it (the Coverage GID-domain
            // face; Anchor fmt2's contour point has no such domain and stays opaque).
            if (referenceGlyph >= walk.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    coord.Location.AtLocal(DataField.Payload, BaseCoordLayout.Format2HeaderField),
                    out diagnostic, referenceGlyph, (ulong)walk.NumGlyphs);
        }
        int end = ownLength;
        StaticDeviceTable? device = null;
        if (format == 3)
        {
            ownLength = BaseCoordLayout.Format3Length;
            end = ownLength;
            status = coord.TryGetFormat3Header(out var format3, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (format3.DeviceOffset != 0)
            {
                // Optional/ZeroAllowed: offset 0 is the present-null, resolved absent
                // (Anchor fmt3's W6 shape). A non-zero offset opens under the BodyEnd
                // floor and the source bounds.
                status = coord.TryOpenDeviceAt(BaseCoordLayout.Format3HeaderField, -1,
                    out var deviceView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                var deviceAt = coord.Location.AtLocal(DataField.Offset, BaseCoordLayout.Format3HeaderField);
                if (walk.Materialize)
                {
                    status = deviceView.TryRead(out device, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    end = Math.Max(end, format3.DeviceOffset + device!.ConsumedLength);
                    if (device!.IsVariationDevice)
                    {
                        status = walk.EnqueueVariationDevice(deviceAt, device.Outer, device.Inner,
                            budget, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                    }
                }
                else
                {
                    status = deviceView.Inspect(out _, out int deviceLength, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    end = Math.Max(end, format3.DeviceOffset + deviceLength);
                    status = deviceView.TryGetDeltaFormat(out ushort deltaFormat, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (deltaFormat == 0x8000)
                    {
                        status = deviceView.TryGetStartSize(out ushort outer, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        status = deviceView.TryGetEndSize(out ushort inner, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        status = walk.EnqueueVariationDevice(deviceAt, outer, inner, budget, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                    }
                }
            }
        }
        length = end;
        if (walk.Materialize)
        {
            if (!budget.Buffer(16, coord.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(format, coordinate, referenceGlyph, baseCoordPoint, device,
                coord.Location, end);
        }
        return walk.PublishCoord(position, model, coord.Location, budget, out diagnostic);
    }

    // The 0x8000 consumption chain's tail: the deferred (outer, inner) references
    // resolve after the store traversal. Presence first (MissingTable at the
    // referencing device field, the ValidateFlags shape), then the sentinel (the
    // legal no-change observation, T4-06 OQ-6's surface), then the store domains.
    private static ReadStatus ResolveVariationDevices(Walk walk, ItemVariationStoreView storeView,
        bool storePresent, VariationStoreTable? store, out FontDiagnostic diagnostic)
    {
        foreach (var reference in walk.VariationDevices)
        {
            if (!storePresent)
                return ReadWire.Fail(ReadStatus.MissingTable, DiagnosticCode.TableNotFound,
                    reference.DeviceAt, out diagnostic);
            if (reference.Outer == 0xFFFF && reference.Inner == 0xFFFF) continue;
            ReadStatus status;
            if (store is not null)
            {
                status = store.TryGetDeltaSet(reference.Outer, reference.Inner,
                    out _, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                continue;
            }
            status = storeView.TryGetItemVariationDataCount(out ushort dataCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (reference.Outer >= dataCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    storeView.Location.AtLocal(DataField.Count, ItemVariationStoreLayout.DataCountAt),
                    out diagnostic, reference.Outer, dataCount);
            status = storeView.TryOpenItemVariationData((int)reference.Outer, out var varDataView,
                out bool varDataPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!varDataPresent) continue; // no variation data for this outer: defined no-change
            status = varDataView.TryGetItemCount(out ushort itemCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (reference.Inner >= itemCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    varDataView.Location.AtLocal(DataField.Count, ItemVariationDataLayout.ItemCountAt),
                    out diagnostic, reference.Inner, itemCount);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Walk state: the materialize flag, the shared-identity caches keyed by
    /// root-relative wire position (validated blocks stay visited in both modes, so a
    /// shared body is proven once), the per-axis tag-list counts (the
    /// defaultBaselineIndex domain, recorded at the tag list's own position so a
    /// cross-axis shared list keeps its context), and the deferred 0x8000 references.</summary>
    private sealed class Walk
    {
        private readonly Dictionary<int, BaseTagListTable?> _tagLists = new();
        private readonly Dictionary<int, int> _tagListCounts = new();
        private readonly Dictionary<int, BaseScriptTable?> _scripts = new();
        private readonly Dictionary<int, BaseValuesTable?> _values = new();
        private readonly Dictionary<int, BaseMinMaxTable?> _minMax = new();
        private readonly Dictionary<int, BaseCoordTable?> _coords = new();

        internal Walk(int numGlyphs, bool materialize)
        {
            NumGlyphs = numGlyphs;
            Materialize = materialize;
        }

        internal int NumGlyphs { get; }
        internal bool Materialize { get; }
        internal List<VariationDeviceReference> VariationDevices { get; } = new();

        internal bool TryTakeTagList(int position, out BaseTagListTable? model) =>
            _tagLists.TryGetValue(position, out model);
        internal bool TryTakeScript(int position, out BaseScriptTable? model) =>
            _scripts.TryGetValue(position, out model);
        internal bool TryTakeValues(int position, out BaseValuesTable? model) =>
            _values.TryGetValue(position, out model);
        internal bool TryTakeMinMax(int position, out BaseMinMaxTable? model) =>
            _minMax.TryGetValue(position, out model);
        internal bool TryTakeCoord(int position, out BaseCoordTable? model) =>
            _coords.TryGetValue(position, out model);

        // The defaultBaselineIndex domain of the axis a values table belongs to: the
        // owning tag list's count, or -1 when that list is NULL (OQ-4's narrowing).
        // The context is bound to the tag list position, not the axis slot, so a
        // cross-axis shared list cannot bind two different counts (the LayoutReadState
        // context-binding discipline in miniature).
        internal void RememberTagCount(int position, int count) => _tagListCounts.Add(position, count);
        internal int TagCountAt(int tagListPosition) =>
            _tagListCounts.TryGetValue(tagListPosition, out int count) ? count : -1;

        // Publishing charges the facts slot at the physical target exactly like the
        // LayoutReadStore discipline: a failed admission publishes nothing, so an
        // unfinished body never owns a cache slot. Validate-mode entries store the
        // null marker - the position is proven visited either way.
        internal ReadStatus PublishTagList(int position, BaseTagListTable? model, DataLocation at,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _tagLists.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishScript(int position, BaseScriptTable? model, DataLocation at,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _scripts.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishValues(int position, BaseValuesTable? model, DataLocation at,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _values.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishMinMax(int position, BaseMinMaxTable? model, DataLocation at,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _minMax.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus PublishCoord(int position, BaseCoordTable? model, DataLocation at,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, at, out diagnostic)) return ReadStatus.ResourceLimit;
            _coords.Add(position, model);
            diagnostic = default;
            return ReadStatus.Done;
        }

        internal ReadStatus EnqueueVariationDevice(DataLocation deviceAt, ushort outer, ushort inner,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (!budget.Buffer(16, deviceAt, out diagnostic)) return ReadStatus.ResourceLimit;
            VariationDevices.Add(new(deviceAt, outer, inner));
            diagnostic = default;
            return ReadStatus.Done;
        }
    }

    private readonly record struct VariationDeviceReference(DataLocation DeviceAt, ushort Outer, ushort Inner);
}
