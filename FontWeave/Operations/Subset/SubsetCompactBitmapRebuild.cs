using System.IO.Compression;
using System.Text;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap bitmap strikes and the sbix/SVG document lists (SE card
/// §2.2-§2.5).
///
/// The CBDT/CBLC and EBDT/EBLC pairs rebuild jointly: the joint read chain
/// materializes the strikes and the pool in canonical chain order, the walk prunes
/// per slot (the k-th populated slot pairs with the k-th image, OQ-SE-2 — the same
/// order the compilers' fixup contract itself consumes), the index layer splits the
/// contiguous formats into maximal retained runs and filters the sparse arrays, the
/// strike rows keep every non-rewritten field verbatim with the surviving span, and
/// empty strikes (and an all-empty pair) drop (OQ-SE-5). Composite frames'
/// component glyph references remap; a dangling component refuses (RE3, OQ-SE-6).
/// The rebuilt parts re-answer the full pairing matrix through the compiler
/// factories, which re-derive every offset/sbitOffset/pool position from the k-th
/// pairing. EBSC rides the rebuilt EBLC strike set: the paired validated read
/// supplies the chain's own faces (a missing EBLC partner propagates as RE2), each
/// record survives only while its substitute ppem pair still names a surviving
/// strike, records travel verbatim, and an all-dead record set drops the table.
/// sbix re-slots every strike over the output domain with 'dupe' targets remapped
/// (a dangling target refuses, RE3) and SVG re-spans the document records over
/// each range's surviving output runs with only the referenced documents kept —
/// their bytes verbatim unless the remap moves a glyph id the document carries:
/// the XML walk rewrites an id's value, reference fragments (character references
/// decoded first) and the internal style sheet's ID selectors/url fragments, while
/// comments, CDATA and processing instructions never open an attribute context;
/// gzip members are rewritten the same way. A style sheet using a CSS escape
/// refuses rather than emitting a selector the map cannot follow. Every rebuilt
/// model re-answers through the compiler factories; any failure refuses with
/// nothing published.</summary>
public static partial class SubsetOperation
{
    // ---- CBDT/CBLC ----

    private static ReadStatus RebuildCbdtCblc(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? cbdtBytes, out byte[]? cblcBytes,
        out bool dropped, out FontDiagnostic diagnostic)
    {
        cbdtBytes = null;
        cblcBytes = null;
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Cbdt, out var cbdtAt,
            out var cbdtSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadTableBytes(directory, Cblc, out var cblcAt, out var cblcSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CblcView.TryOpen(cblcSource, out var cblcView, out diagnostic, cblcAt);
        if (status != ReadStatus.Done) return status;
        status = cblcView.TryReadTable(cbdtSource.Length, maxp.NumGlyphs,
            out var cblc, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = CbdtView.TryOpen(cbdtSource, out var cbdtView, out diagnostic, cbdtAt);
        if (status != ReadStatus.Done) return status;
        status = cbdtView.TryReadTable(cblcView, maxp.NumGlyphs, out var cbdt,
            out diagnostic, budget.Limits);
        if (status != ReadStatus.Done) return status;
        var newStrikes = new List<CblcStrike>();
        var newImages = new List<CbdtImage>();
        status = RebuildCblcStrikes(cblc!, cbdt!, map, cbdtAt, newStrikes, newImages,
            budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (newImages.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = CbdtTableCompiler.TryCreate(3, 0, newImages, out var newCbdt,
            out diagnostic, location: cbdtAt);
        if (status != ReadStatus.Done) return status;
        status = CblcTableCompiler.TryCreate(3, 0, newStrikes, newCbdt!,
            out var newCblc, out diagnostic, location: cblcAt);
        if (status != ReadStatus.Done) return status;
        // TryCompilePair re-derives the canonical index layer against the packed
        // pool one more time (the k-th pairing contract) and emits both tables.
        status = CblcTableCompiler.TryCompilePair(newCblc!, newCbdt!, out cblcBytes,
            out cbdtBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ChargeCompiled(budget, cblcAt, cblcBytes!, out cblcBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, cbdtAt, cbdtBytes!, out cbdtBytes, out diagnostic);
    }

    /// <summary>The joint chain walk: strikes in wire order, records in wire order,
    /// glyph slots ascending, absent forms skipped — the order the pool arrived in.
    /// Contiguous formats split into maximal retained runs (the record's own index
    /// and image formats preserved); sparse arrays filter slot by slot; the strike
    /// rows keep every non-rewritten field verbatim with the surviving span.</summary>
    private static ReadStatus RebuildCblcStrikes(CblcTable cblc, CbdtTable cbdt,
        GlyphRemap map, DataLocation cbdtAt, List<CblcStrike> newStrikes,
        List<CbdtImage> newImages, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ReadStatus status;
        var images = cbdt.Images;
        int imageCursor = 0;
        foreach (var strike in cblc.Sizes)
        {
            var newSubtables = new List<CblcIndexSubtable>();
            int strikeMin = -1, strikeMax = -1;
            FontDiagnostic emitDiagnostic = default;
            // One run of kept slots: the images join the new pool in chain order
            // with composite component references remapped (a dangling one refuses,
            // RE3); the record lands with the run's mapped bounds.
            ReadStatus EmitRun(List<(int Glyph, CbdtImage Image)> slots,
                Func<int, int, CblcIndexSubtable> build)
            {
                int runStart = -1;
                for (int s = 0; s <= slots.Count; s++)
                {
                    bool kept = s < slots.Count && map.OutputOf(slots[s].Glyph) >= 0;
                    if (kept)
                    {
                        if (runStart < 0) runStart = s;
                        // The strike row's surviving span lives in the output domain
                        // (the output face's glyph space), so the remapped value is
                        // what the min/max tracks.
                        int output = map.OutputOf(slots[s].Glyph);
                        if (strikeMin < 0 || output < strikeMin) strikeMin = output;
                        if (output > strikeMax) strikeMax = output;
                        newImages.Add(RemappedImage(slots[s].Image, map, cbdtAt,
                            out emitDiagnostic));
                        if (emitDiagnostic.Code != 0) return ReadStatus.InvalidFormat;
                    }
                    if (runStart >= 0 && (!kept || s == slots.Count))
                    {
                        newSubtables.Add(build(runStart, s - 1));
                        runStart = -1;
                    }
                }
                return ReadStatus.Done;
            }
            foreach (var part in strike.Subtables)
            {
                // One (input glyph, image) pair per populated slot, in chain order;
                // the image consumption mirrors the reader's own slot enumeration
                // (CountSlots), which is what keeps the k-th pairing aligned.
                var slots = new List<(int Glyph, CbdtImage Image)>();
                switch (part)
                {
                    case CblcOffsetSubtable offsets:
                    {
                        int span = offsets.SbitOffsets.Length - 1;
                        if (!budget.Work(span, cbdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int k = 0; k < span; k++)
                        {
                            if (offsets.SbitOffsets[k] == offsets.SbitOffsets[k + 1])
                                continue;
                            slots.Add((offsets.FirstGlyphIndex + k, images[imageCursor++]));
                        }
                        status = EmitRun(slots,
                            (first, last) => new CblcOffsetSubtable(newSubtables.Count,
                                offsets.IndexFormat, offsets.ImageFormat, 0,
                                (ushort)map.OutputOf(slots[first].Glyph),
                                (ushort)map.OutputOf(slots[last].Glyph),
                                RunPattern(slots, first, last, map)));
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case CblcImageSizeSubtable size:
                    {
                        if (size.ImageSize == 0) break;
                        if (!budget.Work(size.LastGlyphIndex - size.FirstGlyphIndex + 1,
                                cbdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int g = size.FirstGlyphIndex; g <= size.LastGlyphIndex; g++)
                            slots.Add((g, images[imageCursor++]));
                        status = EmitRun(slots,
                            (first, last) => new CblcImageSizeSubtable(newSubtables.Count,
                                size.ImageFormat, 0,
                                (ushort)map.OutputOf(slots[first].Glyph),
                                (ushort)map.OutputOf(slots[last].Glyph), size.ImageSize,
                                size.BigMetrics));
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case CblcPairSubtable pairs:
                    {
                        int count = pairs.GlyphIds.Length - 1;
                        if (!budget.Work(count, cbdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int k = 0; k < count; k++)
                        {
                            if (pairs.SbitOffsets[k] == pairs.SbitOffsets[k + 1]) continue;
                            slots.Add((pairs.GlyphIds[k], images[imageCursor++]));
                        }
                        status = EmitRun(slots,
                            (first, last) =>
                            {
                                var glyphs = new ushort[last - first + 2];
                                for (int s = first; s <= last; s++)
                                    glyphs[s - first] =
                                        (ushort)map.OutputOf(slots[s].Glyph);
                                glyphs[^1] = 0xFFFF; // the sizing sentinel
                                var offsets = new ushort[glyphs.Length];
                                for (int s = 0; s < offsets.Length; s++)
                                    offsets[s] = (ushort)s;
                                return new CblcPairSubtable(newSubtables.Count,
                                    pairs.ImageFormat, 0,
                                    (ushort)map.OutputOf(slots[first].Glyph),
                                    (ushort)map.OutputOf(slots[last].Glyph), glyphs,
                                    offsets);
                            });
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case CblcGlyphIdArraySubtable array:
                    {
                        if (array.ImageSize == 0) break;
                        if (!budget.Work(array.GlyphIds.Length, cbdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        foreach (ushort glyph in array.GlyphIds)
                            slots.Add((glyph, images[imageCursor++]));
                        status = EmitRun(slots,
                            (first, last) =>
                            {
                                var glyphs = new ushort[last - first + 1];
                                for (int s = first; s <= last; s++)
                                    glyphs[s - first] =
                                        (ushort)map.OutputOf(slots[s].Glyph);
                                return new CblcGlyphIdArraySubtable(newSubtables.Count,
                                    array.ImageFormat, 0,
                                    (ushort)map.OutputOf(slots[first].Glyph),
                                    (ushort)map.OutputOf(slots[last].Glyph),
                                    array.ImageSize, array.BigMetrics, glyphs);
                            });
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                }
            }
            if (newSubtables.Count == 0) continue;
            newStrikes.Add(new CblcStrike(0, 0, strike.ColorRef, strike.HoriMetrics,
                strike.VertMetrics, (ushort)strikeMin, (ushort)strikeMax, strike.PpemX,
                strike.PpemY, strike.BitDepth, strike.Flags, [.. newSubtables]));
        }
        return ReadStatus.Done;
    }

    /// <summary>One pool frame verbatim except a composite frame's component glyph
    /// references, which remap through the frozen map (RE3 on a dangling one).</summary>
    private static CbdtImage RemappedImage(CbdtImage image, GlyphRemap map,
        DataLocation cbdtAt, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (image.ImageFormat is not (8 or 9) || image.Components.Length == 0)
            return image;
        var components = new CbdtComponent[image.Components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            var component = image.Components[i];
            int output = map.OutputOf(component.GlyphId);
            if (output < 0)
            {
                _ = ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    cbdtAt.AtLocal(DataField.GlyphId, image.SourcePosition ?? 0, i),
                    out diagnostic, component.GlyphId, 0UL);
                return image;
            }
            components[i] = new((ushort)output, component.XOffset, component.YOffset);
        }
        return new CbdtImage(image.ImageFormat, image.Metrics, image.Payload,
            image.BitDepth, components, image.CompositePad);
    }

    /// <summary>One format-1/3 run's offset pattern over the record's full output
    /// span: the run's kept slots land populated (adjacent entries differ) in chain
    /// order and every other output glyph of the span — an interior source-absent
    /// form — stays an equal-offset absent pair. The values themselves re-derive
    /// from the paired frames at the compiler's rewrite; only the populated
    /// pattern travels.</summary>
    private static uint[] RunPattern(List<(int Glyph, CbdtImage Image)> slots,
        int first, int last, GlyphRemap map)
    {
        int outFirst = map.OutputOf(slots[first].Glyph);
        int outLast = map.OutputOf(slots[last].Glyph);
        var pattern = new uint[outLast - outFirst + 2];
        int kept = first;
        uint run = 0;
        for (int o = 0; o < pattern.Length - 1; o++)
        {
            pattern[o] = run;
            if (kept <= last && map.OutputOf(slots[kept].Glyph) == outFirst + o)
            {
                run++;
                kept++;
            }
        }
        pattern[^1] = run;
        return pattern;
    }

    /// <summary>The EBDT/EBLC mirror of the run pattern (the same index codec).</summary>
    private static uint[] RunPattern(List<(int Glyph, EbdtImage Image)> slots,
        int first, int last, GlyphRemap map)
    {
        int outFirst = map.OutputOf(slots[first].Glyph);
        int outLast = map.OutputOf(slots[last].Glyph);
        var pattern = new uint[outLast - outFirst + 2];
        int kept = first;
        uint run = 0;
        for (int o = 0; o < pattern.Length - 1; o++)
        {
            pattern[o] = run;
            if (kept <= last && map.OutputOf(slots[kept].Glyph) == outFirst + o)
            {
                run++;
                kept++;
            }
        }
        pattern[^1] = run;
        return pattern;
    }

    // ---- EBDT/EBLC (+ EBSC linkage) ----

    private static ReadStatus RebuildEbdtEblc(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? ebdtBytes, out byte[]? eblcBytes,
        out HashSet<int> survivingStrikes, out bool dropped, out FontDiagnostic diagnostic)
    {
        ebdtBytes = null;
        eblcBytes = null;
        survivingStrikes = [];
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Ebdt, out var ebdtAt,
            out var ebdtSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadTableBytes(directory, Eblc, out var eblcAt, out var eblcSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = EblcView.TryOpen(eblcSource, out var eblcView, out diagnostic, eblcAt);
        if (status != ReadStatus.Done) return status;
        status = eblcView.TryReadTable(ebdtSource.Length, maxp.NumGlyphs, out var eblc,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = EbdtView.TryOpen(ebdtSource, out var ebdtView, out diagnostic, ebdtAt);
        if (status != ReadStatus.Done) return status;
        status = ebdtView.TryReadTable(eblcView, maxp.NumGlyphs, out var ebdt,
            out diagnostic, budget.Limits);
        if (status != ReadStatus.Done) return status;
        var newStrikes = new List<EblcStrike>();
        var newImages = new List<EbdtImage>();
        status = RebuildEblcStrikes(eblc!, ebdt!, map, ebdtAt, newStrikes, newImages,
            survivingStrikes, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (newImages.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = EbdtTableCompiler.TryCreate(2, 0, newImages, out var newEbdt,
            out diagnostic, location: ebdtAt);
        if (status != ReadStatus.Done) return status;
        status = EblcTableCompiler.TryCreate(2, 0, newStrikes, newEbdt!,
            out var newEblc, out diagnostic, location: eblcAt);
        if (status != ReadStatus.Done) return status;
        status = EblcTableCompiler.TryCompilePair(newEblc!, newEbdt!, out eblcBytes,
            out ebdtBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ChargeCompiled(budget, eblcAt, eblcBytes!, out eblcBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, ebdtAt, ebdtBytes!, out ebdtBytes, out diagnostic);
    }

    /// <summary>The EBDT/EBLC mirror of the joint chain walk; the surviving
    /// strikes' (ppemX, ppemY) pairs feed the EBSC record filter.</summary>
    private static ReadStatus RebuildEblcStrikes(EblcTable eblc, EbdtTable ebdt,
        GlyphRemap map, DataLocation ebdtAt, List<EblcStrike> newStrikes,
        List<EbdtImage> newImages, HashSet<int> survivingStrikes, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ReadStatus status;
        var images = ebdt.Images;
        int imageCursor = 0;
        foreach (var strike in eblc.Sizes)
        {
            var newSubtables = new List<EblcIndexSubtable>();
            int strikeMin = -1, strikeMax = -1;
            FontDiagnostic emitDiagnostic = default;
            ReadStatus EmitRun(List<(int Glyph, EbdtImage Image)> slots,
                Func<int, int, EblcIndexSubtable> build)
            {
                int runStart = -1;
                for (int s = 0; s <= slots.Count; s++)
                {
                    bool kept = s < slots.Count && map.OutputOf(slots[s].Glyph) >= 0;
                    if (kept)
                    {
                        if (runStart < 0) runStart = s;
                        // The strike row's surviving span lives in the output domain.
                        int output = map.OutputOf(slots[s].Glyph);
                        if (strikeMin < 0 || output < strikeMin) strikeMin = output;
                        if (output > strikeMax) strikeMax = output;
                        newImages.Add(RemappedEbdtImage(slots[s].Image, map, ebdtAt,
                            out emitDiagnostic));
                        if (emitDiagnostic.Code != 0) return ReadStatus.InvalidFormat;
                    }
                    if (runStart >= 0 && (!kept || s == slots.Count))
                    {
                        newSubtables.Add(build(runStart, s - 1));
                        runStart = -1;
                    }
                }
                return ReadStatus.Done;
            }
            foreach (var part in strike.Subtables)
            {
                var slots = new List<(int Glyph, EbdtImage Image)>();
                switch (part)
                {
                    case EblcOffsetSubtable offsets:
                    {
                        int span = offsets.SbitOffsets.Length - 1;
                        if (!budget.Work(span, ebdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int k = 0; k < span; k++)
                        {
                            if (offsets.SbitOffsets[k] == offsets.SbitOffsets[k + 1])
                                continue;
                            slots.Add((offsets.FirstGlyphIndex + k, images[imageCursor++]));
                        }
                        status = EmitRun(slots,
                            (first, last) => new EblcOffsetSubtable(newSubtables.Count,
                                offsets.IndexFormat, offsets.ImageFormat, 0,
                                (ushort)map.OutputOf(slots[first].Glyph),
                                (ushort)map.OutputOf(slots[last].Glyph),
                                RunPattern(slots, first, last, map)));
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case EblcImageSizeSubtable size:
                    {
                        if (size.ImageSize == 0) break;
                        if (!budget.Work(size.LastGlyphIndex - size.FirstGlyphIndex + 1,
                                ebdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int g = size.FirstGlyphIndex; g <= size.LastGlyphIndex; g++)
                            slots.Add((g, images[imageCursor++]));
                        status = EmitRun(slots,
                            (first, last) => new EblcImageSizeSubtable(newSubtables.Count,
                                size.ImageFormat, 0,
                                (ushort)map.OutputOf(slots[first].Glyph),
                                (ushort)map.OutputOf(slots[last].Glyph), size.ImageSize,
                                size.BigMetrics));
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case EblcPairSubtable pairs:
                    {
                        int count = pairs.GlyphIds.Length - 1;
                        if (!budget.Work(count, ebdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        for (int k = 0; k < count; k++)
                        {
                            if (pairs.SbitOffsets[k] == pairs.SbitOffsets[k + 1]) continue;
                            slots.Add((pairs.GlyphIds[k], images[imageCursor++]));
                        }
                        status = EmitRun(slots,
                            (first, last) =>
                            {
                                var glyphs = new ushort[last - first + 2];
                                for (int s = first; s <= last; s++)
                                    glyphs[s - first] =
                                        (ushort)map.OutputOf(slots[s].Glyph);
                                glyphs[^1] = 0xFFFF;
                                var offsets = new ushort[glyphs.Length];
                                for (int s = 0; s < offsets.Length; s++)
                                    offsets[s] = (ushort)s;
                                return new EblcPairSubtable(newSubtables.Count,
                                    pairs.ImageFormat, 0,
                                    (ushort)map.OutputOf(slots[first].Glyph),
                                    (ushort)map.OutputOf(slots[last].Glyph), glyphs,
                                    offsets);
                            });
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                    case EblcGlyphIdArraySubtable array:
                    {
                        if (array.ImageSize == 0) break;
                        if (!budget.Work(array.GlyphIds.Length, ebdtAt, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        foreach (ushort glyph in array.GlyphIds)
                            slots.Add((glyph, images[imageCursor++]));
                        status = EmitRun(slots,
                            (first, last) =>
                            {
                                var glyphs = new ushort[last - first + 1];
                                for (int s = first; s <= last; s++)
                                    glyphs[s - first] =
                                        (ushort)map.OutputOf(slots[s].Glyph);
                                return new EblcGlyphIdArraySubtable(newSubtables.Count,
                                    array.ImageFormat, 0,
                                    (ushort)map.OutputOf(slots[first].Glyph),
                                    (ushort)map.OutputOf(slots[last].Glyph),
                                    array.ImageSize, array.BigMetrics, glyphs);
                            });
                        if (status != ReadStatus.Done)
                        {
                            diagnostic = emitDiagnostic;
                            return status;
                        }
                        break;
                    }
                }
            }
            if (newSubtables.Count == 0) continue;
            survivingStrikes.Add(EbscTableCompiler.PpemPair(strike.PpemX, strike.PpemY));
            newStrikes.Add(new EblcStrike(0, 0, strike.ColorRef, strike.HoriMetrics,
                strike.VertMetrics, (ushort)strikeMin, (ushort)strikeMax, strike.PpemX,
                strike.PpemY, strike.BitDepth, strike.Flags, [.. newSubtables]));
        }
        return ReadStatus.Done;
    }

    private static EbdtImage RemappedEbdtImage(EbdtImage image, GlyphRemap map,
        DataLocation ebdtAt, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (image.ImageFormat is not (8 or 9) || image.Components.Length == 0)
            return image;
        var components = new EbdtComponent[image.Components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            var component = image.Components[i];
            int output = map.OutputOf(component.GlyphId);
            if (output < 0)
            {
                _ = ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    ebdtAt.AtLocal(DataField.GlyphId, 0, i), out diagnostic,
                    component.GlyphId, 0UL);
                return image;
            }
            components[i] = new((ushort)output, component.XOffset, component.YOffset);
        }
        return new EbdtImage(image.ImageFormat, image.Metrics, image.Data,
            image.BitDepth, components, image.CompositePad);
    }

    /// <summary>EBSC rides the rebuilt EBLC strike set (SE card §2.5): the paired
    /// validated read re-answers the chain's faces (an EBSC without its EBLC partner
    /// refuses at the missing-table face — RE2), records whose substitute ppem pair
    /// no longer names a surviving strike drop, and an all-dead set drops the
    /// table.</summary>
    private static ReadStatus RebuildEbsc(SfntDirectoryView directory, MaxpTable maxp,
        HashSet<int> survivingStrikes, BuildBudget budget, out byte[]? ebscBytes,
        out bool dropped, out FontDiagnostic diagnostic)
    {
        ebscBytes = null;
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Ebsc, out var ebscAt,
            out var ebscSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadTableBytes(directory, Eblc, out var eblcAt, out var eblcSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = EblcView.TryOpen(eblcSource, out var eblcView, out diagnostic, eblcAt);
        if (status != ReadStatus.Done) return status;
        status = EbscView.TryOpen(ebscSource, out var ebscView, out diagnostic, ebscAt);
        if (status != ReadStatus.Done) return status;
        status = directory.TryFindUniqueTable(Ebdt, out int ebdtOrdinal,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(ebdtOrdinal, out var ebdtSource,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ebscView.TryReadTable(eblcView, ebdtSource.Length, maxp.NumGlyphs,
            out var ebsc, out diagnostic, budget.Limits);
        if (status != ReadStatus.Done) return status;
        var kept = new List<EbscScaleRecord>();
        foreach (var record in ebsc!.Records)
        {
            if (survivingStrikes.Contains(EbscTableCompiler.PpemPair(
                    record.SubstitutePpemX, record.SubstitutePpemY)))
                kept.Add(record);
        }
        if (kept.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = EbscTableCompiler.TryCreate(ebsc.MajorVersion, ebsc.MinorVersion, kept,
            out var newEbsc, out diagnostic, location: ebscAt);
        if (status != ReadStatus.Done) return status;
        status = EbscTableCompiler.TryCompile(newEbsc!, out var bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, ebscAt, bytes!, out ebscBytes, out diagnostic);
    }

    // ---- sbix ----

    private static ReadStatus RebuildSbix(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? sbixBytes, out bool dropped,
        out FontDiagnostic diagnostic)
    {
        sbixBytes = null;
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Sbix, out var sbixAt,
            out var sbixSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = SbixView.TryOpen(sbixSource, out var sbixView, out diagnostic, sbixAt);
        if (status != ReadStatus.Done) return status;
        status = sbixView.TryReadTable(maxp.NumGlyphs, out var sbix, out diagnostic,
            budget);
        if (status != ReadStatus.Done) return status;
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work((long)sbix!.StrikeCount * outputCount, sbixAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var newStrikes = new List<SbixStrike>();
        for (int strikeIndex = 0; strikeIndex < sbix.StrikeCount; strikeIndex++)
        {
            var strike = sbix.Strikes[strikeIndex];
            var slots = new SbixGlyph?[outputCount];
            bool any = false;
            for (int o = 0; o < outputCount; o++)
            {
                if (strike.Glyphs[inputOrder[o]] is not { } glyph)
                {
                    slots[o] = null;
                    continue;
                }
                if (glyph.GraphicType == SbixLayout.GraphicTypeDupe)
                {
                    // The dupe target remaps through the frozen map; an unselected
                    // target refuses (RE3, the TryWithGlyphPayload dangling-dupe
                    // precedent's shape).
                    int target = System.Buffers.Binary.BinaryPrimitives
                        .ReadUInt16BigEndian(glyph.Data);
                    int output = map.OutputOf(target);
                    if (output < 0)
                        return ReadWire.Fail(ReadStatus.InvalidFormat,
                            DiagnosticCode.InvalidValue,
                            sbixAt.AtLocal(DataField.GlyphId,
                                SbixSlotSourceAt(sbix.NumGlyphs, sbix.StrikeCount,
                                    strikeIndex, inputOrder[o]), inputOrder[o]),
                            out diagnostic, (ulong)target, 0UL);
                    var payload = (byte[])glyph.Data.Clone();
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                        payload.AsSpan(0), (ushort)output);
                    slots[o] = new SbixGlyph(glyph.OriginOffsetX, glyph.OriginOffsetY,
                        glyph.GraphicType, payload);
                }
                else slots[o] = glyph;
                any = true;
            }
            if (any) newStrikes.Add(new SbixStrike(strike.Ppem, strike.Ppi, slots));
        }
        // An all-strike-empty table drops (OQ-SE-5); empty strikes drop per strike.
        if (newStrikes.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = SbixTableCompiler.TryCreate(sbix.Version, sbix.Flags, outputCount,
            newStrikes, out var newSbix, out diagnostic, location: sbixAt);
        if (status != ReadStatus.Done) return status;
        status = SbixTableCompiler.TryCompile(newSbix!, out var bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, sbixAt, bytes!, out sbixBytes, out diagnostic);
    }

    /// <summary>The source wire coordinate of one strike slot's dupe target word
    /// (the read layout's own arithmetic, the RE3 diagnostic's provenance).</summary>
    private static ulong SbixSlotSourceAt(int numGlyphs, int strikeCount,
        int strikeIndex, int glyphId)
    {
        ulong at = SbixLayout.StrikeOffsetsAt + 4UL * (uint)strikeCount +
            4UL * (ulong)(uint)strikeIndex * (SbixLayout.StrikeHeaderLength +
                4UL * ((ulong)(uint)numGlyphs + 1UL));
        return at + SbixLayout.StrikeHeaderLength +
            4UL * ((ulong)(uint)numGlyphs + 1UL) + 8UL * (ulong)(uint)glyphId +
            SbixLayout.GlyphDataHeaderLength;
    }

    // ---- SVG ␠ ----

    private static ReadStatus RebuildSvg(SfntDirectoryView directory, GlyphRemap map,
        BuildBudget budget, out byte[]? svgBytes, out bool dropped,
        out FontDiagnostic diagnostic)
    {
        svgBytes = null;
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Svg, out var svgAt,
            out var svgSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = SvgView.TryOpen(svgSource, out var svgView, out diagnostic, svgAt);
        if (status != ReadStatus.Done) return status;
        status = svgView.TryReadTable(out var svg, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // The span query rides the ascending input-GID survivor array: the compact
        // map's output order can permute the input order (the CFF1 SID/CID freeze),
        // so the output-to-input order itself must never answer a span query.
        if (!budget.Work(map.OutputGlyphCount, svgAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var survivors = SortedSurvivors(map);
        var outputs = new int[survivors.Length];
        var runs = new List<(ushort Start, ushort End)>();
        // Each input row emits one wire row per maximal contiguous output run —
        // the wire row is a contiguous interval (the DocumentRecords' strict
        // ascent, V L84), so a row whose surviving outputs straddle a gap must
        // cover itself as several rows; extrema endpoints would claim the
        // in-between glyphs another row's survivors occupy. The runs are pairwise
        // disjoint across the table (a row's own runs split on a gap; different
        // rows' outputs are disjoint by the map's injectivity), but the surviving
        // rows' output order is the map permutation's, not the input rows', so
        // they sort by their output start before the factory re-answers the
        // ascent. On the monotone orders every row's survivors are
        // output-contiguous: one run per row, the sort is the identity, and the
        // traversal — the document dedup below included — replays the input order
        // with the emission byte-identical.
        var spans = new List<(ushort Start, ushort End, int PayloadIndex)>();
        foreach (var record in svg!.Records)
        {
            if (!TrySurvivingRuns(map, survivors, record.StartGlyphID,
                    record.EndGlyphID, outputs, runs))
                continue;
            foreach (var (runStart, runEnd) in runs)
                spans.Add((runStart, runEnd, record.PayloadIndex));
        }
        if (spans.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        // The document table rebuilds over the sorted traversal's first-reference
        // order: each distinct payload keeps exactly one entry, and every index the
        // sorted records carry resolves into it. The documents themselves follow the
        // remap: the spec keys each glyph's description by the document-local element
        // id "glyph<glyphID>", so a renumbered record must carry a renumbered document
        // or the output directory addresses glyph elements the document does not have.
        var newRecords = new List<SvgDocumentRecord>(spans.Count);
        var newDocuments = new List<byte[]>();
        var documentIndex = new Dictionary<int, int>();
        foreach (var (start, end, payloadIndex) in
            spans.OrderBy(static span => span.Start))
        {
            if (!documentIndex.TryGetValue(payloadIndex, out int newIndex))
            {
                newIndex = newDocuments.Count;
                documentIndex[payloadIndex] = newIndex;
                status = TryRewriteSvgDocument(svg.Documents[payloadIndex], map, svgAt,
                    budget, out var document, out diagnostic);
                if (status != ReadStatus.Done) return status;
                newDocuments.Add(document!);
            }
            newRecords.Add(new(start, end, newIndex));
        }
        status = SvgTableCompiler.TryCreate(svg.Version, 0, newRecords, newDocuments,
            out var newSvg, out diagnostic, location: svgAt);
        if (status != ReadStatus.Done) return status;
        status = SvgTableCompiler.TryCompile(newSvg!, out var bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, svgAt, bytes!, out svgBytes, out diagnostic);
    }

    // ---- SVG document glyph IDs ----

    // The document's glyph identity is the spec's element id "glyph<glyphID>" (a
    // non-zero-padded decimal glyph ID), and a document refers to another glyph's
    // element through an href fragment "#glyph<glyphID>". One compact remap moves both
    // carriers or the output's records address elements the document no longer has.
    // The document bytes stay opaque to the table model (never parsed, gzip members
    // verbatim), so the rewrite is a byte-level splice over those two carriers: plain
    // documents keep their encoding (the spec requires UTF-8), and a gzip member is
    // inflated, rewritten and deflated again. A document with no glyph carrier travels
    // byte-for-byte; one whose ids the map drops renames them out of the glyph-id space
    // ("glyph_<old>") and rewrites the references that point at them, so a leftover
    // element can never collide with a surviving glyph's new id.
    private static ReadStatus TryRewriteSvgDocument(byte[] document, GlyphRemap map,
        DataLocation at, BuildBudget budget, out byte[]? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        bool gzip = document.Length >= 3 && document[0] == 0x1F &&
            document[1] == 0x8B && document[2] == 0x08;
        byte[]? text = document;
        if (gzip)
        {
            ReadStatus inflate = TryInflateSvgDocument(document, at, budget, out text,
                out diagnostic);
            if (inflate != ReadStatus.Done) return inflate;
        }
        if (!budget.Work(text!.Length, at, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus splice = TrySpliceSvgGlyphIds(text, map, at, out byte[]? spliced,
            out diagnostic);
        if (splice != ReadStatus.Done) return splice;
        if (spliced is null)
        {
            // The common case (the map is the identity over the document's glyphs, or
            // the document carries no glyph carrier at all) keeps the source bytes.
            rewritten = document;
            diagnostic = default;
            return ReadStatus.Done;
        }
        rewritten = gzip ? DeflateSvgDocument(spliced) : spliced;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The rewrite is XML-context aware: only real element attributes are values, while
    // comments, CDATA, processing instructions and end tags are skipped. An id
    // attribute's complete value and each '#' fragment inside a reference value are
    // decoded (character references included) and matched as a whole ID, so
    // "glyph4.clip" is a different ID and "#glyph4" still resolves. The same ID map
    // covers the ID selectors and url() fragments of an internal style sheet.
    private static ReadStatus TrySpliceSvgGlyphIds(byte[] text, GlyphRemap map,
        DataLocation at, out byte[]? spliced, out FontDiagnostic diagnostic)
    {
        spliced = null;
        diagnostic = default;
        var values = new List<SvgAttributeValue>();
        var styles = new List<(int Start, int End)>();
        ReadSvgStructure(text, values, styles);
        // Existing IDs are the rename namespace: a dropped glyph's placeholder must not
        // collide with a resource id (nor with another placeholder).
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!IsSvgIdAttributeValue(text, value)) continue;
            if (TryDecodeSvgSpan(text, value.ValueStart, value.ValueEnd, out var chars))
                usedIds.Add(SvgCharsToString(chars));
        }
        var droppedNames = new Dictionary<int, string>();
        var edits = new List<(int Start, int End, byte[] Replacement)>();
        foreach (var value in values)
        {
            if (!TryDecodeSvgSpan(text, value.ValueStart, value.ValueEnd, out var chars))
                continue;
            if (IsSvgIdAttributeValue(text, value))
            {
                string id = SvgCharsToString(chars);
                AddSvgIdEdit(text, value.ValueStart, value.ValueEnd, id, map, usedIds,
                    droppedNames, edits);
                continue;
            }
            if (IsSvgStyleAttributeValue(text, value) && HasSvgBackslash(chars))
                return SvgStyleUnsupported(at, chars, out diagnostic);
            int cursor = 0;
            while (cursor < chars.Count)
            {
                if (chars[cursor].Value != '#')
                {
                    cursor++;
                    continue;
                }
                int runStart = cursor + 1;
                int runEnd = SvgDecodedEnd(chars, runStart, IsSvgXmlNameChar);
                if (runEnd > runStart)
                    AddSvgIdEdit(text, chars, runStart, runEnd, map, usedIds, droppedNames,
                        edits);
                cursor = System.Math.Max(runEnd, runStart);
            }
        }
        foreach (var (start, end) in styles)
        {
            ReadStatus styleStatus = RewriteSvgStyleSheet(text, start, end, map, usedIds,
                droppedNames, edits, at, out diagnostic);
            if (styleStatus != ReadStatus.Done) return styleStatus;
        }
        if (edits.Count == 0) return ReadStatus.Done;
        edits.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        int length = text.Length;
        foreach (var (start, end, replacement) in edits)
            length += replacement.Length - (end - start);
        var buffer = new byte[length];
        int source = 0, target = 0;
        foreach (var (start, end, replacement) in edits)
        {
            int run = start - source;
            text.AsSpan(source, run).CopyTo(buffer.AsSpan(target));
            target += run;
            replacement.CopyTo(buffer.AsSpan(target));
            target += replacement.Length;
            source = end;
        }
        text.AsSpan(source).CopyTo(buffer.AsSpan(target));
        spliced = buffer;
        return ReadStatus.Done;
    }

    // One tolerant XML walk: it recognizes start tags and their attribute values,
    // skips comments, CDATA, processing instructions and end tags, and records the raw
    // content of every <style> element. A malformed construct ends the walk, so nothing
    // past it is rewritten from a guessed context.
    private static void ReadSvgStructure(byte[] text, List<SvgAttributeValue> values,
        List<(int Start, int End)> styles)
    {
        int at = 0;
        while (at < text.Length)
        {
            int open = SvgIndexOf(text, (byte)'<', at, text.Length);
            if (open < 0) break;
            at = open;
            if (SvgStartsWith(text, at, "<!--"))
            {
                int end = SvgIndexOfSequence(text, at + 4, "-->");
                at = end < 0 ? text.Length : end + 3;
                continue;
            }
            if (SvgStartsWith(text, at, "<![CDATA["))
            {
                int end = SvgIndexOfSequence(text, at + 9, "]]>");
                at = end < 0 ? text.Length : end + 3;
                continue;
            }
            if (SvgStartsWith(text, at, "<?"))
            {
                int end = SvgIndexOfSequence(text, at + 2, "?>");
                at = end < 0 ? text.Length : end + 2;
                continue;
            }
            if (SvgStartsWith(text, at, "<!"))
            {
                int end = SvgIndexOf(text, (byte)'>', at + 2, text.Length);
                at = end < 0 ? text.Length : end + 1;
                continue;
            }
            if (SvgStartsWith(text, at, "</"))
            {
                int end = SvgIndexOf(text, (byte)'>', at + 2, text.Length);
                at = end < 0 ? text.Length : end + 1;
                continue;
            }
            int nameStart = at + 1;
            if (nameStart >= text.Length || !IsSvgNameByte(text[nameStart]))
            {
                at++;
                continue;
            }
            int cursor = nameStart;
            while (cursor < text.Length && IsSvgNameByte(text[cursor])) cursor++;
            bool isStyle = SvgNameEquals(text, nameStart, cursor, "style");
            bool selfClosing = false;
            bool complete = true;
            while (cursor < text.Length)
            {
                while (cursor < text.Length && IsSvgXmlSpace(text[cursor])) cursor++;
                if (cursor >= text.Length) { complete = false; break; }
                if (text[cursor] == (byte)'>')
                {
                    cursor++;
                    break;
                }
                if (text[cursor] == (byte)'/' && cursor + 1 < text.Length &&
                    text[cursor + 1] == (byte)'>')
                {
                    cursor += 2;
                    selfClosing = true;
                    break;
                }
                int attrStart = cursor;
                while (cursor < text.Length && IsSvgAttributeNameByte(text[cursor])) cursor++;
                if (cursor == attrStart)
                {
                    cursor++;
                    continue;
                }
                int attrEnd = cursor;
                while (cursor < text.Length && IsSvgXmlSpace(text[cursor])) cursor++;
                if (cursor >= text.Length || text[cursor] != (byte)'=') continue;
                cursor++;
                while (cursor < text.Length && IsSvgXmlSpace(text[cursor])) cursor++;
                if (cursor >= text.Length ||
                    (text[cursor] != (byte)'"' && text[cursor] != (byte)'\''))
                {
                    cursor++;
                    continue;
                }
                byte quote = text[cursor];
                int valueStart = cursor + 1;
                int valueEnd = valueStart;
                while (valueEnd < text.Length && text[valueEnd] != quote) valueEnd++;
                if (valueEnd >= text.Length)
                {
                    complete = false;
                    cursor = text.Length;
                    break;
                }
                values.Add(new(attrStart, attrEnd, valueStart, valueEnd));
                cursor = valueEnd + 1;
            }
            if (!complete) break;
            if (!selfClosing && isStyle)
            {
                int close = SvgIndexOfSequence(text, cursor, "</style");
                int contentEnd = close < 0 ? text.Length : close;
                styles.Add((cursor, contentEnd));
                cursor = contentEnd;
            }
            at = cursor;
        }
    }

    // ---- internal style sheets ----

    // Rewrites the ID carriers a CSS sheet can use: "#ident" selectors, "#ident"
    // fragments inside url(), and "[id=value]" attribute selectors. A backslash is a CSS
    // escape the byte-level map cannot follow, so a sheet using one refuses instead of
    // emitting a selector that no longer matches.
    private static ReadStatus RewriteSvgStyleSheet(byte[] text, int start, int end,
        GlyphRemap map, HashSet<string> usedIds, Dictionary<int, string> droppedNames,
        List<(int Start, int End, byte[] Replacement)> edits, DataLocation at,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (!TryDecodeSvgSpan(text, start, end, out var chars)) return ReadStatus.Done;
        if (HasSvgBackslash(chars)) return SvgStyleUnsupported(at, chars, out diagnostic);
        int cursor = 0;
        while (cursor < chars.Count)
        {
            char c = chars[cursor].Value;
            if (c == '/' && cursor + 1 < chars.Count && chars[cursor + 1].Value == '*')
            {
                int close = SvgIndexOfChars(chars, cursor + 2, "*/");
                cursor = close < 0 ? chars.Count : close + 2;
                continue;
            }
            if (c is '"' or '\'')
            {
                cursor = SvgCssStringEnd(chars, cursor);
                continue;
            }
            if (TrySvgCssUrlOpen(chars, cursor, out int urlBody))
            {
                int close = SvgIndexOfChar(chars, urlBody, ')');
                int stop = close < 0 ? chars.Count : close;
                int inner = urlBody;
                while (inner < stop)
                {
                    if (chars[inner].Value == '#')
                    {
                        int runStart = inner + 1;
                        int runEnd = SvgDecodedEnd(chars, runStart, IsSvgXmlNameChar, stop);
                        if (runEnd > runStart)
                            AddSvgIdEdit(text, chars, runStart, runEnd, map, usedIds,
                                droppedNames, edits);
                        inner = System.Math.Max(runEnd, runStart);
                        continue;
                    }
                    inner++;
                }
                cursor = close < 0 ? chars.Count : close + 1;
                continue;
            }
            if (c == '[')
            {
                int next = TrySvgCssIdAttributeSelector(chars, cursor + 1, text,
                    map, usedIds, droppedNames, edits);
                if (next > cursor + 1)
                {
                    cursor = next;
                    continue;
                }
                cursor++;
                continue;
            }
            if (c == '#')
            {
                int runStart = cursor + 1;
                int runEnd = SvgDecodedEnd(chars, runStart, IsSvgCssIdentChar);
                if (runEnd > runStart)
                    AddSvgIdEdit(text, chars, runStart, runEnd, map, usedIds, droppedNames,
                        edits);
                cursor = System.Math.Max(runEnd, runStart);
                continue;
            }
            cursor++;
        }
        return ReadStatus.Done;
    }

    // "[id = value]" with an optional quote pair; returns the position after the
    // selector, or the unchanged input when the shape is not that selector.
    private static int TrySvgCssIdAttributeSelector(List<SvgChar> chars, int cursor,
        byte[] text, GlyphRemap map, HashSet<string> usedIds,
        Dictionary<int, string> droppedNames,
        List<(int Start, int End, byte[] Replacement)> edits)
    {
        int at = cursor;
        at = SvgSkipCssSpace(chars, at);
        if (!SvgCharsMatch(chars, at, "id")) return cursor;
        at += 2;
        at = SvgSkipCssSpace(chars, at);
        if (at >= chars.Count || chars[at].Value != '=') return cursor;
        at++;
        at = SvgSkipCssSpace(chars, at);
        if (at >= chars.Count) return cursor;
        int valueStart, valueEnd;
        if (chars[at].Value is '"' or '\'')
        {
            valueStart = at + 1;
            valueEnd = SvgCssStringEnd(chars, at) - 1;
            if (valueEnd < valueStart) return cursor;
            at = valueEnd + 2;
        }
        else
        {
            valueStart = at;
            valueEnd = SvgDecodedEnd(chars, at, IsSvgCssIdentChar);
            if (valueEnd == valueStart) return cursor;
            at = valueEnd;
        }
        AddSvgIdEdit(text, chars, valueStart, valueEnd, map, usedIds, droppedNames, edits);
        return at;
    }

    private static int SvgSkipCssSpace(List<SvgChar> chars, int at)
    {
        while (at < chars.Count && chars[at].Value is ' ' or '\t' or '\r' or '\n') at++;
        return at;
    }

    private static int SvgCssStringEnd(List<SvgChar> chars, int open)
    {
        char quote = chars[open].Value;
        int at = open + 1;
        while (at < chars.Count && chars[at].Value != quote) at++;
        return System.Math.Min(at + 1, chars.Count);
    }

    private static bool TrySvgCssUrlOpen(List<SvgChar> chars, int at, out int body)
    {
        body = at;
        if (!SvgCharsMatchIgnoreCase(chars, at, "url")) return false;
        int cursor = at + 3;
        cursor = SvgSkipCssSpace(chars, cursor);
        if (cursor >= chars.Count || chars[cursor].Value != '(') return false;
        body = cursor + 1;
        return true;
    }

    private static bool SvgCharsMatch(List<SvgChar> chars, int at, string value)
    {
        if (at < 0 || at + value.Length > chars.Count) return false;
        for (int i = 0; i < value.Length; i++)
            if (chars[at + i].Value != value[i]) return false;
        return true;
    }

    private static bool SvgCharsMatchIgnoreCase(List<SvgChar> chars, int at, string value)
    {
        if (at < 0 || at + value.Length > chars.Count) return false;
        for (int i = 0; i < value.Length; i++)
            if (char.ToLowerInvariant(chars[at + i].Value) != value[i]) return false;
        return true;
    }

    private static int SvgDecodedEnd(List<SvgChar> chars, int at,
        Func<char, bool> isName, int end = int.MaxValue)
    {
        int limit = System.Math.Min(end, chars.Count);
        while (at < limit && isName(chars[at].Value)) at++;
        return at;
    }

    private static bool IsSvgCssIdentChar(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' ||
        value >= '\u0080';

    private static bool IsSvgXmlNameChar(char value) =>
        IsSvgCssIdentChar(value) || value is '.' or ':';

    private static bool HasSvgBackslash(List<SvgChar> chars)
    {
        foreach (var c in chars)
            if (c.Value == '\\') return true;
        return false;
    }

    // The style sheet could not be mapped safely; the table's records are still
    // rewriting, so the whole document rewrite refuses rather than emitting a
    // selector that no longer addresses its element.
    private static ReadStatus SvgStyleUnsupported(DataLocation at, List<SvgChar> chars,
        out FontDiagnostic diagnostic)
    {
        int offset = chars.Count == 0 ? 0 : chars[0].RawStart;
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            at.AtLocal(DataField.Payload, (ulong)offset), out diagnostic, '\\', 0);
    }

    private static int SvgIndexOfChar(List<SvgChar> chars, int at, char needle)
    {
        while (at < chars.Count && chars[at].Value != needle) at++;
        return at < chars.Count ? at : -1;
    }

    private static int SvgIndexOfChars(List<SvgChar> chars, int at, string value)
    {
        for (; at + value.Length <= chars.Count; at++)
            if (SvgCharsMatch(chars, at, value)) return at;
        return -1;
    }

    // ---- attribute values ----

    // One attribute value's byte spans; the name is [NameStart, NameEnd).
    private readonly record struct SvgAttributeValue(int NameStart, int NameEnd,
        int ValueStart, int ValueEnd);

    private static bool IsSvgIdAttributeValue(byte[] text, SvgAttributeValue value) =>
        SvgNameEquals(text, value.NameStart, value.NameEnd, "id");

    // The inline style attribute carries CSS declarations; a CSS escape in it has the
    // same unsafe mapping as in an internal sheet.
    private static bool IsSvgStyleAttributeValue(byte[] text, SvgAttributeValue value) =>
        SvgNameEquals(text, value.NameStart, value.NameEnd, "style");

    private static bool SvgNameEquals(byte[] text, int start, int end, string name)
    {
        if (end - start != name.Length) return false;
        for (int i = 0; i < name.Length; i++)
            if (text[start + i] != (byte)name[i]) return false;
        return true;
    }

    // XML name characters: non-ASCII bytes are part of a Unicode name, so an ID such as
    // "glyph4剪裁" is one name and never a "glyph4" prefix.
    private static bool IsSvgNameByte(byte value) =>
        value is >= (byte)'a' and <= (byte)'z' || value is >= (byte)'A' and <= (byte)'Z' ||
        value is >= (byte)'0' and <= (byte)'9' || value == (byte)'-' || value == (byte)'_' ||
        value == (byte)':' || value == (byte)'.' || value >= 0x80;

    private static bool IsSvgAttributeNameByte(byte value) =>
        IsSvgNameByte(value) && value != (byte)'=' && value != (byte)'/' && value != (byte)'>';

    private static bool IsSvgXmlSpace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static bool SvgStartsWith(byte[] text, int at, string value)
    {
        if (at < 0 || at + value.Length > text.Length) return false;
        for (int i = 0; i < value.Length; i++)
            if (text[at + i] != (byte)value[i]) return false;
        return true;
    }

    private static int SvgIndexOfSequence(byte[] text, int at, string value)
    {
        for (; at + value.Length <= text.Length; at++)
            if (SvgStartsWith(text, at, value)) return at;
        return -1;
    }

    private static int SvgIndexOf(byte[] text, byte needle, int at, int end)
    {
        while (at < end && text[at] != needle) at++;
        return at < end ? at : -1;
    }

    // One decoded character and the raw byte span it came from; a character reference
    // maps one decoded char to its whole "&#...;" span, so a rewrite replaces exactly
    // the bytes that produced the ID.
    private readonly record struct SvgChar(char Value, int RawStart, int RawEnd);

    private static bool TryDecodeSvgSpan(byte[] text, int start, int end,
        out List<SvgChar> chars)
    {
        chars = new List<SvgChar>(end - start);
        for (int at = start; at < end; at++)
        {
            if (text[at] != (byte)'&')
            {
                chars.Add(new((char)text[at], at, at + 1));
                continue;
            }
            if (!TryParseSvgCharRef(text, at, end, out char value, out int after))
                return false;
            chars.Add(new(value, at, after));
            at = after - 1;
        }
        return true;
    }

    private static string SvgCharsToString(List<SvgChar> chars)
    {
        var builder = new StringBuilder(chars.Count);
        foreach (var c in chars) builder.Append(c.Value);
        return builder.ToString();
    }

    private static void AddSvgIdEdit(byte[] text, List<SvgChar> chars, int decodedStart,
        int decodedEnd, GlyphRemap map, HashSet<string> usedIds,
        Dictionary<int, string> droppedNames,
        List<(int Start, int End, byte[] Replacement)> edits)
    {
        string id = SvgCharsToString(chars.GetRange(decodedStart, decodedEnd - decodedStart));
        AddSvgIdEdit(text, chars[decodedStart].RawStart, chars[decodedEnd - 1].RawEnd, id,
            map, usedIds, droppedNames, edits);
    }

    private static void AddSvgIdEdit(byte[] text, int rawStart, int rawEnd, string id,
        GlyphRemap map, HashSet<string> usedIds, Dictionary<int, string> droppedNames,
        List<(int Start, int End, byte[] Replacement)> edits)
    {
        if (!TryResolveSvgGlyphId(id, map, usedIds, droppedNames, out byte[]? token)) return;
        if (SvgSpanEquals(text, rawStart, rawEnd, token!)) return;
        edits.Add((rawStart, rawEnd, token!));
    }

    private static bool SvgSpanEquals(byte[] text, int start, int end, byte[] token) =>
        end - start == token.Length && text.AsSpan(start, end - start).SequenceEqual(token);

    // "glyph<decimal>" exactly, canonical (no leading zeros) and inside the font's GID
    // domain. A surviving glyph answers its output id; a dropped one gets a placeholder
    // that leaves the glyph-id space and is unique within the document.
    private static bool TryResolveSvgGlyphId(string id, GlyphRemap map, HashSet<string> usedIds,
        Dictionary<int, string> droppedNames, out byte[]? token)
    {
        token = null;
        if (id.Length < 6 || !id.StartsWith("glyph", StringComparison.Ordinal)) return false;
        if (id.Length - 5 > 1 && id[5] == '0') return false;
        int glyphId = 0;
        for (int i = 5; i < id.Length; i++)
        {
            if (id[i] is < '0' or > '9') return false;
            glyphId = glyphId * 10 + (id[i] - '0');
            if (glyphId > ushort.MaxValue) return false;
        }
        if (glyphId >= map.InputGlyphCount) return false;
        int output = map.OutputOf(glyphId);
        string name;
        if (output >= 0)
        {
            name = $"glyph{output}";
        }
        else if (droppedNames.TryGetValue(glyphId, out string? existing))
        {
            name = existing;
        }
        else
        {
            // The placeholder leaves the glyph-id space; the used-ID probe keeps an
            // existing resource of the same name (or another placeholder) intact.
            name = $"glyph_{glyphId}";
            while (!usedIds.Add(name)) name += "_";
            droppedNames[glyphId] = name;
        }
        token = Encoding.ASCII.GetBytes(name);
        return true;
    }

    // "&#52;", "&#x34;" and the five predefined XML entities. Numeric code points map
    // to one char (non-BMP values to the replacement char: they cannot occur in a
    // glyph id, but the decoded value still has to be a faithful non-match).
    private static bool TryParseSvgCharRef(byte[] text, int at, int end, out char value,
        out int after)
    {
        value = '\0';
        after = at;
        if (at + 1 >= end || text[at] != (byte)'&') return false;
        int i = at + 1;
        if (text[i] == (byte)'#')
        {
            i++;
            bool hex = i < end && text[i] is (byte)'x' or (byte)'X';
            if (hex) i++;
            int digitsAt = i;
            long code = 0;
            while (i < end)
            {
                int digit;
                if (hex)
                {
                    if (!IsSvgHexDigit(text[i])) break;
                    digit = SvgHexValue(text[i]);
                }
                else
                {
                    if (text[i] is < (byte)'0' or > (byte)'9') break;
                    digit = text[i] - (byte)'0';
                }
                code = code * (hex ? 16 : 10) + digit;
                if (code > 0x10FFFF) return false;
                i++;
            }
            if (i == digitsAt || i >= end || text[i] != (byte)';') return false;
            if (code is >= 0xD800 and <= 0xDFFF) return false;
            value = code <= char.MaxValue ? (char)code : '\uFFFD';
            after = i + 1;
            return true;
        }
        int semi = i;
        while (semi < end && semi - i <= 4 && IsSvgAttributeNameByte(text[semi])) semi++;
        if (semi >= end || text[semi] != (byte)';') return false;
        string name = Encoding.ASCII.GetString(text, i, semi - i);
        value = name switch
        {
            "amp" => '&', "lt" => '<', "gt" => '>', "quot" => '"', "apos" => '\'',
            _ => '\0'
        };
        if (value == '\0') return false;
        after = semi + 1;
        return true;
    }

    private static bool IsSvgHexDigit(byte value) =>
        value is >= (byte)'0' and <= (byte)'9' || value is >= (byte)'a' and <= (byte)'f' ||
        value is >= (byte)'A' and <= (byte)'F';

    private static int SvgHexValue(byte value) => value switch
    {
        <= (byte)'9' => value - (byte)'0',
        <= (byte)'F' => value - (byte)'A' + 10,
        _ => value - (byte)'a' + 10
    };

    // gzip members are inflated only to rewrite their glyph IDs; the table model's own
    // bytes still travel verbatim (the spec requires UTF-8 inside the member). The
    // decoded size is bounded by the operation's temporary budget before it is charged.
    private static ReadStatus TryInflateSvgDocument(byte[] document, DataLocation at,
        BuildBudget budget, out byte[]? text, out FontDiagnostic diagnostic)
    {
        text = null;
        long limit = budget.Limits.MaxTemporaryBytes;
        try
        {
            using var input = new MemoryStream(document, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (output.Length + read > limit)
                    return ReadWire.Fail(ReadStatus.ResourceLimit,
                        DiagnosticCode.BudgetExceeded, at.AtLocal(DataField.TemporaryBytes, 0),
                        out diagnostic, (ulong)output.Length + (ulong)read, (ulong)limit);
                output.Write(chunk, 0, read);
            }
            text = output.ToArray();
        }
        catch (InvalidDataException)
        {
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                at.AtLocal(DataField.Payload, 0), out diagnostic);
        }
        if (!budget.Buffer(text.Length, at, out diagnostic)) return ReadStatus.ResourceLimit;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static byte[] DeflateSvgDocument(byte[] text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(text, 0, text.Length);
        return output.ToArray();
    }
}
