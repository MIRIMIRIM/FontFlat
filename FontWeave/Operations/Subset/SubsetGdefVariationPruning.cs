using FontWeave.Core;
using FontWeave.Tables.Layout;
using FontWeave.Tables.Variation;

namespace FontWeave.Operations.Subset;

public static partial class SubsetOperation
{
    private static ReadStatus PruneSharedGdefVariations(PrepareState state,
        ref byte[]? gdefBytes, ref byte[]? gposBytes, ref byte[]? jstfBytes,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (state.RewrittenLayoutModels is not { Gdef.ItemVariationStore: { } store } models)
            return ReadStatus.Done;
        foreach (var device in MathDevices(state.Math))
        {
            if (!state.Budget.Work(1, state.Math!.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            if (device is { IsVariationDevice: true }) return ReadStatus.Done;
        }
        var collect = new GdefVariationWalker(state.Budget);
        collect.Gdef(models.Gdef);
        if (models.Gpos is { } gpos) collect.Layout(gpos);
        if (models.Jstf is { } jstf) collect.Jstf(jstf);
        if (collect.Status != ReadStatus.Done)
        {
            diagnostic = collect.Diagnostic;
            return collect.Status;
        }
        ReadStatus status = PruneVariationStore(store, collect.LiveIndices, state.Budget,
            out var pruned, out var indexMap, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var gdef = models.Gdef;
        var classes = new GdefClassDefinitions(gdef.Version, gdef.NumGlyphs,
            gdef.GlyphClasses, gdef.MarkAttachmentClasses, 0, 0, 0, 0, gdef.Location);
        status = GdefLookupContext.TryCreate(classes, gdef.MarkGlyphSets, pruned,
            out var context, out diagnostic, state.Budget);
        if (status != ReadStatus.Done) return status;
        var rewrite = new GdefVariationWalker(state.Budget, indexMap!, pruned!, context!);
        GdefTable rebuiltGdef = rewrite.Gdef(gdef);
        LayoutListsTable? rebuiltGpos = models.Gpos is { } gp ? rewrite.Layout(gp) : null;
        JstfTable? rebuiltJstf = models.Jstf is { } js ? rewrite.Jstf(js) : null;
        if (rewrite.Status != ReadStatus.Done)
        {
            diagnostic = rewrite.Diagnostic;
            return rewrite.Status;
        }
        status = GdefTableCompiler.TryCompile(rebuiltGdef, out var nextGdef,
            out diagnostic, state.Budget);
        if (status != ReadStatus.Done) return status;
        byte[]? nextGpos = null, nextJstf = null;
        if (rebuiltGpos is not null)
        {
            status = GposTableCompiler.TryCompile(rebuiltGpos, context, out nextGpos,
                out diagnostic, state.Budget);
            if (status != ReadStatus.Done) return status;
        }
        if (rebuiltJstf is not null)
        {
            status = JstfTableCompiler.TryCompile(rebuiltJstf, out nextJstf,
                out diagnostic, state.Budget);
            if (status != ReadStatus.Done) return status;
        }
        gdefBytes = nextGdef;
        if (rebuiltGpos is not null) gposBytes = nextGpos;
        if (rebuiltJstf is not null) jstfBytes = nextJstf;
        state.GdefContext = context;
        models.Gdef = rebuiltGdef;
        models.Gpos = rebuiltGpos;
        models.Jstf = rebuiltJstf;
        if (state.LayoutRewriteCensus is { } census)
            for (int i = 0; i < census.Count; i++)
            {
                byte[]? bytes = census[i].Tag switch
                {
                    Gdef => nextGdef,
                    Gpos => nextGpos,
                    Jstf => nextJstf,
                    _ => null
                };
                if (bytes is not null) census[i] = census[i] with { OutputBytes = bytes.Length };
            }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static IEnumerable<StaticDeviceTable?> MathDevices(MathTable? math)
    {
        if (math is null) yield break;
        if (math.Constants is { } constants)
            foreach (var device in constants.Devices) yield return device;
        if (math.GlyphInfo is { } info)
        {
            if (info.ItalicsCorrectionInfo is { } italics)
                foreach (var device in italics.Devices) yield return device;
            if (info.TopAccentAttachment is { } accents)
                foreach (var device in accents.Devices) yield return device;
            if (info.KernInfo is { } kerns)
                foreach (var record in kerns.Records)
                {
                    foreach (var device in MathKernDevices(record.TopRight)) yield return device;
                    foreach (var device in MathKernDevices(record.TopLeft)) yield return device;
                    foreach (var device in MathKernDevices(record.BottomRight)) yield return device;
                    foreach (var device in MathKernDevices(record.BottomLeft)) yield return device;
                }
        }
        if (math.Variants is { } variants)
        {
            foreach (var construction in variants.VertConstructions)
                yield return construction.Assembly?.ItalicsCorrectionDevice;
            foreach (var construction in variants.HorizConstructions)
                yield return construction.Assembly?.ItalicsCorrectionDevice;
        }
    }

    private static IEnumerable<StaticDeviceTable?> MathKernDevices(MathKernTable? kern)
    {
        if (kern is null) yield break;
        foreach (var device in kern.HeightDevices) yield return device;
        foreach (var device in kern.KernValueDevices) yield return device;
    }

    private sealed class GdefVariationWalker(BuildBudget budget,
        Dictionary<DeltaSetIndex, DeltaSetIndex>? indexMap = null,
        VariationStoreTable? store = null, GdefLookupContext? context = null)
    {
        private readonly Dictionary<object, object> _cache = new(ReferenceEqualityComparer.Instance);
        internal HashSet<DeltaSetIndex> LiveIndices { get; } = [];
        internal ReadStatus Status = ReadStatus.Done;
        internal FontDiagnostic Diagnostic;

        private bool Reserve(int count, int elementBytes, DataLocation at)
        {
            if (Status != ReadStatus.Done) return false;
            if (!budget.Work(count, at, out Diagnostic) ||
                indexMap is not null && !budget.Buffer((long)count * elementBytes, at, out Diagnostic))
            {
                Status = ReadStatus.ResourceLimit;
                return false;
            }
            return true;
        }

        private T Cached<T>(T source, Func<T, T> visit) where T : class
        {
            if (Status != ReadStatus.Done) return source;
            if (_cache.TryGetValue(source, out var cached)) return (T)cached;
            T result = visit(source);
            _cache.Add(source, result);
            return result;
        }

        private T[]? Span<T>(ReadOnlySpan<T> source, Func<T, T> visit,
            int elementBytes, DataLocation at)
        {
            if (!Reserve(source.Length, elementBytes, at)) return null;
            T[]? output = indexMap is null ? null : new T[source.Length];
            for (int i = 0; i < source.Length && Status == ReadStatus.Done; i++)
            {
                T value = visit(source[i]);
                if (output is not null) output[i] = value;
            }
            return output;
        }

        private T[]? List<T>(IReadOnlyList<T> source, Func<T, T> visit,
            int elementBytes, DataLocation at)
        {
            if (!Reserve(source.Count, elementBytes, at)) return null;
            T[]? output = indexMap is null ? null : new T[source.Count];
            for (int i = 0; i < source.Count && Status == ReadStatus.Done; i++)
            {
                T value = visit(source[i]);
                if (output is not null) output[i] = value;
            }
            return output;
        }

        private StaticDeviceTable? Device(StaticDeviceTable? source) =>
            source is null ? null : Cached(source, DeviceCore);

        private StaticDeviceTable DeviceCore(StaticDeviceTable source)
        {
            if (!Reserve(1, 0, source.Location) || !source.IsVariationDevice) return source;
            var oldIndex = new DeltaSetIndex(source.Outer, source.Inner);
            if (indexMap is null)
            {
                LiveIndices.Add(oldIndex);
                return source;
            }
            var mapped = indexMap[oldIndex];
            return new(mapped.Outer, mapped.Inner, source.DeltaFormat, [], source.Location, 6);
        }

        private StaticValueRecord Value(StaticValueRecord value) => value with
        {
            XPlacementDevice = Device(value.XPlacementDevice),
            YPlacementDevice = Device(value.YPlacementDevice),
            XAdvanceDevice = Device(value.XAdvanceDevice),
            YAdvanceDevice = Device(value.YAdvanceDevice)
        };

        private AnchorTable? Anchor(AnchorTable? source) => source is null ? null : Cached(source, AnchorCore);

        private AnchorTable AnchorCore(AnchorTable source)
        {
            var x = Device(source.XDevice);
            var y = Device(source.YDevice);
            return indexMap is null ? source : new(source.Format, source.XCoordinate,
                source.YCoordinate, source.AnchorPoint, x, y, source.Location, 0);
        }

        internal GdefTable Gdef(GdefTable source) => Cached(source, GdefCore);

        private GdefTable GdefCore(GdefTable source)
        {
            var carets = source.LigCaretList;
            if (carets is not null)
            {
                var glyphs = List(carets.LigGlyphs, LigGlyph, 8, carets.Location);
                if (glyphs is not null)
                    carets = new(carets.Coverage, glyphs, carets.NumGlyphs, carets.Location, 0, []);
            }
            return indexMap is null ? source : new(source.Version, source.NumGlyphs,
                source.GlyphClasses, source.MarkAttachmentClasses, source.MarkGlyphSets,
                source.AttachList, carets, store, 0, source.Location, 0);
        }

        private LigGlyphTable LigGlyph(LigGlyphTable source) => Cached(source, LigGlyphCore);

        private LigGlyphTable LigGlyphCore(LigGlyphTable source)
        {
            if (!Reserve(source.Count, 8, source.Location)) return source;
            var entries = indexMap is null ? null : new CaretValueEntry[source.Count];
            for (int i = 0; i < source.Count && Status == ReadStatus.Done; i++)
            {
                var caret = source[i];
                var device = Device(caret.Device);
                if (entries is not null)
                    entries[i] = device is null ? caret :
                        CaretValueEntry.ForDevice(caret.Coordinate!.Value, device, caret.Location);
            }
            return entries is null ? source : new(entries, source.Location, 0);
        }

        internal LayoutListsTable Layout(LayoutListsTable source)
        {
            var lookups = Span(source.Lookups, Lookup, 8, source.Location);
            if (lookups is null || !Reserve(source.Scripts.Length + source.Features.Length, 8, source.Location))
                return source;
            return new(source.Version, source.FeatureVariationsOffsetRaw, source.Scripts.ToArray(),
                source.Features.ToArray(), lookups, source.NumGlyphs, source.Kind,
                source.Location, source.FeatureVariations);
        }

        private LookupTable Lookup(LookupTable source) => Cached(source, LookupCore);

        private LookupTable LookupCore(LookupTable source)
        {
            switch (source.LookupType)
            {
                case 1:
                    var singles = List(source.SinglePositions!, Single, 8, source.Location);
                    return singles is null ? source : source.WithDecodedPositions(singles);
                case 2:
                    var pairs = List(source.PairPositions!, Pair, 8, source.Location);
                    return pairs is null ? source : source.WithDecodedPairPositions(pairs);
                case 3:
                    var cursives = List(source.CursiveAttachments!, Cursive, 8, source.Location);
                    return cursives is null ? source : source.WithDecodedCursive(cursives);
                case 4:
                    var bases = List(source.MarkBases!, MarkBase, 8, source.Location);
                    return bases is null ? source : source.WithDecodedMarkBases(bases);
                case 5:
                    var ligs = List(source.MarkLigs!, MarkLig, 8, source.Location);
                    return ligs is null ? source : source.WithDecodedMarkLigs(ligs);
                case 6:
                    var marks = List(source.MarkMarks!, MarkMark, 8, source.Location);
                    return marks is null ? source : source.WithDecodedMarkMarks(marks);
                case 7 or 8:
                    return source;
                case 9:
                    var extensions = List(source.ExtensionPositions!, Extension, 8, source.Location);
                    return extensions is null ? source : source.WithDecodedExtensionPositions(extensions);
                default:
                    throw new InvalidOperationException("An admitted GPOS lookup has no typed variation traversal.");
            }
        }

        private SinglePosTable Single(SinglePosTable source) => Cached(source, SingleCore);
        private SinglePosTable SingleCore(SinglePosTable source)
        {
            var values = Span(source.Values, Value, 40, source.Location);
            return values is null ? source : new(source.Coverage, values, source.Format,
                source.ValueFormat, source.Location, 0);
        }

        private PairPosTable Pair(PairPosTable source) => Cached(source, PairCore);
        private PairPosTable PairCore(PairPosTable source)
        {
            if (source.Format == 1)
            {
                var sets = List(source.PairSets!, PairSet, 8, source.Location);
                return sets is null ? source : new(source.Coverage, source.Format,
                    source.ValueFormat1, source.ValueFormat2, pairSets: sets, location: source.Location);
            }
            int count = source.Class1Count * source.Class2Count;
            if (!Reserve(count, 80, source.Location)) return source;
            var first = indexMap is null ? null : new StaticValueRecord[count];
            var second = indexMap is null ? null : new StaticValueRecord[count];
            for (int i = 0; i < count && Status == ReadStatus.Done; i++)
            {
                var a = Value(source.GetMatrixValue1(i / source.Class2Count, i % source.Class2Count));
                var b = Value(source.GetMatrixValue2(i / source.Class2Count, i % source.Class2Count));
                if (first is not null) { first[i] = a; second![i] = b; }
            }
            return first is null ? source : new(source.Coverage, source.Format,
                source.ValueFormat1, source.ValueFormat2, classDef1: source.ClassDef1,
                classDef2: source.ClassDef2, class1Count: (ushort)source.Class1Count,
                class2Count: (ushort)source.Class2Count, values1: first, values2: second, location: source.Location);
        }

        private PairSetTable PairSet(PairSetTable source)
        {
            var rows = Span(source.Records,
                row => new PairValueRecord(row.SecondGlyph, Value(row.Value1), Value(row.Value2)),
                88, source.Location);
            return rows is null ? source : new(rows, source.Location, 0);
        }

        private CursivePosTable Cursive(CursivePosTable source) => Cached(source, CursiveCore);
        private CursivePosTable CursiveCore(CursivePosTable source)
        {
            var rows = Span(source.Records,
                row => new EntryExitTable(Anchor(row.Entry), Anchor(row.Exit)), 16, source.Location);
            return rows is null ? source : new(source.Coverage, rows, source.NumGlyphs, source.Location, 0);
        }

        private MarkRecord Mark(MarkRecord record) => new(record.Class, Anchor(record.Anchor)!);
        private MarkBasePosTable MarkBase(MarkBasePosTable source) => Cached(source, MarkBaseCore);
        private MarkBasePosTable MarkBaseCore(MarkBasePosTable source)
        {
            var marks = Span(source.Marks, Mark, 16, source.Location);
            if (!Reserve(source.BaseCount, 8, source.Location)) return source;
            var rows = indexMap is null ? null : new AnchorTable?[source.BaseCount][];
            for (int i = 0; i < source.BaseCount && Status == ReadStatus.Done; i++)
            {
                var row = Span(source.BaseRow(i), Anchor, 8, source.Location);
                if (rows is not null) rows[i] = row!;
            }
            return rows is null ? source : new(source.MarkCoverage, marks!, source.BaseCoverage,
                source.ClassCount, rows, source.NumGlyphs, source.Location, 0);
        }

        private MarkLigPosTable MarkLig(MarkLigPosTable source) => Cached(source, MarkLigCore);
        private MarkLigPosTable MarkLigCore(MarkLigPosTable source)
        {
            var marks = Span(source.Marks, Mark, 16, source.Location);
            var ligs = List(source.Ligatures, Ligature, 8, source.Location);
            return ligs is null ? source : new(source.MarkCoverage, marks!, source.LigatureCoverage,
                ligs, source.NumGlyphs, source.Location, 0);
        }

        private LigatureAttachTable Ligature(LigatureAttachTable source) => Cached(source, LigatureCore);
        private LigatureAttachTable LigatureCore(LigatureAttachTable source)
        {
            if (!Reserve(source.Components, 8, source.Location)) return source;
            var rows = indexMap is null ? null : new AnchorTable?[source.Components][];
            for (int i = 0; i < source.Components && Status == ReadStatus.Done; i++)
            {
                var row = Span(source.ComponentRow(i), Anchor, 8, source.Location);
                if (rows is not null) rows[i] = row!;
            }
            return rows is null ? source : new(source.ComponentCount, rows, source.Location, 0);
        }

        private MarkMarkPosTable MarkMark(MarkMarkPosTable source) => Cached(source, MarkMarkCore);
        private MarkMarkPosTable MarkMarkCore(MarkMarkPosTable source)
        {
            var marks = Span(source.Marks, Mark, 16, source.Location);
            if (!Reserve(source.Mark2Count, 8, source.Location)) return source;
            var rows = indexMap is null ? null : new AnchorTable?[source.Mark2Count][];
            for (int i = 0; i < source.Mark2Count && Status == ReadStatus.Done; i++)
            {
                var row = Span(source.Mark2Row(i), Anchor, 8, source.Location);
                if (rows is not null) rows[i] = row!;
            }
            return rows is null ? source : new(source.MarkCoverage, marks!, source.Mark2Coverage,
                source.ClassCount, rows, source.NumGlyphs, source.Location, 0);
        }

        private ExtensionPosTable Extension(ExtensionPosTable source) => Cached(source, ExtensionCore);
        private ExtensionPosTable ExtensionCore(ExtensionPosTable source)
        {
            var single = source.Single is null ? null : Single(source.Single);
            var pair = source.Pair is null ? null : Pair(source.Pair);
            var cursive = source.Cursive is null ? null : Cursive(source.Cursive);
            var markBase = source.MarkBase is null ? null : MarkBase(source.MarkBase);
            var markLig = source.MarkLig is null ? null : MarkLig(source.MarkLig);
            var markMark = source.MarkMark is null ? null : MarkMark(source.MarkMark);
            return indexMap is null ? source : new(source.ExtensionLookupType, single, pair,
                cursive, source.Location, 0, markBase, markLig, markMark, source.Contextual, source.Chaining);
        }

        internal JstfTable Jstf(JstfTable source)
        {
            var scripts = List(source.Scripts,
                row => new JstfScriptRecord(row.Tag, Script(row.Script)), 16, source.Location);
            return scripts is null ? source : new(source.Version, source.NumGlyphs,
                source.GsubLookupCount, source.GposLookupCount, context, scripts, source.Location, 0);
        }

        private JstfScriptTable Script(JstfScriptTable source) => Cached(source, ScriptCore);
        private JstfScriptTable ScriptCore(JstfScriptTable source)
        {
            var defaultLang = source.DefaultLangSys is null ? null : LangSys(source.DefaultLangSys);
            var languages = List(source.LangSys,
                row => new JstfLangSysRecord(row.Tag, LangSys(row.LangSys)), 16, source.Location);
            return languages is null ? source : new(source.ExtenderGlyphs, defaultLang,
                languages, source.Location, 0);
        }

        private JstfLangSysTable LangSys(JstfLangSysTable source) => Cached(source, LangSysCore);
        private JstfLangSysTable LangSysCore(JstfLangSysTable source)
        {
            var priorities = List(source.Priorities, Priority, 8, source.Location);
            return priorities is null ? source : new(priorities, source.Location, 0);
        }

        private JstfPriorityTable Priority(JstfPriorityTable source) => Cached(source, PriorityCore);
        private JstfPriorityTable PriorityCore(JstfPriorityTable source)
        {
            var shrink = source.ShrinkageJstfMax is null ? null : Maximum(source.ShrinkageJstfMax);
            var extend = source.ExtensionJstfMax is null ? null : Maximum(source.ExtensionJstfMax);
            return indexMap is null ? source : new(source.GsubShrinkageEnable,
                source.GsubShrinkageDisable, source.GposShrinkageEnable, source.GposShrinkageDisable,
                shrink, source.GsubExtensionEnable, source.GsubExtensionDisable,
                source.GposExtensionEnable, source.GposExtensionDisable, extend, source.Location, 0);
        }

        private JstfMaxTable Maximum(JstfMaxTable source) => Cached(source, MaximumCore);
        private JstfMaxTable MaximumCore(JstfMaxTable source)
        {
            var lookups = List(source.Lookups, Lookup, 8, source.Location);
            return lookups is null ? source : new(lookups, source.Location, 0);
        }
    }
}
