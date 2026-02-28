using OTFontFile2.Tables;

namespace OTFontFile2;

/// <summary>
/// Mutable font model for fonttools-style "edit tables then write back".
/// </summary>
public sealed class FontModel
{
    private static readonly TableDependencyGraph s_defaultDependencies = TableDependencyGraph.CreateDefault();
    private static readonly TableBuilderCodecRegistry s_defaultCodecs = TableBuilderCodecRegistry.CreateDefault();

    private readonly SfntFont? _baseFont;
    private readonly Dictionary<Tag, ISfntTableSource> _overrides = new();
    private readonly HashSet<Tag> _removed = new();
    private readonly List<IFontModelFixup> _fixups = new();

    public FontModel(SfntFont baseFont)
    {
        _baseFont = baseFont;
        SfntVersion = baseFont.SfntVersion;
        Dependencies = s_defaultDependencies.Clone();
        Codecs = s_defaultCodecs.Clone();

        _fixups.Add(new FixGlyfDerivedTablesFixup());
        _fixups.Add(new FixMaxpFromGlyfFixup());
        _fixups.Add(new FixMetricsForGlyphSetChangeFixup());
        _fixups.Add(new FixCblcDerivedTablesFixup());
        _fixups.Add(new FixEblcDerivedTablesFixup());

        // Built-in fixups are validations only for now; rebuild fixups will be added as table builders land.
        _fixups.Add(new ValidateMaxpDependentTablesFixup());
        _fixups.Add(new ValidateVariationTablesFixup());
        _fixups.Add(new FixMetricsTablesFixup());
    }

    public FontModel(uint sfntVersion)
    {
        SfntVersion = sfntVersion;
        Dependencies = s_defaultDependencies.Clone();
        Codecs = s_defaultCodecs.Clone();
        _fixups.Add(new FixGlyfDerivedTablesFixup());
        _fixups.Add(new FixMaxpFromGlyfFixup());
        _fixups.Add(new FixMetricsForGlyphSetChangeFixup());
        _fixups.Add(new FixCblcDerivedTablesFixup());
        _fixups.Add(new FixEblcDerivedTablesFixup());
        _fixups.Add(new ValidateMaxpDependentTablesFixup());
        _fixups.Add(new ValidateVariationTablesFixup());
        _fixups.Add(new FixMetricsTablesFixup());
    }

    public uint SfntVersion { get; set; }

    public bool AllowStaleTables { get; set; }

    public TableDependencyGraph Dependencies { get; }

    public TableBuilderCodecRegistry Codecs { get; }

    public bool HasBaseFont => _baseFont is not null;

    public SfntFont BaseFont
        => _baseFont ?? throw new InvalidOperationException("FontModel has no base font.");

    public int OverrideCount => _overrides.Count;

    public bool IsRemoved(Tag tag) => _removed.Contains(tag);

    public void AddFixup(IFontModelFixup fixup)
    {
        if (fixup is null) throw new ArgumentNullException(nameof(fixup));
        _fixups.Add(fixup);
    }

    public void SetTable(ISfntTableSource table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));

        _removed.Remove(table.Tag);
        _overrides[table.Tag] = table;
    }

    public void SetTable(Tag tag, ReadOnlyMemory<byte> data)
        => SetTable(new MemoryTableSource(tag, data));

    public bool RemoveTable(Tag tag)
    {
        _overrides.Remove(tag);
        return _removed.Add(tag);
    }

    public bool TryGetOverride(Tag tag, out ISfntTableSource table)
        => _overrides.TryGetValue(tag, out table!);

    public bool HasTable(Tag tag)
    {
        if (_removed.Contains(tag))
            return false;

        if (_overrides.ContainsKey(tag))
            return true;

        return _baseFont.HasValue && _baseFont.Value.TryGetTableSlice(tag, out _);
    }

    public void WriteTo(Stream destination, SfntWriteOptions? options = null)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));

        ApplyFixups();
        ValidateNoStaleDependencies();

        if (_baseFont.HasValue)
        {
            var editor = new SfntEditor(_baseFont.Value);
            foreach (var table in _overrides.Values)
                editor.SetTable(table);
            foreach (var tag in _removed)
                editor.RemoveTable(tag);

            editor.WriteTo(destination, options);
            return;
        }

        SfntWriter.Write(destination, SfntVersion, EnumerateNewFontTables(), options);
    }

    public byte[] ToArray(SfntWriteOptions? options = null)
    {
        using var ms = new MemoryStream();
        WriteTo(ms, options);
        return ms.ToArray();
    }

    private IEnumerable<ISfntTableSource> EnumerateNewFontTables()
    {
        foreach (var (tag, table) in _overrides)
        {
            if (_removed.Contains(tag))
                continue;

            yield return table;
        }
    }

    private void ApplyFixups()
    {
        for (int i = 0; i < _fixups.Count; i++)
            _fixups[i].Apply(this);
    }

    private void ValidateNoStaleDependencies()
    {
        if (AllowStaleTables)
            return;

        if (!_baseFont.HasValue)
            return;

        if (_overrides.Count == 0 && _removed.Count == 0)
            return;

        var changed = new HashSet<Tag>(_overrides.Keys);
        foreach (var tag in _removed)
            changed.Add(tag);

        var stale = new HashSet<Tag>();
        foreach (var changedTag in changed)
        {
            foreach (var dependent in Dependencies.GetTransitiveDependents(changedTag, TableDependencyKind.RebuildRequired))
            {
                if (_removed.Contains(dependent))
                    continue;
                if (_overrides.ContainsKey(dependent))
                    continue;

                if (_baseFont.Value.TryGetTableSlice(dependent, out _))
                    stale.Add(dependent);
            }
        }

        if (stale.Count == 0)
            return;

        var names = new List<string>(stale.Count);
        foreach (var tag in stale)
            names.Add(tag.ToString());
        names.Sort(StringComparer.Ordinal);
        string list = string.Join(", ", names);
        throw new InvalidOperationException($"Cannot write font: stale dependent tables present ({list}). Override or remove them, or set AllowStaleTables=true.");
    }

    public bool TryEdit<TBuilder>(out TBuilder builder)
        where TBuilder : class, ISfntTableSource
    {
        builder = null!;

        if (!Codecs.TryGet<TBuilder>(out var codec))
            return false;

        Tag tag = codec.Tag;
        if (_removed.Contains(tag))
            return false;

        if (_overrides.TryGetValue(tag, out var existing))
        {
            if (existing is TBuilder typed)
            {
                builder = typed;
                return true;
            }

            return false;
        }

        if (!codec.TryCreateBuilder(this, out var created))
            return false;

        SetTable(created);
        builder = (TBuilder)created;
        return true;
    }

    internal bool TryGetMaxpNumGlyphs(out ushort numGlyphs)
    {
        numGlyphs = 0;

        if (_removed.Contains(KnownTags.maxp))
            return false;

        if (_overrides.TryGetValue(KnownTags.maxp, out var maxpSource) && maxpSource is MaxpTableBuilder maxpBuilder)
        {
            numGlyphs = maxpBuilder.NumGlyphs;
            return true;
        }

        if (_baseFont.HasValue && _baseFont.Value.TryGetMaxp(out var maxp))
        {
            numGlyphs = maxp.NumGlyphs;
            return true;
        }

        return false;
    }

    internal bool TryGetHheaNumberOfHMetrics(out ushort numberOfHMetrics)
    {
        numberOfHMetrics = 0;

        if (_removed.Contains(KnownTags.hhea))
            return false;

        if (_overrides.TryGetValue(KnownTags.hhea, out var hheaSource) && hheaSource is HheaTableBuilder hheaBuilder)
        {
            numberOfHMetrics = hheaBuilder.NumberOfHMetrics;
            return true;
        }

        if (_baseFont.HasValue && _baseFont.Value.TryGetHhea(out var hhea))
        {
            numberOfHMetrics = hhea.NumberOfHMetrics;
            return true;
        }

        return false;
    }

    internal bool TryGetVheaNumOfLongVerMetrics(out ushort numOfLongVerMetrics)
    {
        numOfLongVerMetrics = 0;

        if (_removed.Contains(KnownTags.vhea))
            return false;

        if (_overrides.TryGetValue(KnownTags.vhea, out var vheaSource) && vheaSource is VheaTableBuilder vheaBuilder)
        {
            numOfLongVerMetrics = vheaBuilder.NumOfLongVerMetrics;
            return true;
        }

        if (_baseFont.HasValue && _baseFont.Value.TryGetVhea(out var vhea))
        {
            numOfLongVerMetrics = vhea.NumOfLongVerMetrics;
            return true;
        }

        return false;
    }

    private sealed class ValidateMaxpDependentTablesFixup : IFontModelFixup
    {
        public void Apply(FontModel model)
        {
            if (!model.TryGetMaxpNumGlyphs(out ushort numGlyphs))
                return;

            if (model._overrides.TryGetValue(KnownTags.hdmx, out var hdmxSource) && hdmxSource is HdmxTableBuilder hdmxBuilder)
            {
                if (hdmxBuilder.NumGlyphs != numGlyphs)
                {
                    throw new InvalidOperationException($"hdmx.NumGlyphs ({hdmxBuilder.NumGlyphs}) must match maxp.NumGlyphs ({numGlyphs}).");
                }
            }

            if (model._overrides.TryGetValue(KnownTags.LTSH, out var ltshSource) && ltshSource is LtshTableBuilder ltshBuilder)
            {
                if (ltshBuilder.NumGlyphs != numGlyphs)
                {
                    throw new InvalidOperationException($"LTSH.NumGlyphs ({ltshBuilder.NumGlyphs}) must match maxp.NumGlyphs ({numGlyphs}).");
                }
            }

            if (model._overrides.TryGetValue(KnownTags.sbix, out var sbixSource) && sbixSource is SbixTableBuilder sbixBuilder)
            {
                if (!sbixBuilder.IsRaw && sbixBuilder.StrikeCount != 0)
                {
                    if (!sbixBuilder.HasNumGlyphs)
                        throw new InvalidOperationException("sbix.NumGlyphs must be set (from maxp) when writing strikes.");

                    if (sbixBuilder.NumGlyphs != numGlyphs)
                        throw new InvalidOperationException($"sbix.NumGlyphs ({sbixBuilder.NumGlyphs}) must match maxp.NumGlyphs ({numGlyphs}).");
                }
            }

            if (model._overrides.TryGetValue(KnownTags.post, out var postSource) && postSource is PostTableBuilder postBuilder)
            {
                if (postBuilder.IsVersion2 && postBuilder.NumberOfGlyphs != numGlyphs)
                {
                    throw new InvalidOperationException($"post.numberOfGlyphs ({postBuilder.NumberOfGlyphs}) must match maxp.NumGlyphs ({numGlyphs}) for post version 2.0.");
                }
            }

            if (model._overrides.TryGetValue(KnownTags.cmap, out var cmapSource) && cmapSource is CmapTableBuilder cmapBuilder)
            {
                if (cmapBuilder.TryFindGlyphIdAtOrAbove(numGlyphs, out uint codePoint, out ushort glyphId, out uint variationSelector))
                {
                    if (variationSelector != 0)
                        throw new InvalidOperationException($"cmap maps U+{codePoint:X} (VS U+{variationSelector:X}) to glyph {glyphId}, which is outside maxp.NumGlyphs ({numGlyphs}).");

                    throw new InvalidOperationException($"cmap maps U+{codePoint:X} to glyph {glyphId}, which is outside maxp.NumGlyphs ({numGlyphs}).");
                }
            }
        }
    }

    private sealed class FixCblcDerivedTablesFixup : IFontModelFixup
    {
        public void Apply(FontModel model)
        {
            if (model._removed.Contains(KnownTags.CBLC))
                return;

            if (!model._overrides.TryGetValue(KnownTags.CBLC, out var cblcSource) || cblcSource is not CblcTableBuilder cblc)
                return;

            if (cblc.IsRaw || !cblc.IsStructured)
                return;

            if (model._removed.Contains(KnownTags.CBDT))
                throw new InvalidOperationException("Cannot write font: CBLC is overridden but CBDT is removed.");

            if (model._overrides.ContainsKey(KnownTags.CBDT))
                throw new InvalidOperationException("Cannot write font: CBLC structured editing owns CBDT writeback; remove the CBDT override or put CBLC in raw mode.");

            if (!cblc.TryBuildDerivedCbdt(out var cbdt))
                throw new InvalidOperationException("Cannot write font: unable to build derived CBDT from CBLC structured builder.");

            model.SetTable(cbdt);
        }
    }

    private sealed class FixEblcDerivedTablesFixup : IFontModelFixup
    {
        public void Apply(FontModel model)
        {
            if (model._removed.Contains(KnownTags.EBLC))
                return;

            if (!model._overrides.TryGetValue(KnownTags.EBLC, out var eblcSource) || eblcSource is not EblcTableBuilder eblc)
                return;

            if (eblc.IsRaw || !eblc.IsStructured)
                return;

            if (model._removed.Contains(KnownTags.EBDT))
                throw new InvalidOperationException("Cannot write font: EBLC is overridden but EBDT is removed.");

            if (model._overrides.ContainsKey(KnownTags.EBDT))
                throw new InvalidOperationException("Cannot write font: EBLC structured editing owns EBDT writeback; remove the EBDT override or put EBLC in raw mode.");

            if (!eblc.TryBuildDerivedEbdt(out var ebdt))
                throw new InvalidOperationException("Cannot write font: unable to build derived EBDT from EBLC structured builder.");

            model.SetTable(ebdt);
        }
    }

    private sealed class FixMetricsTablesFixup : IFontModelFixup
    {
        public void Apply(FontModel model)
        {
            FixHorizontal(model);
            FixVertical(model);
        }

        private static void FixHorizontal(FontModel model)
        {
            if (!model._overrides.TryGetValue(KnownTags.hmtx, out var hmtxSource) || hmtxSource is not HmtxTableBuilder hmtxBuilder)
                return;

            if (!model.TryGetMaxpNumGlyphs(out ushort numGlyphs))
                throw new InvalidOperationException("Cannot write font: missing maxp table required to validate hmtx.");

            if (hmtxBuilder.NumGlyphs != numGlyphs)
                throw new InvalidOperationException($"hmtx glyph count ({hmtxBuilder.NumGlyphs}) must match maxp.NumGlyphs ({numGlyphs}).");

            if (!model.TryGetHheaNumberOfHMetrics(out ushort numberOfHMetrics))
                throw new InvalidOperationException("Cannot write font: missing hhea table required to interpret hmtx.");

            if (numberOfHMetrics == hmtxBuilder.NumberOfHMetrics)
                return;

            if (!model.TryEdit<HheaTableBuilder>(out var hheaBuilder))
                throw new InvalidOperationException("Cannot write font: unable to create hhea builder to match hmtx.");

            hheaBuilder.NumberOfHMetrics = hmtxBuilder.NumberOfHMetrics;
        }

        private static void FixVertical(FontModel model)
        {
            if (!model._overrides.TryGetValue(KnownTags.vmtx, out var vmtxSource) || vmtxSource is not VmtxTableBuilder vmtxBuilder)
                return;

            if (!model.TryGetMaxpNumGlyphs(out ushort numGlyphs))
                throw new InvalidOperationException("Cannot write font: missing maxp table required to validate vmtx.");

            if (vmtxBuilder.NumGlyphs != numGlyphs)
                throw new InvalidOperationException($"vmtx glyph count ({vmtxBuilder.NumGlyphs}) must match maxp.NumGlyphs ({numGlyphs}).");

            if (!model.TryGetVheaNumOfLongVerMetrics(out ushort numOfLongVerMetrics))
                throw new InvalidOperationException("Cannot write font: missing vhea table required to interpret vmtx.");

            if (numOfLongVerMetrics == vmtxBuilder.NumOfLongVerMetrics)
                return;

            if (!model.TryEdit<VheaTableBuilder>(out var vheaBuilder))
                throw new InvalidOperationException("Cannot write font: unable to create vhea builder to match vmtx.");

            vheaBuilder.NumOfLongVerMetrics = vmtxBuilder.NumOfLongVerMetrics;
        }
    }

    private sealed class FixGlyfDerivedTablesFixup : IFontModelFixup
    {
        public void Apply(FontModel model)
        {
            if (model._removed.Contains(KnownTags.glyf))
                return;

            if (!model._overrides.TryGetValue(KnownTags.glyf, out var glyfSource) || glyfSource is not GlyfTableBuilder glyfBuilder)
                return;

            if (!glyfBuilder.IsLinkedBaseFont)
                return;

            // Ensure loca is overridden when glyf is overridden, to avoid stale dependency failures.
            if (!model._removed.Contains(KnownTags.loca) && !model._overrides.ContainsKey(KnownTags.loca))
            {
                model.SetTable(new LocaTableBuilder(glyfBuilder));
            }

            // Ensure head.indexToLocFormat matches the loca format implied by the current glyf layout.
            if (model._removed.Contains(KnownTags.head))
                return;

            short required = glyfBuilder.RequiredIndexToLocFormat;

            if (model._overrides.TryGetValue(KnownTags.head, out var headSource) && headSource is HeadTableBuilder headBuilder)
            {
                headBuilder.IndexToLocFormat = required;
                return;
            }

            if (model._baseFont.HasValue && model._baseFont.Value.TryGetHead(out var baseHead))
            {
                if (baseHead.IndexToLocFormat == required)
                    return;

                if (model.TryEdit<HeadTableBuilder>(out var builder))
                    builder.IndexToLocFormat = required;
            }
        }
    }

    private sealed class FixMaxpFromGlyfFixup : IFontModelFixup
    {
        public void Apply(FontModel model)
        {
            if (model._removed.Contains(KnownTags.glyf))
                return;

            if (!model._overrides.TryGetValue(KnownTags.glyf, out var glyfSource) || glyfSource is not GlyfTableBuilder glyfBuilder)
                return;

            if (!glyfBuilder.IsLinkedBaseFont || !glyfBuilder.HasGlyphOverrides)
                return;

            if (model._removed.Contains(KnownTags.maxp))
                return;

            if (!model.TryEdit<MaxpTableBuilder>(out var maxpBuilder))
                return;

            if (!maxpBuilder.IsTrueTypeMaxp)
                return;

            if (!MaxpRecalculator.TryRecalculateFromGlyf(glyfBuilder, out var f))
                throw new InvalidOperationException("Cannot write font: unable to recalculate maxp derived fields from glyf.");

            maxpBuilder.MaxPoints = f.MaxPoints;
            maxpBuilder.MaxContours = f.MaxContours;
            maxpBuilder.MaxCompositePoints = f.MaxCompositePoints;
            maxpBuilder.MaxCompositeContours = f.MaxCompositeContours;
            maxpBuilder.MaxSizeOfInstructions = f.MaxSizeOfInstructions;
            maxpBuilder.MaxComponentElements = f.MaxComponentElements;
            maxpBuilder.MaxComponentDepth = f.MaxComponentDepth;
        }
    }

    private sealed class FixMetricsForGlyphSetChangeFixup : IFontModelFixup
    {
        public void Apply(FontModel model)
        {
            if (!model._overrides.TryGetValue(KnownTags.maxp, out var maxpSource) || maxpSource is not MaxpTableBuilder maxp)
                return;

            ushort numGlyphs = maxp.NumGlyphs;
            if (numGlyphs == 0)
                throw new InvalidOperationException("Cannot write font: maxp.NumGlyphs must be >= 1.");

            FixHorizontalMetrics(model, numGlyphs);
            FixVerticalMetrics(model, numGlyphs);
            FixOs2AvgCharWidth(model, numGlyphs);
        }

        private static void FixHorizontalMetrics(FontModel model, ushort numGlyphs)
        {
            if (model._removed.Contains(KnownTags.hmtx) || model._removed.Contains(KnownTags.hhea))
                return;

            if (model._overrides.TryGetValue(KnownTags.hmtx, out var hmtxSource) && hmtxSource is HmtxTableBuilder existing)
            {
                if (existing.NumGlyphs != numGlyphs)
                    model.SetTable(ResizeHmtx(existing, numGlyphs));
                return;
            }

            // No override; if base glyph count changed, rebuild hmtx from base.
            if (!model._baseFont.HasValue)
                return;

            if (!model._baseFont.Value.TryGetMaxp(out var baseMaxp))
                return;

            if (baseMaxp.NumGlyphs == numGlyphs)
                return;

            var baseFont = model._baseFont.Value;
            if (!baseFont.TryGetHhea(out var hhea) || !baseFont.TryGetHmtx(out var hmtx))
                return;

            model.SetTable(RebuildHmtxFromBase(hmtx, hhea.NumberOfHMetrics, baseMaxp.NumGlyphs, numGlyphs));
        }

        private static void FixVerticalMetrics(FontModel model, ushort numGlyphs)
        {
            if (model._removed.Contains(KnownTags.vmtx) || model._removed.Contains(KnownTags.vhea))
                return;

            if (model._overrides.TryGetValue(KnownTags.vmtx, out var vmtxSource) && vmtxSource is VmtxTableBuilder existing)
            {
                if (existing.NumGlyphs != numGlyphs)
                    model.SetTable(ResizeVmtx(existing, numGlyphs));
                return;
            }

            if (!model._baseFont.HasValue)
                return;

            if (!model._baseFont.Value.TryGetMaxp(out var baseMaxp))
                return;

            if (baseMaxp.NumGlyphs == numGlyphs)
                return;

            var baseFont = model._baseFont.Value;
            if (!baseFont.TryGetVhea(out var vhea) || !baseFont.TryGetVmtx(out var vmtx))
                return;

            model.SetTable(RebuildVmtxFromBase(vmtx, vhea.NumOfLongVerMetrics, baseMaxp.NumGlyphs, numGlyphs));
        }

        private static void FixOs2AvgCharWidth(FontModel model, ushort numGlyphs)
        {
            if (model._removed.Contains(KnownTags.OS2) || model._removed.Contains(KnownTags.hmtx))
                return;

            if (!model.HasTable(KnownTags.OS2))
                return;

            if (!model._overrides.TryGetValue(KnownTags.hmtx, out var hmtxSource) || hmtxSource is not HmtxTableBuilder hmtx)
                return;

            if (hmtx.NumGlyphs != numGlyphs)
                return;

            long sum = 0;
            for (ushort gid = 0; gid < numGlyphs; gid++)
            {
                if (!hmtx.TryGetMetric(gid, out ushort aw, out _))
                    return;
                sum += aw;
            }

            int avg = (int)(sum / numGlyphs);
            if (avg < short.MinValue) avg = short.MinValue;
            if (avg > short.MaxValue) avg = short.MaxValue;

            if (!model.TryEdit<Os2TableBuilder>(out var os2))
                return;

            os2.XAvgCharWidth = (short)avg;
        }

        private static HmtxTableBuilder ResizeHmtx(HmtxTableBuilder existing, ushort numGlyphs)
        {
            ushort n = existing.NumberOfHMetrics;
            if (n == 0) n = 1;
            if (n > numGlyphs) n = numGlyphs;

            var resized = new HmtxTableBuilder(numGlyphs, n);

            ushort fallbackAw = 0;
            if (existing.NumGlyphs != 0 && existing.TryGetMetric((ushort)(existing.NumGlyphs - 1), out ushort lastAw, out _))
                fallbackAw = lastAw;

            for (ushort gid = 0; gid < numGlyphs; gid++)
            {
                if (gid < existing.NumGlyphs && existing.TryGetMetric(gid, out ushort aw, out short lsb))
                    resized.SetMetric(gid, aw, lsb);
                else
                    resized.SetMetric(gid, fallbackAw, 0);
            }

            return resized;
        }

        private static VmtxTableBuilder ResizeVmtx(VmtxTableBuilder existing, ushort numGlyphs)
        {
            ushort n = existing.NumOfLongVerMetrics;
            if (n == 0) n = 1;
            if (n > numGlyphs) n = numGlyphs;

            var resized = new VmtxTableBuilder(numGlyphs, n);

            ushort fallbackAh = 0;
            if (existing.NumGlyphs != 0 && existing.TryGetMetric((ushort)(existing.NumGlyphs - 1), out ushort lastAh, out _))
                fallbackAh = lastAh;

            for (ushort gid = 0; gid < numGlyphs; gid++)
            {
                if (gid < existing.NumGlyphs && existing.TryGetMetric(gid, out ushort ah, out short tsb))
                    resized.SetMetric(gid, ah, tsb);
                else
                    resized.SetMetric(gid, fallbackAh, 0);
            }

            return resized;
        }

        private static HmtxTableBuilder RebuildHmtxFromBase(HmtxTable hmtx, ushort baseNumberOfHMetrics, ushort baseNumGlyphs, ushort numGlyphs)
        {
            ushort numberOfHMetrics = baseNumberOfHMetrics;
            if (numberOfHMetrics == 0) numberOfHMetrics = 1;
            if (numberOfHMetrics > numGlyphs) numberOfHMetrics = numGlyphs;

            var rebuilt = new HmtxTableBuilder(numGlyphs, numberOfHMetrics);

            ushort fallbackAw = 0;
            if (baseNumGlyphs != 0 && hmtx.TryGetMetric((ushort)(baseNumGlyphs - 1), baseNumberOfHMetrics, baseNumGlyphs, out var last))
                fallbackAw = last.AdvanceWidth;

            ushort copyCount = baseNumGlyphs < numGlyphs ? baseNumGlyphs : numGlyphs;
            for (ushort gid = 0; gid < copyCount; gid++)
            {
                if (!hmtx.TryGetMetric(gid, baseNumberOfHMetrics, baseNumGlyphs, out var m))
                    throw new InvalidOperationException("Cannot write font: invalid base hmtx table.");

                rebuilt.SetMetric(gid, m.AdvanceWidth, m.LeftSideBearing);
            }

            for (ushort gid = copyCount; gid < numGlyphs; gid++)
                rebuilt.SetMetric(gid, fallbackAw, 0);

            return rebuilt;
        }

        private static VmtxTableBuilder RebuildVmtxFromBase(VmtxTable vmtx, ushort baseNumOfLongVerMetrics, ushort baseNumGlyphs, ushort numGlyphs)
        {
            ushort numOfLongVerMetrics = baseNumOfLongVerMetrics;
            if (numOfLongVerMetrics == 0) numOfLongVerMetrics = 1;
            if (numOfLongVerMetrics > numGlyphs) numOfLongVerMetrics = numGlyphs;

            var rebuilt = new VmtxTableBuilder(numGlyphs, numOfLongVerMetrics);

            ushort fallbackAh = 0;
            if (baseNumGlyphs != 0 && vmtx.TryGetMetric((ushort)(baseNumGlyphs - 1), baseNumOfLongVerMetrics, baseNumGlyphs, out var last))
                fallbackAh = last.AdvanceHeight;

            ushort copyCount = baseNumGlyphs < numGlyphs ? baseNumGlyphs : numGlyphs;
            for (ushort gid = 0; gid < copyCount; gid++)
            {
                if (!vmtx.TryGetMetric(gid, baseNumOfLongVerMetrics, baseNumGlyphs, out var m))
                    throw new InvalidOperationException("Cannot write font: invalid base vmtx table.");

                rebuilt.SetMetric(gid, m.AdvanceHeight, m.TopSideBearing);
            }

            for (ushort gid = copyCount; gid < numGlyphs; gid++)
                rebuilt.SetMetric(gid, fallbackAh, 0);

            return rebuilt;
        }
    }

    private sealed class ValidateVariationTablesFixup : IFontModelFixup
    {
        public void Apply(FontModel model)
        {
            bool hasGvar = !model._removed.Contains(KnownTags.gvar) && (model._overrides.ContainsKey(KnownTags.gvar) || (model._baseFont.HasValue && model._baseFont.Value.TryGetTableSlice(KnownTags.gvar, out _)));
            bool hasCvar = !model._removed.Contains(KnownTags.cvar) && (model._overrides.ContainsKey(KnownTags.cvar) || (model._baseFont.HasValue && model._baseFont.Value.TryGetTableSlice(KnownTags.cvar, out _)));

            if (!hasGvar && !hasCvar)
                return;

            if (!model.TryGetFvarAxisCount(out ushort axisCount))
                throw new InvalidOperationException("Cannot write font: missing fvar table required by gvar/cvar.");

            if (model.TryGetAvarAxisCount(out ushort avarAxisCount))
            {
                if (avarAxisCount != axisCount)
                    throw new InvalidOperationException($"avar.AxisCount ({avarAxisCount}) must match fvar.AxisCount ({axisCount}).");
            }

            if (hasGvar)
                ValidateGvar(model, axisCount);

            if (hasCvar)
                ValidateCvar(model, axisCount);
        }

        private static void ValidateGvar(FontModel model, ushort fvarAxisCount)
        {
            if (!model.TryGetMaxpNumGlyphs(out ushort numGlyphs))
                throw new InvalidOperationException("Cannot write font: missing maxp table required to validate gvar.");

            if (model._overrides.TryGetValue(KnownTags.gvar, out var gvarSource) && gvarSource is GvarTableBuilder gvarBuilder)
            {
                using var buffer = FontBuffer.FromMemory(gvarBuilder.DataBytes);
                var slice = new TableSlice(buffer, KnownTags.gvar, directoryChecksum: 0, offset: 0, length: gvarBuilder.DataBytes.Length);
                if (!GvarTable.TryCreate(slice, out var gvar))
                    throw new InvalidOperationException("Cannot write font: gvar override is invalid.");

                if (gvar.AxisCount != fvarAxisCount)
                    throw new InvalidOperationException($"gvar.AxisCount ({gvar.AxisCount}) must match fvar.AxisCount ({fvarAxisCount}).");

                if (gvar.GlyphCount != numGlyphs)
                    throw new InvalidOperationException($"gvar.GlyphCount ({gvar.GlyphCount}) must match maxp.NumGlyphs ({numGlyphs}).");

                return;
            }

            if (model._baseFont.HasValue && model._baseFont.Value.TryGetGvar(out var baseGvar))
            {
                if (baseGvar.AxisCount != fvarAxisCount)
                    throw new InvalidOperationException($"gvar.AxisCount ({baseGvar.AxisCount}) must match fvar.AxisCount ({fvarAxisCount}).");

                if (baseGvar.GlyphCount != numGlyphs)
                    throw new InvalidOperationException($"gvar.GlyphCount ({baseGvar.GlyphCount}) must match maxp.NumGlyphs ({numGlyphs}).");
            }
        }

        private static void ValidateCvar(FontModel model, ushort fvarAxisCount)
        {
            if (model._overrides.TryGetValue(KnownTags.cvar, out var cvarSource) && cvarSource is CvarTableBuilder cvarBuilder)
            {
                if (cvarBuilder.IsStructured)
                {
                    if (cvarBuilder.AxisCount != fvarAxisCount)
                        throw new InvalidOperationException($"cvar axisCount ({cvarBuilder.AxisCount}) must match fvar.AxisCount ({fvarAxisCount}).");

                    if (model.TryGetCvtValueCount(out int cvtCount) && cvarBuilder.CvtCount != cvtCount)
                        throw new InvalidOperationException($"cvar cvtCount ({cvarBuilder.CvtCount}) must match cvt.ValueCount ({cvtCount}).");

                    return;
                }

                using var buffer = FontBuffer.FromMemory(cvarBuilder.DataBytes);
                var slice = new TableSlice(buffer, KnownTags.cvar, directoryChecksum: 0, offset: 0, length: cvarBuilder.DataBytes.Length);
                if (!CvarTable.TryCreate(slice, out var cvar))
                    throw new InvalidOperationException("Cannot write font: cvar override is invalid.");

                if (!cvar.TryGetTupleVariationStore(fvarAxisCount, out _))
                    throw new InvalidOperationException($"Cannot write font: cvar does not parse with fvar.AxisCount ({fvarAxisCount}).");

                return;
            }

            if (model._baseFont.HasValue && model._baseFont.Value.TryGetCvar(out var baseCvar))
            {
                if (!baseCvar.TryGetTupleVariationStore(fvarAxisCount, out _))
                    throw new InvalidOperationException($"Cannot write font: cvar does not parse with fvar.AxisCount ({fvarAxisCount}).");
            }
        }
    }

    private bool TryGetFvarAxisCount(out ushort axisCount)
    {
        axisCount = 0;

        if (_removed.Contains(KnownTags.fvar))
            return false;

        if (_overrides.TryGetValue(KnownTags.fvar, out var fvarSource) && fvarSource is FvarTableBuilder fvarBuilder)
        {
            axisCount = checked((ushort)fvarBuilder.AxisCount);
            return true;
        }

        if (_baseFont.HasValue && _baseFont.Value.TryGetFvar(out var fvar))
        {
            axisCount = fvar.AxisCount;
            return true;
        }

        return false;
    }

    private bool TryGetAvarAxisCount(out ushort axisCount)
    {
        axisCount = 0;

        if (_removed.Contains(KnownTags.avar))
            return false;

        if (_overrides.TryGetValue(KnownTags.avar, out var avarSource) && avarSource is AvarTableBuilder avarBuilder)
        {
            axisCount = checked((ushort)avarBuilder.AxisCount);
            return true;
        }

        if (_baseFont.HasValue && _baseFont.Value.TryGetAvar(out var avar))
        {
            axisCount = avar.AxisCount;
            return true;
        }

        return false;
    }

    private bool TryGetCvtValueCount(out int valueCount)
    {
        valueCount = 0;

        if (_removed.Contains(KnownTags.cvt))
            return false;

        if (_overrides.TryGetValue(KnownTags.cvt, out var cvtSource) && cvtSource is CvtTableBuilder cvtBuilder)
        {
            valueCount = cvtBuilder.ValueCount;
            return true;
        }

        if (_baseFont.HasValue && _baseFont.Value.TryGetCvt(out var cvt))
        {
            valueCount = cvt.ValueCount;
            return true;
        }

        return false;
    }
}
